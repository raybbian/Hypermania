using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;
using Game.Sim.Observation;
using Game.Sim.Replay;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Policy;
using TorchSharp;
using static TorchSharp.torch;
using F = TorchSharp.torch.nn.functional;

namespace Hypermania.CPU.Training
{
    public sealed class PpoConfig
    {
        public int RolloutsPerUpdate = 4;

        public float Gamma = 0.99f;
        public float Lambda = 0.95f;
        public float ClipEps = 0.2f;
        public float ValueCoef = 0.5f;
        public float EntropyCoef = 0.01f;
        public float MaxGradNorm = 0.5f;
        public float LearningRate = 3e-4f;

        public int Epochs = 4;
        public int MinibatchSize = 256;

        public int TotalUpdates = 1000;
        public int SnapshotEvery = 50;
        public int ReplayEvery = 50;

        // How often to refresh the frozen opponent from the latest learner
        // weights. 0 = never (always plays vs RandomPolicy / initial).
        public int OpponentRefreshEvery = 25;
    }

    public sealed class PpoTrainer
    {
        readonly PpoConfig _cfg;
        readonly RewardConfig _rewardCfg;
        readonly SimOptions _simOptions;
        readonly NeuralPolicy _learner;
        readonly NeuralPolicy _frozenOpponent; // refreshed periodically
        readonly BatchedSelfPlayEnv _env;
        readonly torch.optim.Optimizer _opt;
        readonly Device _device;
        readonly string _runDir;
        readonly string _runId;
        readonly Random _rng;

        public PpoTrainer(
            SimOptions simOptions,
            PpoConfig cfg,
            RewardConfig rewardCfg,
            Device device,
            string outDir,
            int seed,
            string resumeFrom = null
        )
        {
            _simOptions = simOptions;
            _cfg = cfg;
            _rewardCfg = rewardCfg;
            _device = device;
            _rng = new Random(seed);

            _runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-s{seed}";
            _runDir = Path.Combine(outDir, _runId);
            Directory.CreateDirectory(_runDir);

            int obsDim = Featurizer.Length;
            _learner = new NeuralPolicy(obsDim, device);
            // Optional warm-start from a prior snapshot. Optimizer state is
            // not persisted - Adam's per-param momentum/variance buffers
            // restart from zero. First few updates after resume have noisier
            // gradients than a continuous run; in practice this washes out
            // within tens of updates.
            if (!string.IsNullOrEmpty(resumeFrom))
            {
                using FileStream fs = File.OpenRead(resumeFrom);
                _learner.Load(fs);
                Console.WriteLine($"resumed from {resumeFrom} at step {_learner.TrainingStep}");
            }
            _frozenOpponent = new NeuralPolicy(obsDim, device);
            CopyWeights(_learner, _frozenOpponent);

            _env = new BatchedSelfPlayEnv(simOptions, rewardCfg, device, cfg.RolloutsPerUpdate);
            _opt = torch.optim.Adam(_learner.Net.parameters(), lr: cfg.LearningRate);

            torch.manual_seed(seed);
        }

        public string RunDir => _runDir;
        public NeuralPolicy Learner => _learner;

        public void Train(ITrainingReporter reporter = null)
        {
            reporter ??= new ConsoleReporter();
            reporter.Begin(_runId, _runDir, _cfg, _device, Featurizer.Length);

            float updateSecondsEma = 0f;

            try
            {
                for (int update = 1; update <= _cfg.TotalUpdates; update++)
                {
                    System.Diagnostics.Stopwatch updateSw = System.Diagnostics.Stopwatch.StartNew();

                    int[] learnerIndices = new int[_cfg.RolloutsPerUpdate];
                    for (int r = 0; r < _cfg.RolloutsPerUpdate; r++)
                        learnerIndices[r] = _rng.Next(2);

                    System.Diagnostics.Stopwatch rolloutSw =
                        System.Diagnostics.Stopwatch.StartNew();
                    (Trajectory[] trajArr, EpisodeResult[] eps) = _env.Run(
                        learnerIndices,
                        _learner.Net,
                        _frozenOpponent.Net
                    );
                    rolloutSw.Stop();

                    List<Trajectory> rollouts = new List<Trajectory>(trajArr);
                    EpisodeResult lastEpisode = eps[eps.Length - 1];
                    int learnerWins = 0;
                    float meanReturn = 0f;
                    float totalFrames = 0f;
                    float totalFightingFrames = 0f;
                    RewardBreakdown breakdownSum = default;
                    for (int r = 0; r < eps.Length; r++)
                    {
                        meanReturn += eps[r].TotalReward;
                        totalFrames += eps[r].Frames;
                        totalFightingFrames += trajArr[r].Length;
                        breakdownSum = breakdownSum + eps[r].TotalBreakdown;
                        if (eps[r].LearnerLives > eps[r].OpponentLives)
                            learnerWins++;
                    }
                    meanReturn /= _cfg.RolloutsPerUpdate;

                    System.Diagnostics.Stopwatch optSw = System.Diagnostics.Stopwatch.StartNew();
                    (float policyLoss, float valueLoss, float entropy) = OptimizeOn(rollouts);
                    optSw.Stop();

                    _learner.TrainingStep++;
                    _learner.WallclockUtcTicks = DateTime.UtcNow.Ticks;
                    long step = _learner.TrainingStep;

                    updateSw.Stop();
                    float updateSecs = (float)updateSw.Elapsed.TotalSeconds;
                    // 0.2 alpha gives ~5-update half-life; smooth enough to read,
                    // responsive enough to track regime changes.
                    updateSecondsEma =
                        updateSecondsEma == 0f
                            ? updateSecs
                            : 0.2f * updateSecs + 0.8f * updateSecondsEma;

                    TrainingMetrics m = new TrainingMetrics
                    {
                        Step = step,
                        TotalUpdates = _cfg.TotalUpdates,
                        Rollouts = _cfg.RolloutsPerUpdate,
                        Wins = learnerWins,
                        MeanReturn = meanReturn,
                        MeanFrames = totalFrames / _cfg.RolloutsPerUpdate,
                        MeanFightingFrames = totalFightingFrames / _cfg.RolloutsPerUpdate,
                        PolicyLoss = policyLoss,
                        ValueLoss = valueLoss,
                        Entropy = entropy,
                        AvgBreakdown = breakdownSum / _cfg.RolloutsPerUpdate,
                        RolloutTime = rolloutSw.Elapsed,
                        OptimizeTime = optSw.Elapsed,
                        UpdateTime = updateSw.Elapsed,
                        UpdateSecondsEma = updateSecondsEma,
                    };
                    reporter.OnUpdate(m);

                    if (update % _cfg.SnapshotEvery == 0 || update == _cfg.TotalUpdates)
                        WriteSnapshot(step);

                    if (_cfg.OpponentRefreshEvery > 0 && update % _cfg.OpponentRefreshEvery == 0)
                        CopyWeights(_learner, _frozenOpponent);

                    if (
                        _cfg.ReplayEvery > 0
                        && update % _cfg.ReplayEvery == 0
                        && lastEpisode != null
                    )
                        WriteReplay(step, lastEpisode);
                }
            }
            finally
            {
                reporter.End();
            }
        }

        // GAE-lambda advantage and discounted return computation, per-trajectory.
        static void ComputeAdvantages(
            Trajectory t,
            float gamma,
            float lam,
            out float[] adv,
            out float[] ret
        )
        {
            int n = t.Length;
            adv = new float[n];
            ret = new float[n];
            float lastAdv = 0f;
            for (int i = n - 1; i >= 0; i--)
            {
                float nextValue = (i == n - 1) ? t.TerminalValue : t.Values[i + 1];
                float nonterminal = t.Dones[i] ? 0f : 1f;
                float delta = t.Rewards[i] + gamma * nextValue * nonterminal - t.Values[i];
                lastAdv = delta + gamma * lam * nonterminal * lastAdv;
                adv[i] = lastAdv;
                ret[i] = adv[i] + t.Values[i];
            }
        }

        (float policyLoss, float valueLoss, float entropy) OptimizeOn(List<Trajectory> rollouts)
        {
            // Flatten all trajectories into one batch.
            int total = 0;
            foreach (Trajectory t in rollouts)
                total += t.Length;
            if (total == 0)
                return (0f, 0f, 0f);

            int obsDim = Featurizer.Length;
            float[] flatObs = new float[total * obsDim];
            long[] flatDir = new long[total];
            long[] flatBtn = new long[total];
            float[] flatOldLogp = new float[total];
            float[] flatAdv = new float[total];
            float[] flatRet = new float[total];

            int o = 0;
            foreach (Trajectory t in rollouts)
            {
                ComputeAdvantages(t, _cfg.Gamma, _cfg.Lambda, out float[] adv, out float[] ret);
                for (int i = 0; i < t.Length; i++)
                {
                    Buffer.BlockCopy(
                        t.Observations[i],
                        0,
                        flatObs,
                        o * obsDim * sizeof(float),
                        obsDim * sizeof(float)
                    );
                    flatDir[o] = t.DirActions[i];
                    flatBtn[o] = t.BtnActions[i];
                    flatOldLogp[o] = t.LogProbs[i];
                    flatAdv[o] = adv[i];
                    flatRet[o] = ret[i];
                    o++;
                }
            }

            // Normalize advantages over the entire batch.
            float advMean = 0f,
                advVar = 0f;
            for (int i = 0; i < total; i++)
                advMean += flatAdv[i];
            advMean /= total;
            for (int i = 0; i < total; i++)
            {
                float d = flatAdv[i] - advMean;
                advVar += d * d;
            }
            float advStd = MathF.Sqrt(advVar / Math.Max(1, total - 1)) + 1e-8f;
            for (int i = 0; i < total; i++)
                flatAdv[i] = (flatAdv[i] - advMean) / advStd;

            using Tensor obsT = tensor(flatObs, new long[] { total, obsDim }, ScalarType.Float32)
                .to(_device);
            using Tensor dirT = tensor(flatDir, new long[] { total }, ScalarType.Int64).to(_device);
            using Tensor btnT = tensor(flatBtn, new long[] { total }, ScalarType.Int64).to(_device);
            using Tensor oldLogpT = tensor(flatOldLogp, new long[] { total }, ScalarType.Float32)
                .to(_device);
            using Tensor advT = tensor(flatAdv, new long[] { total }, ScalarType.Float32)
                .to(_device);
            using Tensor retT = tensor(flatRet, new long[] { total }, ScalarType.Float32)
                .to(_device);

            float lastPolicyLoss = 0f,
                lastValueLoss = 0f,
                lastEntropy = 0f;
            for (int epoch = 0; epoch < _cfg.Epochs; epoch++)
            {
                // Random permutation for shuffled minibatches.
                int[] idx = new int[total];
                for (int i = 0; i < total; i++)
                    idx[i] = i;
                Shuffle(idx, _rng);

                for (int start = 0; start < total; start += _cfg.MinibatchSize)
                {
                    int end = Math.Min(total, start + _cfg.MinibatchSize);
                    int len = end - start;
                    long[] batchIdx = new long[len];
                    for (int i = 0; i < len; i++)
                        batchIdx[i] = idx[start + i];
                    using Tensor idxT = tensor(batchIdx, new long[] { len }, ScalarType.Int64)
                        .to(_device);

                    using Tensor mbObs = obsT.index_select(0, idxT);
                    using Tensor mbDir = dirT.index_select(0, idxT);
                    using Tensor mbBtn = btnT.index_select(0, idxT);
                    using Tensor mbOldLogp = oldLogpT.index_select(0, idxT);
                    using Tensor mbAdv = advT.index_select(0, idxT);
                    using Tensor mbRet = retT.index_select(0, idxT);

                    var (newLogp, ent, value) = _learner.Net.Evaluate(mbObs, mbDir, mbBtn);

                    Tensor ratio = (newLogp - mbOldLogp).exp();
                    Tensor surr1 = ratio * mbAdv;
                    Tensor surr2 = ratio.clamp(1f - _cfg.ClipEps, 1f + _cfg.ClipEps) * mbAdv;
                    Tensor policyLoss = -torch.minimum(surr1, surr2).mean();

                    Tensor valueLoss = F.mse_loss(value, mbRet);
                    Tensor entropy = ent.mean();

                    Tensor loss =
                        policyLoss + _cfg.ValueCoef * valueLoss - _cfg.EntropyCoef * entropy;

                    _opt.zero_grad();
                    loss.backward();
                    torch.nn.utils.clip_grad_norm_(_learner.Net.parameters(), _cfg.MaxGradNorm);
                    _opt.step();

                    lastPolicyLoss = policyLoss.cpu().item<float>();
                    lastValueLoss = valueLoss.cpu().item<float>();
                    lastEntropy = entropy.cpu().item<float>();
                }
            }
            return (lastPolicyLoss, lastValueLoss, lastEntropy);
        }

        void WriteSnapshot(long step)
        {
            string path = Path.Combine(_runDir, $"policy_{step:D6}.hmpolicy");
            using (FileStream fs = File.Create(path))
                _learner.Save(fs);
            string latest = Path.Combine(_runDir, "latest.hmpolicy");
            File.Copy(path, latest, overwrite: true);
        }

        void WriteReplay(long step, EpisodeResult ep)
        {
            Character p1Char = _simOptions.Players[0].Character?.Character ?? Character.Nythea;
            Character p2Char = _simOptions.Players[1].Character?.Character ?? Character.Nythea;
            ReplayFile r = ReplayFile.Build(
                schemaHash: ObservationSchema.Hash(typeof(GameState)),
                p1Char: p1Char,
                p2Char: p2Char,
                p1Skin: 0,
                p2Skin: p1Char == p2Char ? 1 : 0,
                stage: Stage.Stage1,
                p1Inputs: ep.P1Inputs.ToArray(),
                p2Inputs: ep.P2Inputs.ToArray(),
                source: $"train:{_runId}:u{step:D6}",
                simOptions: _simOptions
            );
            string path = Path.Combine(_runDir, $"rollout_u{step:D6}.hmrep");
            ReplayFile.Save(path, r);
        }

        static void CopyWeights(NeuralPolicy from, NeuralPolicy to)
        {
            using MemoryStream ms = new MemoryStream();
            from.Save(ms);
            ms.Position = 0;
            to.Load(ms);
        }

        static void Shuffle(int[] arr, Random rng)
        {
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
        }
    }
}
