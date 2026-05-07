using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hypermania.Game;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Hosting;
using Hypermania.CPU.Policy;
using TorchSharp;
using static TorchSharp.torch;

namespace Hypermania.CPU.Training
{
    // Vectorized self-play: holds N independent TrainingEnvs and steps them in
    // lockstep, batching every torch forward pass into one [K, ObsDim] tensor
    // (K = envs currently in Fighting mode AND not yet terminated). On CUDA
    // this is the order-of-magnitude speedup over the sequential per-env path
    // because batch=1 inference is dominated by kernel-launch + stream-sync
    // overhead. The policy + opponent each see one fused forward per step
    // instead of N.
    //
    // Frames in non-Fighting modes (Countdown, ManiaStart, Mania, RoundEnd,
    // End-transition) skip torch entirely - both fighters get InputFlags.None
    // and the env advances. Rewards earned on those frames carry forward and
    // flush onto the next appended Fighting step (or the last step at episode
    // termination), so terminal win/loss bonuses aren't dropped.
    //
    // Episodes always run until GameMode.End - the round timer terminates
    // matches naturally, no maxFrames cap.
    //
    // Per-env GRU hidden state: each env owns a column in [1, N_envs, hidden]
    // tensors (one for the learner, one for the neural opponent if any).
    // Active-env subsets are gathered with index_select before each forward
    // and scattered back with index_copy_ after, so non-active envs keep
    // their previous hidden across non-Fighting frames.
    public sealed class BatchedSelfPlayEnv
    {
        readonly TrainingEnv[] _envs;
        readonly RewardShaper[] _shapers;
        readonly SimOptions _options;
        readonly Device _device;
        readonly int _envCount;
        readonly int _obsDim;
        readonly int _stateDim;
        readonly int _historyDim;
        readonly int _historyLen;
        // Reaction-time latency in Fighting frames. The policy at frame t reads
        // the obs captured at frame t-N. 0 = no latency. Sets the ring depth.
        readonly int _latencyFrames;
        readonly int _ringSlots; // = _latencyFrames + 1

        // Per-env ring buffers of past Featurizer state encodings. Slot index
        // for Fighting frame f is `f mod _ringSlots`. Two parallel rings
        // because the learner and opponent see flipped perspectives. Layout:
        // _learnerObsHistory[(e * _ringSlots + slot) * _stateDim + j]. State
        // only - the action-history block is appended at policy-input time so
        // delayed obs aren't paired with stale history snapshots.
        readonly float[] _learnerObsHistory;
        readonly float[] _oppObsHistory;

        // Per-env action history. For each env we keep the last _historyLen
        // (dir, btn) pairs the learner and opponent actually committed,
        // oldest-first when read in head-relative order. _historyHead[e] is
        // the next-write index; reading from (head + i) % _historyLen for
        // i in [0, _historyLen) yields oldest..newest. Initialized to -1
        // ("no action yet") at every Run.
        readonly int[] _learnerDirRing;
        readonly int[] _learnerBtnRing;
        readonly int[] _oppDirRing;
        readonly int[] _oppBtnRing;
        readonly int[] _historyHead;
        // Per-env scratch ordered oldest..newest, reused each step instead of
        // allocating during the parallel feature loop.
        readonly int[] _learnerDirOrdered;
        readonly int[] _learnerBtnOrdered;
        readonly int[] _oppDirOrdered;
        readonly int[] _oppBtnOrdered;

        // Per-env ring of pending decisions from non-neural opponents. The
        // bot decides on the live state at frame t but its decision isn't
        // applied until t + _latencyFrames - same delayed-MDP semantics the
        // learner experiences on its obs side. Layout matches the obs ring:
        // slot index for Fighting frame f is f mod _ringSlots. Unused for
        // neural opponents (their delay is baked into the obs ring).
        readonly InputFlags[] _oppActionRing;

        // Pre-allocated CPU staging buffers, sized for the worst case (all
        // envs Fighting). Each step we fill the prefix [0, K * obsDim) and
        // narrow the resulting tensor to [K, obsDim] before transferring to
        // the device.
        readonly float[] _learnerObsBatch;
        readonly float[] _oppObsBatch;
        // Persistent CPU tensors that wrap the staging buffers. Allocated
        // once; each step we copy the active prefix in via MemoryMarshal and
        // .to(device) produces the GPU view. Avoids the per-step
        // torch.tensor() allocation + managed-to-native copy that the rollout
        // was paying twice per sim frame.
        readonly Tensor _learnerObsCpuT;
        readonly Tensor _oppObsCpuT;
        // One row per env so parallel workers in the step loop can each call
        // RewardShaper.AfterStep without contending on a shared scratch buffer.
        readonly RewardBreakdown[][] _breakdownBufs;

        // Per-step scratch for active-env indices - reused across Run calls.
        readonly long[] _activeIdsLong;

        public int EnvCount => _envCount;

        // Per-phase wall-clock from the most recent Run, summed across all
        // sim steps within the run. The trainer surfaces these in metrics so
        // we can tell whether rollout time is going to featurization, the two
        // policy forwards, or the parallel sim step.
        public TimeSpan LastFeaturizeTime { get; private set; }
        public TimeSpan LastForwardTime { get; private set; }
        public TimeSpan LastStepTime { get; private set; }

        public BatchedSelfPlayEnv(
            SimOptions options,
            RewardConfig rewardCfg,
            Device device,
            int envCount,
            int reactionLatencyFrames = 0
        )
        {
            if (envCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(envCount));
            if (reactionLatencyFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(reactionLatencyFrames));
            _options = options;
            _device = device ?? torch.CPU;
            _envCount = envCount;
            _obsDim = Featurizer.Length;
            _stateDim = Featurizer.StateLength;
            _historyDim = Featurizer.HistoryLength;
            _historyLen = Featurizer.ActionHistoryFrames;
            _latencyFrames = reactionLatencyFrames;
            _ringSlots = reactionLatencyFrames + 1;
            _envs = new TrainingEnv[envCount];
            _shapers = new RewardShaper[envCount];
            for (int i = 0; i < envCount; i++)
            {
                _envs[i] = new TrainingEnv(options);
                _shapers[i] = new RewardShaper(options, rewardCfg);
            }
            _learnerObsBatch = new float[envCount * _obsDim];
            _oppObsBatch = new float[envCount * _obsDim];
            _learnerObsHistory = new float[envCount * _ringSlots * _stateDim];
            _oppObsHistory = new float[envCount * _ringSlots * _stateDim];
            _learnerObsCpuT = zeros(new long[] { envCount, _obsDim }, ScalarType.Float32);
            _oppObsCpuT = zeros(new long[] { envCount, _obsDim }, ScalarType.Float32);
            _learnerDirRing = new int[envCount * _historyLen];
            _learnerBtnRing = new int[envCount * _historyLen];
            _oppDirRing = new int[envCount * _historyLen];
            _oppBtnRing = new int[envCount * _historyLen];
            _historyHead = new int[envCount];
            _learnerDirOrdered = new int[envCount * _historyLen];
            _learnerBtnOrdered = new int[envCount * _historyLen];
            _oppDirOrdered = new int[envCount * _historyLen];
            _oppBtnOrdered = new int[envCount * _historyLen];
            _oppActionRing = new InputFlags[envCount * _ringSlots];
            _breakdownBufs = new RewardBreakdown[envCount][];
            for (int i = 0; i < envCount; i++)
                _breakdownBufs[i] = new RewardBreakdown[options.Players.Length];
            _activeIdsLong = new long[envCount];
        }

        // Run all N envs to GameMode.End in parallel. learnerIndices[e] picks
        // which fighter slot the learner controls in env e (the opponent
        // controls the other). Returns N trajectories (one per env) and N
        // EpisodeResults.
        public (Trajectory[] trajs, EpisodeResult[] results) Run(
            int[] learnerIndices,
            PolicyNet learner,
            IPolicy opponent,
            Generator sampleGen = null,
            bool greedyOpponent = false
        )
        {
            if (learnerIndices == null || learnerIndices.Length != _envCount)
                throw new ArgumentException(
                    $"learnerIndices length must equal envCount ({_envCount})",
                    nameof(learnerIndices)
                );

            // Neural opponent goes through the fused SampleBatchPair path
            // (one CUDA stream sync). Anything else (scripted bots, RandomPolicy)
            // runs the learner forward solo and gets opp actions per-env via
            // IPolicy.Act / ActSample.
            NeuralPolicy oppNeural = opponent as NeuralPolicy;
            bool oppIsNeural = oppNeural != null;

            Trajectory[] trajs = new Trajectory[_envCount];
            EpisodeResult[] results = new EpisodeResult[_envCount];
            int[] frames = new int[_envCount];
            // Per-env Fighting-frame counter. Indexes the obs ring; non-Fighting
            // frames don't bump it, so the lookback always crosses N actual
            // Fighting frames regardless of intervening Mania/RoundEnd gaps.
            int[] fightFrame = new int[_envCount];
            float[] runningReward = new float[_envCount];
            float[] pendingReward = new float[_envCount];
            bool[] done = new bool[_envCount];

            for (int e = 0; e < _envCount; e++)
            {
                if ((uint)learnerIndices[e] >= 2)
                    throw new ArgumentOutOfRangeException(nameof(learnerIndices));
                _envs[e].Reset();
                _shapers[e].Reset();
                trajs[e] = new Trajectory(_obsDim);
                results[e] = new EpisodeResult
                {
                    LearnerIndex = learnerIndices[e],
                    OpponentLabel = OpponentLabels.For(opponent),
                };
                _historyHead[e] = 0;
            }
            // Reset action rings to "no action yet" sentinel so the first
            // few frames see all-zeros for past actions instead of leftover
            // values from the previous Run.
            Array.Fill(_learnerDirRing, -1);
            Array.Fill(_learnerBtnRing, -1);
            Array.Fill(_oppDirRing, -1);
            Array.Fill(_oppBtnRing, -1);
            // Pending non-neural decisions reset to None so the first
            // _latencyFrames frames consume "no-op" placeholders.
            Array.Fill(_oppActionRing, InputFlags.None);

            // Per-env GRU hidden state. Allocated fresh each Run so the
            // shape always matches the current learner.Hidden, and disposed
            // at the end of Run so we don't accumulate device memory across
            // updates. Opponent hidden is only allocated for neural opponents.
            using Tensor learnerHidden = learner.InitHidden(_envCount, _device);
            Tensor oppHidden = oppIsNeural
                ? oppNeural.Net.InitHidden(_envCount, _device)
                : null;

            // Per-step scratch.
            int[] activeFightingIds = new int[_envCount];
            int[] envToBatchIdx = new int[_envCount];
            bool[] wasFighting = new bool[_envCount];

            Stopwatch featSw = new Stopwatch();
            Stopwatch fwdSw = new Stopwatch();
            Stopwatch stepSw = new Stopwatch();

            try
            {
                int activeCount = _envCount;
                while (activeCount > 0)
                {
                    // Phase 1: classify envs. activeFighting = active and in Fighting
                    // mode (the only frames the policy actually needs to evaluate).
                    int afn = 0;
                    for (int e = 0; e < _envCount; e++)
                    {
                        envToBatchIdx[e] = -1;
                        if (done[e])
                        {
                            wasFighting[e] = false;
                            continue;
                        }
                        bool fighting = _envs[e].State.GameMode == GameMode.Fighting;
                        wasFighting[e] = fighting;
                        if (fighting)
                        {
                            envToBatchIdx[e] = afn;
                            activeFightingIds[afn++] = e;
                        }
                    }

                    int[] dir = null;
                    int[] btn = null;
                    float[] logp = null;
                    float[] value = null;
                    int[] oppDir = null;
                    int[] oppBtn = null;

                    // Phase 2 + 3: encode both perspectives and run two batched
                    // forward passes (learner + opponent). Skipped entirely on
                    // steps where no env is in Fighting mode.
                    if (afn > 0)
                    {
                        featSw.Start();
                        Parallel.For(0, afn, i =>
                        {
                            int e = activeFightingIds[i];
                            int writeSlot = fightFrame[e] % _ringSlots;
                            int writeBase = (e * _ringSlots + writeSlot) * _stateDim;

                            // Previous-frame action indices, looked up at
                            // (head - 1) mod len for each fighter slot. -1 on
                            // the first Fighting frame of the episode.
                            int head = _historyHead[e];
                            int prevIdx = (head - 1 + _historyLen) % _historyLen;
                            int learnerRingBase = e * _historyLen;
                            int prevDirLearner = _learnerDirRing[learnerRingBase + prevIdx];
                            int prevDirOpp = _oppDirRing[learnerRingBase + prevIdx];
                            int learnerSlot = learnerIndices[e];
                            int oppSlot = 1 - learnerSlot;
                            int prevDir0 = learnerSlot == 0 ? prevDirLearner : prevDirOpp;
                            int prevDir1 = learnerSlot == 0 ? prevDirOpp : prevDirLearner;

                            // Encode current state directly into both rings.
                            Featurizer.EncodePair(
                                _envs[e].State,
                                _options,
                                learnerSlot,
                                prevDir0,
                                prevDir1,
                                _learnerObsHistory.AsSpan(writeBase, _stateDim),
                                _oppObsHistory.AsSpan(writeBase, _stateDim)
                            );
                            // Stage the *delayed* obs (slot from N Fighting frames
                            // ago, clamped at slot 0 while fightFrame < N) into the
                            // state portion of the batch row, then append the
                            // current action history to the tail of that row.
                            int readSlot =
                                (fightFrame[e] >= _latencyFrames ? fightFrame[e] - _latencyFrames : 0)
                                % _ringSlots;
                            int readBase = (e * _ringSlots + readSlot) * _stateDim;
                            int batchBase = i * _obsDim;
                            _learnerObsHistory
                                .AsSpan(readBase, _stateDim)
                                .CopyTo(_learnerObsBatch.AsSpan(batchBase, _stateDim));
                            _oppObsHistory
                                .AsSpan(readBase, _stateDim)
                                .CopyTo(_oppObsBatch.AsSpan(batchBase, _stateDim));

                            int orderedBase = i * _historyLen;
                            for (int k = 0; k < _historyLen; k++)
                            {
                                int src = learnerRingBase + (head + k) % _historyLen;
                                _learnerDirOrdered[orderedBase + k] = _learnerDirRing[src];
                                _learnerBtnOrdered[orderedBase + k] = _learnerBtnRing[src];
                                _oppDirOrdered[orderedBase + k] = _oppDirRing[src];
                                _oppBtnOrdered[orderedBase + k] = _oppBtnRing[src];
                            }
                            Featurizer.WriteHistory(
                                _learnerDirOrdered.AsSpan(orderedBase, _historyLen),
                                _learnerBtnOrdered.AsSpan(orderedBase, _historyLen),
                                _learnerObsBatch.AsSpan(batchBase + _stateDim, _historyDim)
                            );
                            Featurizer.WriteHistory(
                                _oppDirOrdered.AsSpan(orderedBase, _historyLen),
                                _oppBtnOrdered.AsSpan(orderedBase, _historyLen),
                                _oppObsBatch.AsSpan(batchBase + _stateDim, _historyDim)
                            );
                        });
                        featSw.Stop();

                        fwdSw.Start();
                        // Copy the active prefix straight into persistent CPU
                        // tensor storage; narrow takes a no-copy view, .to(device)
                        // transfers only the active rows.
                        int floatsToCopy = afn * _obsDim;
                        MemoryMarshal
                            .AsBytes(_learnerObsBatch.AsSpan(0, floatsToCopy))
                            .CopyTo(_learnerObsCpuT.bytes);
                        using Tensor learnerObs = _learnerObsCpuT.narrow(0, 0, afn).to(_device);

                        // Build active-env index tensor for hidden gather/scatter.
                        for (int i = 0; i < afn; i++)
                            _activeIdsLong[i] = activeFightingIds[i];
                        using Tensor activeIdx = tensor(
                            _activeIdsLong,
                            new long[] { afn },
                            ScalarType.Int64
                        ).to(_device);

                        using Tensor learnerHIn = learnerHidden.index_select(1, activeIdx);

                        if (oppIsNeural)
                        {
                            MemoryMarshal
                                .AsBytes(_oppObsBatch.AsSpan(0, floatsToCopy))
                                .CopyTo(_oppObsCpuT.bytes);
                            using Tensor oppObs = _oppObsCpuT.narrow(0, 0, afn).to(_device);
                            using Tensor oppHIn = oppHidden.index_select(1, activeIdx);
                            var packResult = PolicyNet.SampleBatchPair(
                                learner,
                                oppNeural.Net,
                                learnerObs,
                                oppObs,
                                learnerHIn,
                                oppHIn,
                                greedyOpponent,
                                sampleGen
                            );
                            dir = packResult.lDir;
                            btn = packResult.lBtn;
                            logp = packResult.lLogp;
                            value = packResult.lValue;
                            oppDir = packResult.oppDir;
                            oppBtn = packResult.oppBtn;
                            using (Tensor lHOut = packResult.lHOut)
                                learnerHidden.index_copy_(1, activeIdx, lHOut);
                            using (Tensor oHOut = packResult.oppHOut)
                                oppHidden.index_copy_(1, activeIdx, oHOut);
                        }
                        else
                        {
                            // Learner solo forward; opp comes from a non-neural
                            // IPolicy that reads GameState directly. Skip the opp
                            // obs transfer entirely (no use), and resolve opp
                            // actions sequentially to avoid imposing thread-safety
                            // on every IPolicy implementation.
                            var learnerResult = learner.SampleBatch(
                                learnerObs,
                                learnerHIn,
                                sampleGen
                            );
                            dir = learnerResult.dir;
                            btn = learnerResult.btn;
                            logp = learnerResult.logProb;
                            value = learnerResult.value;
                            using (Tensor lHOut = learnerResult.hOut)
                                learnerHidden.index_copy_(1, activeIdx, lHOut);
                            oppDir = new int[afn];
                            oppBtn = new int[afn];
                            for (int i = 0; i < afn; i++)
                            {
                                int e = activeFightingIds[i];
                                int oppIdx = 1 - learnerIndices[e];
                                // Read first, then write: at fightFrame f the
                                // applied action came from the bot's decision at
                                // f - _latencyFrames (clamped to slot 0 during
                                // warmup, mirroring the state ring's clamp).
                                int ringBase = e * _ringSlots;
                                int readSlot =
                                    (fightFrame[e] >= _latencyFrames
                                        ? fightFrame[e] - _latencyFrames
                                        : 0)
                                    % _ringSlots;
                                int writeSlot = fightFrame[e] % _ringSlots;
                                InputFlags applied = _oppActionRing[ringBase + readSlot];
                                InputFlags decided = greedyOpponent
                                    ? opponent.Act(_envs[e].State, _options, oppIdx)
                                    : opponent.ActSample(_envs[e].State, _options, oppIdx);
                                _oppActionRing[ringBase + writeSlot] = decided;
                                var (od, ob) = ActionSpace.Encode(applied);
                                oppDir[i] = od;
                                oppBtn[i] = ob;
                            }
                        }
                        fwdSw.Stop();

                        // Commit each fighter's chosen action into its history
                        // ring so the next encode sees [t-N+1, ..., t] when this
                        // step's action becomes "newest." Sequential write; cheap
                        // compared to the forwards above. Opp ring only matters
                        // when the opp is neural (non-neural opps don't read obs).
                        for (int i = 0; i < afn; i++)
                        {
                            int e = activeFightingIds[i];
                            int head = _historyHead[e];
                            int slot = e * _historyLen + head;
                            _learnerDirRing[slot] = dir[i];
                            _learnerBtnRing[slot] = btn[i];
                            if (oppIsNeural)
                            {
                                _oppDirRing[slot] = oppDir[i];
                                _oppBtnRing[slot] = oppBtn[i];
                            }
                            _historyHead[e] = (head + 1) % _historyLen;
                        }
                    }

                    // Phase 4: decode actions, advance every active env, accumulate.
                    // Parallel across envs; each worker touches only its own env,
                    // shaper, trajectory, and breakdown buffer.
                    stepSw.Start();
                    Parallel.For(0, _envCount, e =>
                    {
                        if (done[e])
                            return;

                        InputFlags learnerFlags;
                        InputFlags oppFlags;
                        if (wasFighting[e])
                        {
                            int b = envToBatchIdx[e];
                            learnerFlags = ActionSpace.Decode(dir[b], btn[b]);
                            oppFlags = ActionSpace.Decode(oppDir[b], oppBtn[b]);
                        }
                        else
                        {
                            learnerFlags = InputFlags.None;
                            oppFlags = InputFlags.None;
                        }

                        InputFlags p1,
                            p2;
                        if (learnerIndices[e] == 0)
                        {
                            p1 = learnerFlags;
                            p2 = oppFlags;
                        }
                        else
                        {
                            p1 = oppFlags;
                            p2 = learnerFlags;
                        }

                        RewardBreakdown[] breakdownBuf = _breakdownBufs[e];
                        _shapers[e].BeforeStep(_envs[e].State);
                        bool ended = _envs[e].Step(p1, p2);
                        _shapers[e].AfterStep(_envs[e].State, breakdownBuf);

                        results[e].P1Inputs.Add((int)p1);
                        results[e].P2Inputs.Add((int)p2);

                        RewardBreakdown rb = breakdownBuf[learnerIndices[e]];
                        float r = rb.Total;
                        results[e].TotalBreakdown = results[e].TotalBreakdown + rb;
                        runningReward[e] += r;
                        frames[e]++;
                        done[e] = ended;

                        if (wasFighting[e])
                        {
                            int b = envToBatchIdx[e];
                            trajs[e].AppendObservation(_learnerObsBatch.AsSpan(b * _obsDim, _obsDim));
                            trajs[e].DirActions.Add(dir[b]);
                            trajs[e].BtnActions.Add(btn[b]);
                            trajs[e].LogProbs.Add(logp[b]);
                            trajs[e].Values.Add(value[b]);
                            trajs[e].Rewards.Add(r + pendingReward[e]);
                            trajs[e].Dones.Add(done[e]);
                            pendingReward[e] = 0f;
                            fightFrame[e]++;
                        }
                        else
                        {
                            pendingReward[e] += r;
                        }
                    });
                    stepSw.Stop();

                    activeCount = 0;
                    for (int e = 0; e < _envCount; e++)
                        if (!done[e])
                            activeCount++;
                }
            }
            finally
            {
                oppHidden?.Dispose();
            }

            LastFeaturizeTime = featSw.Elapsed;
            LastForwardTime = fwdSw.Elapsed;
            LastStepTime = stepSw.Elapsed;

            // Phase 5: flush any pending-reward carry-over and finalize each
            // EpisodeResult.
            for (int e = 0; e < _envCount; e++)
            {
                if (pendingReward[e] != 0f && trajs[e].Length > 0)
                {
                    int last = trajs[e].Length - 1;
                    trajs[e].Rewards[last] += pendingReward[e];
                    trajs[e].Dones[last] = true;
                }
                else if (trajs[e].Length > 0)
                {
                    trajs[e].Dones[trajs[e].Length - 1] = true;
                }
                trajs[e].TerminalValue = 0f;

                results[e].Frames = frames[e];
                results[e].TotalReward = runningReward[e];
                results[e].LearnerLives = _envs[e].State.Fighters[learnerIndices[e]].Lives;
                results[e].OpponentLives = _envs[e].State.Fighters[1 - learnerIndices[e]].Lives;
                results[e].FinalHealth[0] = (float)_envs[e].State.Fighters[0].Health;
                results[e].FinalHealth[1] = (float)_envs[e].State.Fighters[1].Health;
            }

            return (trajs, results);
        }
    }
}
