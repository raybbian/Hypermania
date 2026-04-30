using System;
using Game.Sim;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Hosting;
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
    public sealed class BatchedSelfPlayEnv
    {
        readonly TrainingEnv[] _envs;
        readonly RewardShaper[] _shapers;
        readonly SimOptions _options;
        readonly Device _device;
        readonly int _envCount;
        readonly int _obsDim;

        // Pre-allocated CPU staging buffers, sized for the worst case (all
        // envs Fighting). Each step we fill the prefix [0, K * obsDim) and
        // narrow the resulting tensor to [K, obsDim] before transferring to
        // the device.
        readonly float[] _learnerObsBatch;
        readonly float[] _oppObsBatch;
        readonly RewardBreakdown[] _breakdownBuf;

        public int EnvCount => _envCount;

        public BatchedSelfPlayEnv(
            SimOptions options,
            RewardConfig rewardCfg,
            Device device,
            int envCount
        )
        {
            if (envCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(envCount));
            _options = options;
            _device = device ?? torch.CPU;
            _envCount = envCount;
            _obsDim = Featurizer.Length;
            _envs = new TrainingEnv[envCount];
            _shapers = new RewardShaper[envCount];
            for (int i = 0; i < envCount; i++)
            {
                _envs[i] = new TrainingEnv(options);
                _shapers[i] = new RewardShaper(options, rewardCfg);
            }
            _learnerObsBatch = new float[envCount * _obsDim];
            _oppObsBatch = new float[envCount * _obsDim];
            _breakdownBuf = new RewardBreakdown[options.Players.Length];
        }

        // Run all N envs to GameMode.End in parallel. learnerIndices[e] picks
        // which fighter slot the learner controls in env e (the opponent
        // controls the other). Returns N trajectories (one per env) and N
        // EpisodeResults.
        public (Trajectory[] trajs, EpisodeResult[] results) Run(
            int[] learnerIndices,
            PolicyNet learner,
            PolicyNet opponent,
            Generator sampleGen = null,
            bool greedyOpponent = false
        )
        {
            if (learnerIndices == null || learnerIndices.Length != _envCount)
                throw new ArgumentException(
                    $"learnerIndices length must equal envCount ({_envCount})",
                    nameof(learnerIndices)
                );

            Trajectory[] trajs = new Trajectory[_envCount];
            EpisodeResult[] results = new EpisodeResult[_envCount];
            int[] frames = new int[_envCount];
            float[] runningReward = new float[_envCount];
            float[] pendingReward = new float[_envCount];
            bool[] done = new bool[_envCount];

            for (int e = 0; e < _envCount; e++)
            {
                if ((uint)learnerIndices[e] >= 2)
                    throw new ArgumentOutOfRangeException(nameof(learnerIndices));
                _envs[e].Reset();
                _shapers[e].Reset();
                trajs[e] = new Trajectory();
                results[e] = new EpisodeResult { LearnerIndex = learnerIndices[e] };
            }

            // Per-step scratch.
            int[] activeFightingIds = new int[_envCount];
            int[] envToBatchIdx = new int[_envCount];
            bool[] wasFighting = new bool[_envCount];

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
                    for (int i = 0; i < afn; i++)
                    {
                        int e = activeFightingIds[i];
                        Featurizer.Encode(
                            _envs[e].State,
                            _options,
                            learnerIndices[e],
                            _learnerObsBatch.AsSpan(i * _obsDim, _obsDim)
                        );
                        Featurizer.Encode(
                            _envs[e].State,
                            _options,
                            1 - learnerIndices[e],
                            _oppObsBatch.AsSpan(i * _obsDim, _obsDim)
                        );
                    }

                    // tensor() copies the whole staging buffer into a CPU
                    // tensor, narrow takes a no-copy view of the active prefix,
                    // .to(device) only transfers those rows.
                    using Tensor learnerObsFull = tensor(
                        _learnerObsBatch,
                        new long[] { _envCount, _obsDim },
                        ScalarType.Float32
                    );
                    using Tensor learnerObs = learnerObsFull.narrow(0, 0, afn).to(_device);
                    using Tensor oppObsFull = tensor(
                        _oppObsBatch,
                        new long[] { _envCount, _obsDim },
                        ScalarType.Float32
                    );
                    using Tensor oppObs = oppObsFull.narrow(0, 0, afn).to(_device);

                    (dir, btn, logp, value) = learner.SampleBatch(learnerObs, sampleGen);
                    if (greedyOpponent)
                    {
                        var (od, ob, _) = opponent.ActGreedyBatch(oppObs);
                        oppDir = od;
                        oppBtn = ob;
                    }
                    else
                    {
                        var (od, ob, _, _) = opponent.SampleBatch(oppObs, sampleGen);
                        oppDir = od;
                        oppBtn = ob;
                    }
                }

                // Phase 4: decode actions, advance every active env, accumulate.
                for (int e = 0; e < _envCount; e++)
                {
                    if (done[e])
                        continue;

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

                    _shapers[e].BeforeStep(_envs[e].State);
                    bool ended = _envs[e].Step(p1, p2);
                    _shapers[e].AfterStep(_envs[e].State, _breakdownBuf);

                    results[e].P1Inputs.Add((int)p1);
                    results[e].P2Inputs.Add((int)p2);

                    RewardBreakdown rb = _breakdownBuf[learnerIndices[e]];
                    float r = rb.Total;
                    results[e].TotalBreakdown = results[e].TotalBreakdown + rb;
                    runningReward[e] += r;
                    frames[e]++;
                    done[e] = ended;

                    if (wasFighting[e])
                    {
                        int b = envToBatchIdx[e];
                        float[] obsCopy = new float[_obsDim];
                        Buffer.BlockCopy(
                            _learnerObsBatch,
                            b * _obsDim * sizeof(float),
                            obsCopy,
                            0,
                            _obsDim * sizeof(float)
                        );
                        trajs[e].Observations.Add(obsCopy);
                        trajs[e].DirActions.Add(dir[b]);
                        trajs[e].BtnActions.Add(btn[b]);
                        trajs[e].LogProbs.Add(logp[b]);
                        trajs[e].Values.Add(value[b]);
                        trajs[e].Rewards.Add(r + pendingReward[e]);
                        trajs[e].Dones.Add(done[e]);
                        pendingReward[e] = 0f;
                    }
                    else
                    {
                        pendingReward[e] += r;
                    }

                    if (done[e])
                        activeCount--;
                }
            }

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
