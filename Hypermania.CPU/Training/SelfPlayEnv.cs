using System;
using System.Collections.Generic;
using Hypermania.Game;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Hosting;
using Hypermania.CPU.Policy;
using TorchSharp;
using static TorchSharp.torch;

namespace Hypermania.CPU.Training
{
    public sealed class EpisodeResult
    {
        public int LearnerIndex;
        public int Frames;
        public float TotalReward;
        public int LearnerLives;
        public int OpponentLives;
        public float[] FinalHealth = new float[2];

        // Sum of per-tick reward breakdowns over the episode, from the
        // learner's perspective. The TUI surfaces these so the human can see
        // which shaping term dominated this episode.
        public RewardBreakdown TotalBreakdown;

        // Full per-frame input log so we can dump a .hmrep that exactly
        // reproduces the rollout.
        public List<int> P1Inputs = new List<int>();
        public List<int> P2Inputs = new List<int>();

        // Human-facing description of the opponent the learner faced - e.g.
        // "Warmup AI", "Random AI", "Snapshot u000400". Consumed by replay
        // writers as the P1Player/P2Player display name.
        public string OpponentLabel = string.Empty;
    }

    public static class OpponentLabels
    {
        public static string For(IPolicy policy)
        {
            switch (policy)
            {
                case WarmupPolicy _:
                    return "Warmup AI";
                case RandomPolicy _:
                    return "Random AI";
                case NeuralPolicy n:
                    return $"Snapshot u{n.TrainingStep:D6}";
                default:
                    return policy != null ? policy.GetType().Name : "Unknown";
            }
        }
    }

    // Runs an episode of self-play, collecting a PPO trajectory for the
    // learner. The opponent is treated as a black-box IPolicy - it could be
    // a frozen earlier snapshot, a different network, or RandomPolicy.
    public sealed class SelfPlayEnv
    {
        readonly TrainingEnv _env;
        readonly SimOptions _options;
        readonly RewardShaper _shaper;
        readonly float[] _learnerObsBuf;
        readonly float[] _oppObsBuf;
        readonly RewardBreakdown[] _breakdownBuf;
        readonly Device _device;
        readonly int _stateDim;
        readonly int _historyDim;
        readonly int _historyLen;

        // Reaction-time latency in Fighting frames. Both learner and (if it's
        // a NeuralPolicy) the opponent read obs from N Fighting frames ago.
        // 0 = no latency. Sets the ring depth.
        readonly int _latencyFrames;
        readonly int _ringSlots;
        // Per-frame Featurizer state encodings, indexed by `fightFrame mod _ringSlots`.
        // History is appended at policy-input time, not stored in the ring.
        readonly float[] _learnerObsHistory;
        readonly float[] _oppObsHistory;

        // Action-history ring per fighter. _head is the next-write index.
        readonly int[] _learnerDirRing;
        readonly int[] _learnerBtnRing;
        readonly int[] _oppDirRing;
        readonly int[] _oppBtnRing;
        int _historyHead;
        readonly int[] _dirOrdered;
        readonly int[] _btnOrdered;

        // Pending non-neural opponent decisions; same role as
        // BatchedSelfPlayEnv._oppActionRing. Decision at fightFrame f goes
        // to slot f mod _ringSlots; the action applied this frame is read
        // from slot (f - _latencyFrames) mod _ringSlots (clamped to slot 0
        // during warmup, mirroring the obs ring).
        readonly InputFlags[] _oppActionRing;

        public SelfPlayEnv(
            SimOptions options,
            RewardConfig rewardCfg,
            Device device,
            int reactionLatencyFrames = 0
        )
        {
            if (reactionLatencyFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(reactionLatencyFrames));
            _env = new TrainingEnv(options);
            _options = options;
            _shaper = new RewardShaper(options, rewardCfg);
            _learnerObsBuf = new float[Featurizer.Length];
            _oppObsBuf = new float[Featurizer.Length];
            _breakdownBuf = new RewardBreakdown[options.Players.Length];
            _device = device ?? torch.CPU;
            _stateDim = Featurizer.StateLength;
            _historyDim = Featurizer.HistoryLength;
            _historyLen = Featurizer.ActionHistoryFrames;
            _latencyFrames = reactionLatencyFrames;
            _ringSlots = reactionLatencyFrames + 1;
            _learnerObsHistory = new float[_ringSlots * _stateDim];
            _oppObsHistory = new float[_ringSlots * _stateDim];
            _learnerDirRing = new int[_historyLen];
            _learnerBtnRing = new int[_historyLen];
            _oppDirRing = new int[_historyLen];
            _oppBtnRing = new int[_historyLen];
            _dirOrdered = new int[_historyLen];
            _btnOrdered = new int[_historyLen];
            _oppActionRing = new InputFlags[_ringSlots];
        }

        // Roll out one match. The learner's per-step samples land in `traj`
        // only on Fighting-mode frames - actions during Countdown / Mania /
        // RoundEnd / End-transition are still issued (so the env progresses
        // naturally) but excluded from the learner's gradient. Rewards earned
        // on skipped frames are accumulated onto the next appended Fighting
        // frame so terminal win/loss bonuses (which fire on the End-transition
        // frame) aren't dropped. A complete record of both fighters' inputs
        // goes to the result so the caller can dump a replay if it wants.
        // Episodes always run until GameMode.End - the round timer in the
        // sim handles termination, and there's no explicit frame cap.
        public EpisodeResult Run(
            int learnerIndex,
            PolicyNet learner,
            IPolicy opponent,
            Trajectory traj,
            Generator sampleGen = null,
            bool greedyOpponent = false
        )
        {
            if (learnerIndex < 0 || learnerIndex > 1)
                throw new ArgumentOutOfRangeException(nameof(learnerIndex));
            int oppIndex = 1 - learnerIndex;

            _env.Reset();
            _shaper.Reset();
            traj.Clear();

            EpisodeResult res = new EpisodeResult
            {
                LearnerIndex = learnerIndex,
                OpponentLabel = OpponentLabels.For(opponent),
            };
            float runningReward = 0f;
            float pendingReward = 0f;
            int frames = 0;
            int fightFrame = 0;
            _historyHead = 0;
            Array.Fill(_learnerDirRing, -1);
            Array.Fill(_learnerBtnRing, -1);
            Array.Fill(_oppDirRing, -1);
            Array.Fill(_oppBtnRing, -1);
            Array.Fill(_oppActionRing, InputFlags.None);
            // For neural opponents we bypass IPolicy.Act/ActSample (which
            // featurize the *current* state) and call Net.Sample/ActGreedy
            // directly so the opponent sees the same delayed obs the learner
            // does. Random/scripted opponents keep their existing path.
            NeuralPolicy oppNn = opponent as NeuralPolicy;

            // Per-fighter GRU hidden state for this episode. Both start at
            // zeros; the opponent slot is only used when the opponent is a
            // NeuralPolicy. Disposed in finally so the device tensors don't
            // leak if the loop throws.
            Tensor learnerHidden = learner.InitHidden(1, _device);
            Tensor oppHidden = oppNn != null ? oppNn.Net.InitHidden(1, _device) : null;

            try
            {
                bool done = false;
                while (!done)
                {
                    bool isFighting = _env.State.GameMode == GameMode.Fighting;

                    InputFlags learnerFlags;
                    InputFlags oppFlags;
                    int dir = 0,
                        btn = 0;
                    float logp = 0f,
                        value = 0f;

                    if (isFighting)
                    {
                        // Encode + sample only on frames the trajectory will keep.
                        // Non-Fighting frames (Countdown, ManiaStart, Mania,
                        // RoundEnd, End-transition) skip both Featurizer.Encode
                        // and any torch forward pass: actions are zeroed for both
                        // fighters, and the env still advances naturally because
                        // these phases largely ignore inputs anyway.
                        int writeSlot = fightFrame % _ringSlots;
                        int writeBase = writeSlot * _stateDim;

                        // Previous-frame action for each fighter slot. Both rings
                        // are -1 on the first Fighting frame so prev-block flags
                        // start at zero.
                        int prevIdx = (_historyHead - 1 + _historyLen) % _historyLen;
                        int prevDirLearner = _learnerDirRing[prevIdx];
                        int prevDirOpp = _oppDirRing[prevIdx];
                        int prevDir0 = learnerIndex == 0 ? prevDirLearner : prevDirOpp;
                        int prevDir1 = learnerIndex == 0 ? prevDirOpp : prevDirLearner;

                        Featurizer.EncodePair(
                            _env.State,
                            _options,
                            learnerIndex,
                            prevDir0,
                            prevDir1,
                            _learnerObsHistory.AsSpan(writeBase, _stateDim),
                            _oppObsHistory.AsSpan(writeBase, _stateDim)
                        );
                        int readSlot =
                            (fightFrame >= _latencyFrames ? fightFrame - _latencyFrames : 0)
                            % _ringSlots;
                        int readBase = readSlot * _stateDim;
                        // _learnerObsBuf = delayed state || own action history.
                        // Used both as the policy input and as the trajectory row.
                        _learnerObsHistory
                            .AsSpan(readBase, _stateDim)
                            .CopyTo(_learnerObsBuf.AsSpan(0, _stateDim));
                        BuildOrdered(_learnerDirRing, _learnerBtnRing);
                        Featurizer.WriteHistory(
                            _dirOrdered,
                            _btnOrdered,
                            _learnerObsBuf.AsSpan(_stateDim, _historyDim)
                        );

                        using Tensor obs = tensor(
                                _learnerObsBuf,
                                new long[] { _learnerObsBuf.Length },
                                ScalarType.Float32
                            )
                            .to(_device);

                        var sampleResult = learner.Sample(obs, learnerHidden, sampleGen);
                        dir = sampleResult.dir;
                        btn = sampleResult.btn;
                        logp = sampleResult.logProb;
                        value = sampleResult.value;
                        // Replace stored hidden with the freshly-returned one.
                        learnerHidden.Dispose();
                        learnerHidden = sampleResult.hOut;
                        learnerFlags = ActionSpace.Decode(dir, btn);

                        if (oppNn != null)
                        {
                            _oppObsHistory
                                .AsSpan(readBase, _stateDim)
                                .CopyTo(_oppObsBuf.AsSpan(0, _stateDim));
                            BuildOrdered(_oppDirRing, _oppBtnRing);
                            Featurizer.WriteHistory(
                                _dirOrdered,
                                _btnOrdered,
                                _oppObsBuf.AsSpan(_stateDim, _historyDim)
                            );
                            using Tensor oppObs = tensor(
                                    _oppObsBuf,
                                    new long[] { _oppObsBuf.Length },
                                    ScalarType.Float32
                                )
                                .to(_device);
                            int oDir,
                                oBtn;
                            Tensor newOppH;
                            if (greedyOpponent)
                            {
                                var oppGreedy = oppNn.Net.ActGreedy(oppObs, oppHidden);
                                oDir = oppGreedy.dir;
                                oBtn = oppGreedy.btn;
                                newOppH = oppGreedy.hOut;
                            }
                            else
                            {
                                var oppSampled = oppNn.Net.Sample(oppObs, oppHidden, sampleGen);
                                oDir = oppSampled.dir;
                                oBtn = oppSampled.btn;
                                newOppH = oppSampled.hOut;
                            }
                            oppHidden.Dispose();
                            oppHidden = newOppH;
                            oppFlags = ActionSpace.Decode(oDir, oBtn);
                            PushHistory(_oppDirRing, _oppBtnRing, oDir, oBtn, advance: false);
                        }
                        else
                        {
                            // Read first, then write: action applied this frame
                            // came from the bot's decision _latencyFrames Fighting
                            // frames ago. Mirrors the learner's obs latency.
                            int oppReadSlot =
                                (fightFrame >= _latencyFrames
                                    ? fightFrame - _latencyFrames
                                    : 0)
                                % _ringSlots;
                            int oppWriteSlot = fightFrame % _ringSlots;
                            oppFlags = _oppActionRing[oppReadSlot];
                            InputFlags decided = greedyOpponent
                                ? opponent.Act(_env.State, _options, oppIndex)
                                : opponent.ActSample(_env.State, _options, oppIndex);
                            _oppActionRing[oppWriteSlot] = decided;
                            // Non-neural opponents: log a sentinel since we don't
                            // have a categorical action breakdown. Their history
                            // block stays at -1 -> all zeros.
                            PushHistory(_oppDirRing, _oppBtnRing, -1, -1, advance: false);
                        }

                        PushHistory(_learnerDirRing, _learnerBtnRing, dir, btn, advance: true);
                    }
                    else
                    {
                        learnerFlags = InputFlags.None;
                        oppFlags = InputFlags.None;
                    }

                    // Learner's perspective is fighter index `learnerIndex`. Map
                    // its (p1, p2) flags into the fixed (p1, p2) sim slots.
                    InputFlags p1,
                        p2;
                    if (learnerIndex == 0)
                    {
                        p1 = learnerFlags;
                        p2 = oppFlags;
                    }
                    else
                    {
                        p1 = oppFlags;
                        p2 = learnerFlags;
                    }

                    _shaper.BeforeStep(_env.State);
                    bool ended = _env.Step(p1, p2);
                    _shaper.AfterStep(_env.State, _breakdownBuf);

                    res.P1Inputs.Add((int)p1);
                    res.P2Inputs.Add((int)p2);

                    RewardBreakdown rb = _breakdownBuf[learnerIndex];
                    float r = rb.Total;
                    res.TotalBreakdown = res.TotalBreakdown + rb;
                    runningReward += r;
                    frames++;
                    done = ended;

                    if (isFighting)
                    {
                        traj.AppendObservation(_learnerObsBuf);
                        traj.DirActions.Add(dir);
                        traj.BtnActions.Add(btn);
                        traj.LogProbs.Add(logp);
                        traj.Values.Add(value);
                        traj.Rewards.Add(r + pendingReward);
                        traj.Dones.Add(done);
                        pendingReward = 0f;
                        fightFrame++;
                    }
                    else
                    {
                        pendingReward += r;
                    }
                }
            }
            finally
            {
                learnerHidden.Dispose();
                oppHidden?.Dispose();
            }

            // If the episode ended on a non-Fighting frame (always true - the
            // End-transition fires from RoundEnd), flush carried reward onto
            // the last appended Fighting step and mark it terminal.
            if (pendingReward != 0f && traj.Length > 0)
            {
                int last = traj.Length - 1;
                traj.Rewards[last] += pendingReward;
                traj.Dones[last] = true;
                pendingReward = 0f;
            }
            else if (traj.Length > 0)
            {
                traj.Dones[traj.Length - 1] = true;
            }

            // Episode always runs to GameMode.End (no truncation), so the
            // bootstrap value is always 0.
            traj.TerminalValue = 0f;

            res.Frames = frames;
            res.TotalReward = runningReward;
            res.LearnerLives = _env.State.Fighters[learnerIndex].Lives;
            res.OpponentLives = _env.State.Fighters[oppIndex].Lives;
            res.FinalHealth[0] = (float)_env.State.Fighters[0].Health;
            res.FinalHealth[1] = (float)_env.State.Fighters[1].Health;
            return res;
        }

        // Walk the ring from _historyHead so output is oldest-first.
        void BuildOrdered(int[] dirRing, int[] btnRing)
        {
            int head = _historyHead;
            for (int k = 0; k < _historyLen; k++)
            {
                int src = (head + k) % _historyLen;
                _dirOrdered[k] = dirRing[src];
                _btnOrdered[k] = btnRing[src];
            }
        }

        // Write the action at the next slot. Both fighters share _historyHead,
        // so only one of the per-frame calls (the second) advances it.
        void PushHistory(int[] dirRing, int[] btnRing, int dir, int btn, bool advance)
        {
            dirRing[_historyHead] = dir;
            btnRing[_historyHead] = btn;
            if (advance)
                _historyHead = (_historyHead + 1) % _historyLen;
        }
    }
}
