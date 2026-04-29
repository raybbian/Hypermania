using System;
using System.IO;
using Game.Sim;
using Game.Sim.Observation;
using Game.Sim.Replay;
using Hypermania.CPU.Hosting;
using Hypermania.CPU.Policy;
using Hypermania.CPU.Training;
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
                    case "train": return RunTrain(args);
                    case "play":  return RunPlay(args);
                    case "eval":  return RunEval(args);
                    case "schema": return DumpSchema();
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
            int episodes = 0;          // 0 = use cfg.TotalUpdates default
            int seed = 0;
            int snapshotEvery = 50;
            int replayEvery = 50;
            string outDir = Path.Combine("snapshots");
            string presetPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--episodes": episodes = int.Parse(args[++i]); break;
                    case "--seed": seed = int.Parse(args[++i]); break;
                    case "--snapshot-every": snapshotEvery = int.Parse(args[++i]); break;
                    case "--replay-every": replayEvery = int.Parse(args[++i]); break;
                    case "--out-dir": outDir = args[++i]; break;
                    case "--config": presetPath = args[++i]; break;
                    default: throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            SimOptions sim = ResolveSimOptions(presetPath);

            PpoConfig cfg = new PpoConfig
            {
                SnapshotEvery = snapshotEvery,
                ReplayEvery = replayEvery,
            };
            if (episodes > 0) cfg.TotalUpdates = episodes;

            torch.Device device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;
            Console.WriteLine($"device: {(device.type == DeviceType.CUDA ? "CUDA" : "CPU")}");
            Console.WriteLine($"obs dim: {Featurization.Featurizer.Length}");
            Console.WriteLine($"schema hash: 0x{Featurization.Featurizer.SchemaHash:X16}");

            PpoTrainer trainer = new PpoTrainer(sim, cfg, RewardConfig.Default, device, outDir, seed);
            Console.WriteLine($"run dir: {trainer.RunDir}");
            trainer.Train();
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

            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": outPath = args[++i]; break;
                    case "--opponent": opponentSpec = args[++i]; break;
                    case "--seed": seed = int.Parse(args[++i]); break;
                    case "--config": presetPath = args[++i]; break;
                    default: throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            SimOptions sim = ResolveSimOptions(presetPath);
            torch.Device device = torch.cuda.is_available() ? torch.CUDA : torch.CPU;

            NeuralPolicy learner = new NeuralPolicy(Featurization.Featurizer.Length, device);
            using (FileStream fs = File.OpenRead(policyPath))
                learner.Load(fs);

            IPolicy opp = ResolveOpponent(opponentSpec, device, seed);

            SelfPlayEnv env = new SelfPlayEnv(sim, RewardConfig.Default, device);
            Trajectory traj = new Trajectory();
            EpisodeResult ep = env.Run(0, learner.Net, opp, traj, maxFrames: 60 * 90);

            Console.WriteLine($"frames={ep.Frames} reward={ep.TotalReward:F3} learner_lives={ep.LearnerLives} opp_lives={ep.OpponentLives}");

            if (!string.IsNullOrEmpty(outPath))
            {
                ReplayFile r = ReplayFile.Build(
                    schemaHash: ObservationSchema.Hash(typeof(GameState)),
                    p1Char: sim.Players[0].Character?.Character ?? Character.Nythea,
                    p2Char: sim.Players[1].Character?.Character ?? Character.Nythea,
                    p1Inputs: ep.P1Inputs.ToArray(),
                    p2Inputs: ep.P2Inputs.ToArray(),
                    source: $"eval:{Path.GetFileName(policyPath)}"
                );
                ReplayFile.Save(outPath, r);
                Console.WriteLine($"replay -> {outPath}");
            }
            return 0;
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
                    case "--config": presetPath = args[++i]; break;
                    default: throw new ArgumentException($"unknown flag: {args[i]}");
                }
            }

            ReplayFile r = ReplayFile.Load(replayPath);
            ulong schemaHash = ObservationSchema.Hash(typeof(GameState));
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
                if (ended) { frames = i + 1; break; }
            }

            int p1Lives = env.State.Fighters[0].Lives;
            int p2Lives = env.State.Fighters[1].Lives;
            float p1Hp = (float)env.State.Fighters[0].Health;
            float p2Hp = (float)env.State.Fighters[1].Health;
            string winner = p1Lives > p2Lives ? "P1" : (p2Lives > p1Lives ? "P2" : "draw");
            Console.WriteLine($"frames={frames} mode={env.State.GameMode} winner={winner} p1=({p1Lives}L,{p1Hp:F0}HP) p2=({p2Lives}L,{p2Hp:F0}HP)");
            Console.WriteLine($"source: {r.Source}");
            return 0;
        }

        static SimOptions ResolveSimOptions(string presetPath)
        {
            if (string.IsNullOrEmpty(presetPath))
                throw new ArgumentException(
                    "sim options not specified. pass --config <preset.bin>; generate one " +
                    "from the Unity editor menu 'Hypermania/Export Sim Options Preset...'."
                );
            return DefaultSimOptions.LoadPreset(presetPath);
        }

        static IPolicy ResolveOpponent(string spec, torch.Device device, int seed)
        {
            if (spec == "random") return new RandomPolicy(seed);
            if (File.Exists(spec))
            {
                NeuralPolicy p = new NeuralPolicy(Featurization.Featurizer.Length, device);
                using FileStream fs = File.OpenRead(spec);
                p.Load(fs);
                return p;
            }
            throw new ArgumentException($"unknown opponent spec: {spec} (try 'random' or a path to a .hmpolicy)");
        }

        static int DumpSchema()
        {
            ObservationField[] fields = ObservationSchema.For(typeof(GameState));
            Console.WriteLine($"GameState observation schema: {fields.Length} fields");
            Console.WriteLine($"Schema hash: 0x{ObservationSchema.Hash(typeof(GameState)):X16}");
            for (int i = 0; i < Math.Min(fields.Length, 40); i++)
                Console.WriteLine($"  [{i,3}] {fields[i].Path} : {fields[i].Type.Name}");
            if (fields.Length > 40) Console.WriteLine($"  ... ({fields.Length - 40} more)");
            return 0;
        }

        static void PrintHelp()
        {
            Console.WriteLine("Hypermania.CPU");
            Console.WriteLine("  train  --config <preset.bin> [--episodes N] [--seed S]");
            Console.WriteLine("         [--snapshot-every M] [--replay-every R] [--out-dir <dir>]");
            Console.WriteLine("  eval   <policy.hmpolicy> --config <preset.bin> [--out <replay.hmrep>]");
            Console.WriteLine("         [--opponent random|<other.hmpolicy>] [--seed S]");
            Console.WriteLine("  play   <replay.hmrep> --config <preset.bin>");
            Console.WriteLine("  schema");
            Console.WriteLine();
            Console.WriteLine("Generate <preset.bin> from the Unity editor:");
            Console.WriteLine("  Hypermania > Export Sim Options Preset...");
        }
    }
}
