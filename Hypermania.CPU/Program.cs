using System;
using Game.Sim;
using Game.Sim.Observation;

namespace Hypermania.CPU
{
    // Skeleton CLI. Real subcommands will be filled in by Phase 6 and
    // whichever ML library lands.
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            switch (args[0])
            {
                case "train":
                    Console.Error.WriteLine("train: not implemented yet (Phase 6).");
                    return 2;
                case "play":
                    Console.Error.WriteLine("play: not implemented yet (Phase 5/6).");
                    return 2;
                case "eval":
                    Console.Error.WriteLine("eval: not implemented yet.");
                    return 2;
                case "schema":
                    return DumpSchema();
                default:
                    PrintHelp();
                    return 1;
            }
        }

        static int DumpSchema()
        {
            ObservationField[] fields = ObservationSchema.For(typeof(GameState));
            Console.WriteLine($"GameState observation schema: {fields.Length} fields");
            Console.WriteLine($"Schema hash: 0x{ObservationSchema.Hash(typeof(GameState)):X16}");
            for (int i = 0; i < Math.Min(fields.Length, 40); i++)
            {
                Console.WriteLine($"  [{i,3}] {fields[i].Path} : {fields[i].Type.Name}");
            }
            if (fields.Length > 40) Console.WriteLine($"  ... ({fields.Length - 40} more)");
            return 0;
        }

        static void PrintHelp()
        {
            Console.WriteLine("Hypermania.CPU");
            Console.WriteLine("  train [--episodes N] [--seed S]   Run self-play training (TODO)");
            Console.WriteLine("  play <replay.hmrep>               Play back a recorded replay (TODO)");
            Console.WriteLine("  eval <policy.hmpolicy>            Evaluate a policy snapshot (TODO)");
            Console.WriteLine("  schema                            Print the observation-field layout");
        }
    }
}
