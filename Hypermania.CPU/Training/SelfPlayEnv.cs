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
        readonly RewardShaper _shaper;
        readonly float[] _learnerObsBuf;
        readonly float[] _rewardBuf;
        readonly Device _device;

        public SelfPlayEnv(SimOptions options, RewardConfig rewardCfg, Device device)
        {
            _env = new TrainingEnv(options);
            _shaper = new RewardShaper(options.Players.Length, rewardCfg);
            _learnerObsBuf = new float[Featurizer.Length];
            _rewardBuf = new float[options.Players.Length];
            _device = device ?? CPU;
        }

        // Roll out one match. The learner's per-step samples land in `traj`;
        // a complete record of both fighters' inputs goes to the result so
        // the caller can dump a replay if it wants. `maxFrames` caps runaway
        // matches; 0 means "until GameMode.End".
        public EpisodeResult Run(
            int learnerIndex,
            PolicyNet learner,
            IPolicy opponent,
            Trajectory traj,
            Generator sampleGen = null,
            int maxFrames = 0
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
            int frames = 0;

            bool done = false;
            while (!done)
            {
                Featurizer.Encode(_env.State, learnerIndex, _learnerObsBuf);
                using Tensor obs = tensor(_learnerObsBuf, new long[] { _learnerObsBuf.Length }, ScalarType.Float32).to(_device);

                var (dir, btn, logp, value) = learner.Sample(obs, sampleGen);
                InputFlags learnerFlags = ActionSpace.Decode(dir, btn);
                InputFlags oppFlags = opponent.Act(_env.State, oppIndex);

                // Learner's perspective is fighter index `learnerIndex`. Map
                // its (p1, p2) flags into the fixed (p1, p2) sim slots.
                InputFlags p1, p2;
                if (learnerIndex == 0) { p1 = learnerFlags; p2 = oppFlags; }
                else { p1 = oppFlags; p2 = learnerFlags; }

                float[] obsCopy = new float[_learnerObsBuf.Length];
                Buffer.BlockCopy(_learnerObsBuf, 0, obsCopy, 0, _learnerObsBuf.Length * sizeof(float));

                _shaper.BeforeStep(_env.State);
                bool ended = _env.Step(p1, p2);
                _shaper.AfterStep(_env.State, _rewardBuf);

                res.P1Inputs.Add((int)p1);
                res.P2Inputs.Add((int)p2);

                float r = _rewardBuf[learnerIndex];
                runningReward += r;
                frames++;

                bool truncated = maxFrames > 0 && frames >= maxFrames;
                done = ended || truncated;

                traj.Observations.Add(obsCopy);
                traj.DirActions.Add(dir);
                traj.BtnActions.Add(btn);
                traj.LogProbs.Add(logp);
                traj.Values.Add(value);
                traj.Rewards.Add(r);
                traj.Dones.Add(done);
            }

            // Bootstrap value: 0 if the episode actually ended, else the
            // value estimate of the next observation.
            if (_env.State.GameMode == GameMode.End)
            {
                traj.TerminalValue = 0f;
            }
            else
            {
                Featurizer.Encode(_env.State, learnerIndex, _learnerObsBuf);
                using Tensor obs = tensor(_learnerObsBuf, new long[] { _learnerObsBuf.Length }, ScalarType.Float32).to(_device);
                var (_, _, v) = learner.ActGreedy(obs);
                traj.TerminalValue = v;
            }

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
