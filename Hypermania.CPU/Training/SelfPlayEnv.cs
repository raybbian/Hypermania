using System;
using System.Collections.Generic;
using Game.Sim;
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
        readonly RewardBreakdown[] _breakdownBuf;
        readonly Device _device;

        public SelfPlayEnv(SimOptions options, RewardConfig rewardCfg, Device device)
        {
            _env = new TrainingEnv(options);
            _options = options;
            _shaper = new RewardShaper(options, rewardCfg);
            _learnerObsBuf = new float[Featurizer.Length];
            _breakdownBuf = new RewardBreakdown[options.Players.Length];
            _device = device ?? torch.CPU;
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

            EpisodeResult res = new EpisodeResult { LearnerIndex = learnerIndex };
            float runningReward = 0f;
            float pendingReward = 0f;
            int frames = 0;

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
                    Featurizer.Encode(_env.State, _options, learnerIndex, _learnerObsBuf);
                    using Tensor obs = tensor(
                            _learnerObsBuf,
                            new long[] { _learnerObsBuf.Length },
                            ScalarType.Float32
                        )
                        .to(_device);

                    (dir, btn, logp, value) = learner.Sample(obs, sampleGen);
                    learnerFlags = ActionSpace.Decode(dir, btn);
                    oppFlags = greedyOpponent
                        ? opponent.Act(_env.State, _options, oppIndex)
                        : opponent.ActSample(_env.State, _options, oppIndex);
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
                    float[] obsCopy = new float[_learnerObsBuf.Length];
                    Buffer.BlockCopy(
                        _learnerObsBuf,
                        0,
                        obsCopy,
                        0,
                        _learnerObsBuf.Length * sizeof(float)
                    );
                    traj.Observations.Add(obsCopy);
                    traj.DirActions.Add(dir);
                    traj.BtnActions.Add(btn);
                    traj.LogProbs.Add(logp);
                    traj.Values.Add(value);
                    traj.Rewards.Add(r + pendingReward);
                    traj.Dones.Add(done);
                    pendingReward = 0f;
                }
                else
                {
                    pendingReward += r;
                }
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
    }
}
