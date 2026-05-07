using System;
using System.IO;
using Hypermania.Game;
using Hypermania.CPU.Hosting;
using Hypermania.CPU.Policy;
using Hypermania.CPU.Training;
using Hypermania.Game.Replay;
using Hypermania.Shared;
using TorchSharp;

namespace Hypermania.CPU
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            try
            {
                switch (args[0])
                {
                    case "train":
                        return RunTrain(args);
                    case "play":
                        return RunPlay(args);
                    case "eval":
                        return RunEval(args);
                    case "schema":
                        return DumpSchema();
                    default:
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 3;
            }
        }

        static int RunTrain(string[] args)
        {
            int episodes = 0; // 0 = use cfg.TotalUpdates default
            int rollouts = 0; // 0 = use cfg.RolloutsPerUpdate default
            int minibatch = 0; // envs per minibatch; 0 = use cfg.EnvsPerMinibatch default
            int hidden = 0; // 0 = use cfg.Hidden default
            int seed = 0;
            int snapshotEvery = 50;
            int replayEvery = 50;
            string outDir = Path.Combine("snapshots");
            string presetPath = null;
            string resumeFrom = null;
            bool useTui = false;
            bool cpuRollout = false;
            bool includeWarmup = true;
            OpponentSamplingMode? oppSampling = null;
            float? pfspExp = null;
            int? reactionLatency = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--episodes":
                        episodes = int.Parse(args[++i]);
                        break;
                    case "--rollouts":
                        rollouts = int.Parse(args[++i]);
                        break;
                    case "--minibatch":
                        minibatch = int.Parse(args[++i]);
                        break;
                    case "--hidden":
                        hidden = int.Parse(args[++i]);
                        break;
                    case "--seed":
                        seed = int.Parse(args[++i]);
                        break;
                    case "--snapshot-every":
                        snapshotEvery = int.Parse(args[++i]);
                        break;
                    case "--replay-every":
                        replayEvery = int.Parse(args[++i]);
                        break;
                    case "--out-dir":
                        outDir = args[++i];
                        break;
                    case "--config":
                        presetPath = args[++i];
                        break;
                    case "--resume":
                        resumeFrom = args[++i];
                        break;
                    case "--tui":
                        useTui = true;
                        break;
                    case "--cpu-rollout":
                        cpuRollout = true;
                        break;
                    case "--no-warmup":
                        includeWarmup = false;
                        break;
                    case "--opp-sampling":
                    {
                        string mode = args[++i];
                        oppSampling = mode.ToLowerInvariant() switch
                        {
                            "uniform" => OpponentSamplingMode.Uniform,
                            "pfsp" => OpponentSamplingMode.Pfsp,
                            _ => throw new ArgumentException(
                                $"--opp-sampling: expected uniform|pfsp, got '{mode}'"),
                        };
                        break;
                    }
                    case "--pfsp-exp":
                        pfspExp = float.Parse(args[++i]);
                        break;
                    case "--reaction-latency":
                        reactionLatency = int.Parse(args[++i]);
                        break;
                    default:
                        throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            SimOptions sim = ResolveSimOptions(presetPath);

            PpoConfig cfg = new PpoConfig
            {
                SnapshotEvery = snapshotEvery,
                ReplayEvery = replayEvery,
            };
            if (episodes > 0)
                cfg.TotalUpdates = episodes;
            if (rollouts > 0)
                cfg.RolloutsPerUpdate = rollouts;
            if (minibatch > 0)
                cfg.EnvsPerMinibatch = minibatch;
            if (hidden > 0)
                cfg.Hidden = hidden;
            if (oppSampling.HasValue)
                cfg.OpponentSamplingMode = oppSampling.Value;
            if (pfspExp.HasValue)
                cfg.PfspExponent = pfspExp.Value;
            if (reactionLatency.HasValue)
                cfg.ReactionLatencyFrames = reactionLatency.Value;

            torch.Device device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
            torch.Device rolloutDevice = cpuRollout ? torch.CPU : device;
            // The TUI takes over stdout - skip these intro lines so they don't
            // scroll above the live frame. ConsoleReporter prints its own.
            if (!useTui)
            {
                string optName = device.type == DeviceType.CUDA ? "CUDA" : "CPU";
                string rolloutName = rolloutDevice.type == DeviceType.CUDA ? "CUDA" : "CPU";
                Console.WriteLine(
                    optName == rolloutName
                        ? $"device: {optName}"
                        : $"device: optimize={optName} rollout={rolloutName}"
                );
                Console.WriteLine($"obs dim: {Featurization.Featurizer.Length}");
                Console.WriteLine($"schema hash: 0x{Featurization.Featurizer.SchemaHash:X16}");
            }

            PpoTrainer trainer = new PpoTrainer(
                sim,
                cfg,
                RewardConfig.Default,
                device,
                outDir,
                seed,
                resumeFrom: resumeFrom,
                rolloutDevice: rolloutDevice,
                includeWarmupOpponent: includeWarmup
            );

            ITrainingReporter reporter = useTui ? new TuiReporter() : new ConsoleReporter();
            trainer.Train(reporter);
            return 0;
        }

        static int RunEval(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("eval: missing <policy.hmpolicy>");
                return 1;
            }
            string policyPath = args[1];
            string outPath = null;
            string opponentSpec = "random";
            int seed = 0;
            string presetPath = null;
            int reactionLatency = new PpoConfig().ReactionLatencyFrames;

            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        outPath = args[++i];
                        break;
                    case "--opponent":
                        opponentSpec = args[++i];
                        break;
                    case "--seed":
                        seed = int.Parse(args[++i]);
                        break;
                    case "--config":
                        presetPath = args[++i];
                        break;
                    case "--reaction-latency":
                        reactionLatency = int.Parse(args[++i]);
                        break;
                    default:
                        throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            SimOptions sim = ResolveSimOptions(presetPath);
            torch.Device device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;

            NeuralPolicy learner;
            using (FileStream fs = File.OpenRead(policyPath))
                learner = NeuralPolicy.LoadFrom(fs, device);

            IPolicy opp = ResolveOpponent(opponentSpec, device, seed);

            SelfPlayEnv env = new SelfPlayEnv(sim, RewardConfig.Default, device, reactionLatency);
            Trajectory traj = new Trajectory(Featurization.Featurizer.Length);
            EpisodeResult ep = env.Run(0, learner.Net, opp, traj, greedyOpponent: true);

            Console.WriteLine(
                $"frames={ep.Frames} reward={ep.TotalReward:F3} learner_lives={ep.LearnerLives} opp_lives={ep.OpponentLives}"
            );

            if (!string.IsNullOrEmpty(outPath))
            {
                Character p1Char = sim.Players[0].Character?.Character ?? Character.Nythea;
                Character p2Char = sim.Players[1].Character?.Character ?? Character.Nythea;

                string learnerName = Path.GetFileNameWithoutExtension(policyPath);
                string opponentName = OpponentDisplayName(opponentSpec);
                string p1Name = ep.LearnerIndex == 0 ? learnerName : opponentName;
                string p2Name = ep.LearnerIndex == 0 ? opponentName : learnerName;

                ReplayFile r = ReplayFile.Build(
                    schemaHash: Featurization.Featurizer.SchemaHash,
                    p1Char: p1Char,
                    p2Char: p2Char,
                    p1Skin: 0,
                    p2Skin: p1Char == p2Char ? 1 : 0,
                    stage: Stage.Stage1,
                    p1Inputs: ep.P1Inputs.ToArray(),
                    p2Inputs: ep.P2Inputs.ToArray(),
                    source: $"eval:{Path.GetFileName(policyPath)}",
                    simOptions: sim,
                    p1Player: p1Name,
                    p2Player: p2Name
                );
                ReplayFile.Save(outPath, r);
                Console.WriteLine($"replay -> {outPath}");
            }
            return 0;
        }

        static string OpponentDisplayName(string spec)
        {
            if (spec == "random")
                return "Random AI";
            if (spec == "warmup")
                return "Warmup AI";
            return Path.GetFileNameWithoutExtension(spec);
        }

        static int RunPlay(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("play: missing <replay.hmrep>");
                return 1;
            }
            string replayPath = args[1];
            string presetPath = null;

            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--config":
                        presetPath = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            ReplayFile r = ReplayFile.Load(replayPath);
            if (r.Version != ReplayFile.CurrentVersion)
            {
                Console.Error.WriteLine(
                    $"replay version mismatch: file v{r.Version} vs current v{ReplayFile.CurrentVersion}; re-record."
                );
                return 2;
            }
            ulong schemaHash = Featurization.Featurizer.SchemaHash;
            if (r.SchemaHash != schemaHash)
                Console.Error.WriteLine(
                    $"warning: replay schema hash 0x{r.SchemaHash:X16} differs from current 0x{schemaHash:X16}; playback may diverge"
                );

            SimOptions sim = ResolveSimOptions(presetPath);
            TrainingEnv env = new TrainingEnv(sim);

            int frames = r.P1Inputs.Length;
            for (int i = 0; i < frames; i++)
            {
                bool ended = env.Step((InputFlags)r.P1Inputs[i], (InputFlags)r.P2Inputs[i]);
                if (ended)
                {
                    frames = i + 1;
                    break;
                }
            }

            int p1Lives = env.State.Fighters[0].Lives;
            int p2Lives = env.State.Fighters[1].Lives;
            float p1Hp = (float)env.State.Fighters[0].Health;
            float p2Hp = (float)env.State.Fighters[1].Health;
            string winner = p1Lives > p2Lives ? "P1" : (p2Lives > p1Lives ? "P2" : "draw");
            Console.WriteLine(
                $"frames={frames} mode={env.State.GameMode} winner={winner} p1=({p1Lives}L,{p1Hp:F0}HP) p2=({p2Lives}L,{p2Hp:F0}HP)"
            );
            Console.WriteLine($"players: P1={r.P1Player ?? ""} vs P2={r.P2Player ?? ""}");
            DateTimeOffset recordedAt = DateTimeOffset.FromUnixTimeSeconds(r.RecordedAtUnix);
            Console.WriteLine($"recorded: {recordedAt:u}  length: {r.MatchLengthTicks} ticks");
            Console.WriteLine($"source: {r.Source}");
            return 0;
        }

        static SimOptions ResolveSimOptions(string presetPath)
        {
            if (string.IsNullOrEmpty(presetPath))
                throw new ArgumentException(
                    "sim options not specified. pass --config <preset.bin>; generate one "
                        + "from the Unity editor menu 'Hypermania/Export Sim Options Preset...'."
                );
            return DefaultSimOptions.LoadPreset(presetPath);
        }

        static IPolicy ResolveOpponent(string spec, torch.Device device, int seed)
        {
            if (spec == "random")
                return new RandomPolicy(seed);
            if (spec == "warmup")
                return new WarmupPolicy(seed);
            if (File.Exists(spec))
            {
                using FileStream fs = File.OpenRead(spec);
                return NeuralPolicy.LoadFrom(fs, device);
            }
            throw new ArgumentException(
                $"unknown opponent spec: {spec} (try 'random', 'warmup', or a path to a .hmpolicy)"
            );
        }

        static int DumpSchema()
        {
            Console.WriteLine($"Observation length: {Featurization.Featurizer.Length}");
            Console.WriteLine($"  state:   {Featurization.Featurizer.StateLength}");
            Console.WriteLine($"  history: {Featurization.Featurizer.HistoryLength}");
            Console.WriteLine($"Schema hash: 0x{Featurization.Featurizer.SchemaHash:X16}");
            return 0;
        }

        static void PrintHelp()
        {
            Console.WriteLine("Hypermania.CPU");
            Console.WriteLine("  train  --config <preset.bin> [--episodes N] [--rollouts K] [--seed S]");
            Console.WriteLine("         [--minibatch B] [--hidden H]");
            Console.WriteLine("         [--snapshot-every M] [--replay-every R] [--out-dir <dir>]");
            Console.WriteLine("         [--resume <policy.hmpolicy>] [--tui]");
            Console.WriteLine("         [--reaction-latency N] [--cpu-rollout] [--no-warmup]");
            Console.WriteLine(
                "  eval   <policy.hmpolicy> --config <preset.bin> [--out <replay.hmrep>]"
            );
            Console.WriteLine("         [--opponent random|warmup|<other.hmpolicy>] [--seed S]");
            Console.WriteLine("         [--reaction-latency N]");
            Console.WriteLine("  play   <replay.hmrep> --config <preset.bin>");
            Console.WriteLine("  schema");
            Console.WriteLine();
            Console.WriteLine("Generate <preset.bin> from the Unity editor:");
            Console.WriteLine("  Hypermania > Export Sim Options Preset...");
        }
    }
}
