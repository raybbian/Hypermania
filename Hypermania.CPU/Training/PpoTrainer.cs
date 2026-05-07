using System;
using System.Collections.Generic;
using System.IO;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Policy;
using Hypermania.Game;
using Hypermania.Game.Replay;
using Hypermania.Shared;
using TorchSharp;
using static TorchSharp.torch;

namespace Hypermania.CPU.Training
{
    public enum OpponentSamplingMode
    {
        Uniform,
        Pfsp,
    }

    public sealed class PpoConfig
    {
        public int RolloutsPerUpdate = 64;

        public float Gamma = 0.99f;
        public float Lambda = 0.95f;
        public float ClipEps = 0.2f;
        public float ValueCoef = 0.5f;
        public float EntropyCoef = 0.01f;
        public float MaxGradNorm = 0.5f;
        public float LearningRate = 3e-4f;

        public int Epochs = 4;
        // Sequential minibatch size in *envs*, not steps. The recurrent
        // policy needs each minibatch to be one or more contiguous env
        // trajectories so the GRU can run in time order with h_init = 0.
        // RolloutsPerUpdate / EnvsPerMinibatch = number of minibatches per
        // PPO epoch; with 64 envs and 16 envs/mb that's 4 minibatches/epoch.
        public int EnvsPerMinibatch = 16;
        public int Hidden = 256;

        public int TotalUpdates = 1000;
        public int SnapshotEvery = 50;
        public int ReplayEvery = 50;

        // Promote a new learner snapshot into the opponent pool once the
        // learner's winrate is at or above this threshold against every pool
        // entry that has met the minimum sample size. The all-opponents
        // requirement is multiplicative, so the effective skill bar to trip
        // the gate is meaningfully higher than the nominal threshold.
        public float OpponentPromotionWinRate = 0.60f;

        // Minimum games against a pool entry before its winrate counts toward
        // the promotion gate. Stats below this are ignored (entry treated as
        // "not yet evaluated"), which prevents promotion before the learner
        // has actually faced every opponent.
        public int OpponentPromotionMinGames = 50;

        // Smoothing factor for the per-opponent win-rate EMA. Each update
        // batch's winrate is blended in as a*batchRate + (1-a)*ewma. Lower =
        // smoother but slower to react; higher = noisier but tracks the
        // current learner faster. 0.1 gives ~7-batch half-life.
        public float OpponentWinRateEma = 0.1f;

        // Max number of past learner snapshots kept as opponents. Each Run
        // samples uniformly from the pool. Mitigates rock-paper-scissors
        // cycles and forgetting against earlier-style opponents.
        public int OpponentPoolSize = 5;

        // How to draw an opponent from the pool each update. Uniform is the
        // safe default; Pfsp weights toward opponents the learner is losing
        // to or barely beating, AlphaStar-style.
        public OpponentSamplingMode OpponentSamplingMode = OpponentSamplingMode.Uniform;

        // PFSP weight is max(PfspMinWeight, (1 - winrate)^PfspExponent).
        // Higher exponent focuses harder on losses; the floor keeps dominated
        // entries reachable until eviction.
        public float PfspExponent = 2.0f;
        public float PfspMinWeight = 0.01f;

        // Frames of observation latency. Both learner and opponent pick the
        // action for sim frame t using the obs captured at Fighting frame t-N.
        // N=10 ≈ 167ms at 60 TPS, ~human reaction. 0 = no latency.
        public int ReactionLatencyFrames = 10;
    }

    public sealed class PpoTrainer
    {
        readonly PpoConfig _cfg;
        readonly RewardConfig _rewardCfg;
        readonly SimOptions _simOptions;
        readonly NeuralPolicy _learner;
        // Opponents the learner faces during rollouts. Mostly past learner
        // snapshots; can also include scripted bots (e.g. WarmupPolicy) seeded
        // at construction. One entry is sampled per Run. Bounded by
        // PpoConfig.OpponentPoolSize; oldest entries evict on overflow, so
        // the warmup bot rolls off naturally as neural snapshots get promoted.
        readonly List<IPolicy> _opponentPool = new List<IPolicy>();
        // Parallel to _opponentPool. ewma is the EMA of per-batch winrate
        // against this entry; gamesSeen is the cumulative game count, used
        // only for the MinGames gate. Drives PFSP weighting and the promotion
        // threshold. Index-locked with _opponentPool: every Add/RemoveAt on
        // the pool mirrors here.
        readonly List<(float ewma, long gamesSeen)> _oppStats =
            new List<(float, long)>();
        readonly BatchedSelfPlayEnv _env;
        readonly torch.optim.Optimizer _opt;
        readonly Device _device;
        readonly Device _rolloutDevice;
        // Forward-only mirror of the learner on the rollout device. Points at
        // _learner directly when both devices match; otherwise a separate net
        // that gets its weights synced from _learner before every rollout.
        readonly NeuralPolicy _rolloutLearner;
        readonly bool _hasRolloutMirror;
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
            string resumeFrom = null,
            Device rolloutDevice = null,
            bool includeWarmupOpponent = true
        )
        {
            _simOptions = simOptions;
            _cfg = cfg;
            _rewardCfg = rewardCfg;
            _device = device;
            _rolloutDevice = rolloutDevice ?? device;
            _rng = new Random(seed);

            _runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-s{seed}";
            _runDir = Path.Combine(outDir, _runId);
            Directory.CreateDirectory(_runDir);

            int obsDim = Featurizer.Length;
            // Optional warm-start from a prior snapshot. The snapshot's own
            // hidden size wins over cfg.Hidden so the user doesn't need to
            // pass --hidden matching the resume target. Optimizer state is
            // not persisted - Adam's per-param momentum/variance buffers
            // restart from zero. First few updates after resume have noisier
            // gradients than a continuous run; in practice this washes out
            // within tens of updates.
            if (!string.IsNullOrEmpty(resumeFrom))
            {
                using FileStream fs = File.OpenRead(resumeFrom);
                _learner = NeuralPolicy.LoadFrom(fs, device);
                cfg.Hidden = _learner.Net.Hidden;
                Console.WriteLine(
                    $"resumed from {resumeFrom} at step {_learner.TrainingStep} (hidden={cfg.Hidden})"
                );
            }
            else
            {
                _learner = new NeuralPolicy(obsDim, device, cfg.Hidden);
            }

            _hasRolloutMirror = !SameDevice(_device, _rolloutDevice);
            if (_hasRolloutMirror)
            {
                _rolloutLearner = new NeuralPolicy(obsDim, _rolloutDevice, cfg.Hidden);
                CopyWeights(_learner, _rolloutLearner);
            }
            else
            {
                _rolloutLearner = _learner;
            }

            // Seed the scripted warmup bot first so it lives in slot 0 and is
            // the first to evict as neural snapshots get promoted in.
            if (includeWarmupOpponent)
            {
                _opponentPool.Add(new WarmupPolicy(seed));
                _oppStats.Add((0f, 0L));
            }
            _opponentPool.Add(NewOpponentSnapshot());
            _oppStats.Add((0f, 0L));

            _env = new BatchedSelfPlayEnv(
                simOptions,
                rewardCfg,
                _rolloutDevice,
                cfg.RolloutsPerUpdate,
                cfg.ReactionLatencyFrames
            );
            _opt = torch.optim.Adam(_learner.Net.parameters(), lr: cfg.LearningRate);

            torch.manual_seed(seed);
        }

        static bool SameDevice(Device a, Device b) =>
            a.type == b.type && a.index == b.index;

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

                    int oppIdx = SampleOpponentIndex();
                    IPolicy opponent = _opponentPool[oppIdx];

                    System.Diagnostics.Stopwatch rolloutSw =
                        System.Diagnostics.Stopwatch.StartNew();
                    if (_hasRolloutMirror)
                        CopyWeights(_learner, _rolloutLearner);
                    (Trajectory[] trajArr, EpisodeResult[] eps) = _env.Run(
                        learnerIndices,
                        _rolloutLearner.Net,
                        opponent
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

                    var (prevEwma, prevSeen) = _oppStats[oppIdx];
                    float batchRate = (float)learnerWins / _cfg.RolloutsPerUpdate;
                    float a = _cfg.OpponentWinRateEma;
                    // Seed the EMA from the first batch instead of blending
                    // toward zero, otherwise the gate stays artificially low
                    // for the first several updates.
                    float oppWinRate = prevSeen == 0
                        ? batchRate
                        : a * batchRate + (1f - a) * prevEwma;
                    long newSeen = prevSeen + _cfg.RolloutsPerUpdate;
                    _oppStats[oppIdx] = (oppWinRate, newSeen);

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
                        OpponentStep = opponent is NeuralPolicy nopp ? nopp.TrainingStep : -1,
                        OpponentWinRate = oppWinRate,
                        PolicyLoss = policyLoss,
                        ValueLoss = valueLoss,
                        Entropy = entropy,
                        AvgBreakdown = breakdownSum / _cfg.RolloutsPerUpdate,
                        RolloutTime = rolloutSw.Elapsed,
                        OptimizeTime = optSw.Elapsed,
                        UpdateTime = updateSw.Elapsed,
                        FeaturizeTime = _env.LastFeaturizeTime,
                        ForwardTime = _env.LastForwardTime,
                        StepTime = _env.LastStepTime,
                        UpdateSecondsEma = updateSecondsEma,
                    };
                    reporter.OnUpdate(m);

                    if (update % _cfg.SnapshotEvery == 0 || update == _cfg.TotalUpdates)
                        WriteSnapshot(step);

                    if (ShouldPromoteOpponent())
                    {
                        _opponentPool.Add(NewOpponentSnapshot());
                        _oppStats.Add((0f, 0L));
                        while (_opponentPool.Count > Math.Max(1, _cfg.OpponentPoolSize))
                        {
                            _opponentPool.RemoveAt(0);
                            _oppStats.RemoveAt(0);
                        }
                        // Reset stats so the gate measures the new learner
                        // against the new pool, not the old learner's record.
                        // Without this the EMA would still sit above threshold
                        // and the gate could re-trip immediately.
                        for (int i = 0; i < _oppStats.Count; i++)
                            _oppStats[i] = (0f, 0L);
                    }

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
            // Recurrent PPO: minibatch unit is *envs*, not steps. Each
            // trajectory is one full episode, so the GRU runs from h=0 over
            // the whole sequence. We pad to T_max within a minibatch and
            // mask out the pad positions so they contribute 0 loss.
            int N = rollouts.Count;
            float[][] advs = new float[N][];
            float[][] rets = new float[N][];
            int total = 0;
            int totalValid = 0;
            for (int i = 0; i < N; i++)
            {
                ComputeAdvantages(
                    rollouts[i], _cfg.Gamma, _cfg.Lambda, out advs[i], out rets[i]
                );
                total += rollouts[i].Length;
                totalValid += rollouts[i].Length;
            }
            if (total == 0)
                return (0f, 0f, 0f);

            // Normalize advantages over the entire batch (across all envs and
            // valid steps). Pad steps don't exist yet at this point.
            float advMean = 0f;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < advs[i].Length; j++)
                    advMean += advs[i][j];
            advMean /= totalValid;
            float advVar = 0f;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < advs[i].Length; j++)
                {
                    float d = advs[i][j] - advMean;
                    advVar += d * d;
                }
            float advStd = MathF.Sqrt(advVar / Math.Max(1, totalValid - 1)) + 1e-8f;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < advs[i].Length; j++)
                    advs[i][j] = (advs[i][j] - advMean) / advStd;

            int obsDim = Featurizer.Length;
            int hidden = _learner.Net.Hidden;
            int envsPerMb = Math.Max(1, _cfg.EnvsPerMinibatch);

            float lastPolicyLoss = 0f,
                lastValueLoss = 0f,
                lastEntropy = 0f;
            int[] perm = new int[N];
            for (int i = 0; i < N; i++)
                perm[i] = i;

            for (int epoch = 0; epoch < _cfg.Epochs; epoch++)
            {
                // Fisher-Yates shuffle of trajectory indices. Each minibatch
                // is a contiguous window of envs from this permutation.
                for (int i = N - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (perm[i], perm[j]) = (perm[j], perm[i]);
                }

                for (int start = 0; start < N; start += envsPerMb)
                {
                    int end = Math.Min(N, start + envsPerMb);
                    int B = end - start;

                    int Tmax = 0;
                    for (int b = 0; b < B; b++)
                    {
                        int ti = perm[start + b];
                        if (rollouts[ti].Length > Tmax)
                            Tmax = rollouts[ti].Length;
                    }
                    if (Tmax == 0)
                        continue;

                    int rowFloats = Tmax * obsDim;
                    int rowSteps = Tmax;
                    float[] padObs = new float[B * rowFloats];
                    long[] padDir = new long[B * rowSteps];
                    long[] padBtn = new long[B * rowSteps];
                    float[] padOldLogp = new float[B * rowSteps];
                    float[] padAdv = new float[B * rowSteps];
                    float[] padRet = new float[B * rowSteps];
                    float[] padMask = new float[B * rowSteps];

                    for (int b = 0; b < B; b++)
                    {
                        int ti = perm[start + b];
                        Trajectory t = rollouts[ti];
                        float[] adv = advs[ti];
                        float[] ret = rets[ti];
                        int n = t.Length;
                        if (n > 0)
                        {
                            // Each row spans Tmax * obsDim floats; copy the
                            // valid prefix from the trajectory's contiguous
                            // ObsBuffer. Pad floats stay 0.
                            Buffer.BlockCopy(
                                t.ObsBuffer,
                                0,
                                padObs,
                                (b * rowFloats) * sizeof(float),
                                n * obsDim * sizeof(float)
                            );
                        }
                        int rowOff = b * rowSteps;
                        for (int i = 0; i < n; i++)
                        {
                            padDir[rowOff + i] = t.DirActions[i];
                            padBtn[rowOff + i] = t.BtnActions[i];
                            padOldLogp[rowOff + i] = t.LogProbs[i];
                            padAdv[rowOff + i] = adv[i];
                            padRet[rowOff + i] = ret[i];
                            padMask[rowOff + i] = 1f;
                        }
                    }

                    // Per-minibatch dispose scope. Forward + loss + backward +
                    // step all live inside; everything reclaims at scope exit
                    // instead of waiting on the .NET finalizer.
                    using var mbScope = torch.NewDisposeScope();

                    Tensor mbObs = tensor(
                        padObs, new long[] { B, Tmax, obsDim }, ScalarType.Float32
                    ).to(_device);
                    Tensor mbDir = tensor(
                        padDir, new long[] { B, Tmax }, ScalarType.Int64
                    ).to(_device);
                    Tensor mbBtn = tensor(
                        padBtn, new long[] { B, Tmax }, ScalarType.Int64
                    ).to(_device);
                    Tensor mbOldLogp = tensor(
                        padOldLogp, new long[] { B, Tmax }, ScalarType.Float32
                    ).to(_device);
                    Tensor mbAdv = tensor(
                        padAdv, new long[] { B, Tmax }, ScalarType.Float32
                    ).to(_device);
                    Tensor mbRet = tensor(
                        padRet, new long[] { B, Tmax }, ScalarType.Float32
                    ).to(_device);
                    Tensor mbMask = tensor(
                        padMask, new long[] { B, Tmax }, ScalarType.Float32
                    ).to(_device);
                    Tensor hIn = _learner.Net.InitHidden(B, _device);

                    var (newLogp, ent, value) = _learner.Net.Evaluate(
                        mbObs, hIn, mbDir, mbBtn
                    );

                    // All losses are computed elementwise and reduced over
                    // valid (mask == 1) steps. Pad rows contribute 0 to the
                    // sum but their value head outputs would otherwise leak
                    // into the value loss, so the mask is required there.
                    Tensor maskSum = mbMask.sum().clamp_min(1f);
                    Tensor ratio = (newLogp - mbOldLogp).exp();
                    Tensor surr1 = ratio * mbAdv;
                    Tensor surr2 = ratio.clamp(1f - _cfg.ClipEps, 1f + _cfg.ClipEps) * mbAdv;
                    Tensor policyLossPer = -torch.minimum(surr1, surr2);
                    Tensor policyLoss = (policyLossPer * mbMask).sum() / maskSum;

                    Tensor valueErr = value - mbRet;
                    Tensor valueLoss = (valueErr * valueErr * mbMask).sum() / maskSum;
                    Tensor entropy = (ent * mbMask).sum() / maskSum;

                    Tensor loss =
                        policyLoss + _cfg.ValueCoef * valueLoss - _cfg.EntropyCoef * entropy;

                    _opt.zero_grad();
                    loss.backward();
                    torch.nn.utils.clip_grad_norm_(_learner.Net.parameters(), _cfg.MaxGradNorm);
                    _opt.step();

                    // Only sync stats on the final minibatch of the final epoch.
                    // Trainer reports the last-iteration values; intermediate
                    // .cpu().item() calls were forcing a stream sync per minibatch.
                    bool isFinal = epoch == _cfg.Epochs - 1 && end >= N;
                    if (isFinal)
                    {
                        Tensor stats = stack(
                            new[] { policyLoss.detach(), valueLoss.detach(), entropy.detach() }
                        ).cpu();
                        float[] s = stats.data<float>().ToArray();
                        lastPolicyLoss = s[0];
                        lastValueLoss = s[1];
                        lastEntropy = s[2];
                    }
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

            string learnerName = $"Learner u{step:D6}";
            string opponentName = string.IsNullOrEmpty(ep.OpponentLabel) ? "Unknown" : ep.OpponentLabel;
            string p1Name = ep.LearnerIndex == 0 ? learnerName : opponentName;
            string p2Name = ep.LearnerIndex == 0 ? opponentName : learnerName;

            ReplayFile r = ReplayFile.Build(
                schemaHash: Featurizer.SchemaHash,
                p1Char: p1Char,
                p2Char: p2Char,
                p1Skin: 0,
                p2Skin: p1Char == p2Char ? 1 : 0,
                stage: Stage.Stage1,
                p1Inputs: ep.P1Inputs.ToArray(),
                p2Inputs: ep.P2Inputs.ToArray(),
                source: $"train:{_runId}:u{step:D6}",
                simOptions: _simOptions,
                p1Player: p1Name,
                p2Player: p2Name
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

        NeuralPolicy NewOpponentSnapshot()
        {
            NeuralPolicy snap = new NeuralPolicy(Featurizer.Length, _rolloutDevice, _cfg.Hidden);
            CopyWeights(_learner, snap);
            return snap;
        }

        // True iff every pool entry has at least MinGames games logged AND
        // winrate EMA >= threshold. Entries below MinGames block promotion
        // (they count as "not yet evaluated"), which forces full coverage of
        // the pool before a new snapshot can be pushed.
        bool ShouldPromoteOpponent()
        {
            if (_opponentPool.Count == 0)
                return false;
            for (int i = 0; i < _oppStats.Count; i++)
            {
                var (ewma, seen) = _oppStats[i];
                if (seen < _cfg.OpponentPromotionMinGames)
                    return false;
                if (ewma < _cfg.OpponentPromotionWinRate)
                    return false;
            }
            return true;
        }

        // Picks one entry from _opponentPool. Uniform mode = plain random index.
        // PFSP weights w_i = max(min, (1 - winrate_i)^k). Unseen entries
        // (games == 0) get the maximum weight 1 so freshly-added snapshots are
        // sampled hard until they have stats.
        int SampleOpponentIndex()
        {
            int n = _opponentPool.Count;
            if (n <= 1)
                return 0;
            if (_cfg.OpponentSamplingMode == OpponentSamplingMode.Uniform)
                return _rng.Next(n);

            Span<float> weights = stackalloc float[n];
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                var (ewma, seen) = _oppStats[i];
                float p = seen > 0 ? ewma : 0f;
                float weight = MathF.Pow(MathF.Max(0f, 1f - p), _cfg.PfspExponent);
                if (weight < _cfg.PfspMinWeight)
                    weight = _cfg.PfspMinWeight;
                weights[i] = weight;
                total += weight;
            }
            float draw = (float)_rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                acc += weights[i];
                if (draw <= acc)
                    return i;
            }
            return n - 1;
        }
    }
}
