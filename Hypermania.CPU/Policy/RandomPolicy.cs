using System;
using System.IO;
using Game.Sim;

namespace Hypermania.CPU.Policy
{
    // Sanity-check policy. Picks one direction and one attack per frame from
    // a seeded RNG so episode replays are bit-reproducible.
    public sealed class RandomPolicy : IPolicy
    {
        static readonly InputFlags[] Directions =
        {
            InputFlags.None,
            InputFlags.Left,
            InputFlags.Right,
            InputFlags.Down,
            InputFlags.Up,
        };

        static readonly InputFlags[] Buttons =
        {
            InputFlags.None,
            InputFlags.LightAttack,
            InputFlags.MediumAttack,
            InputFlags.HeavyAttack,
            InputFlags.SpecialAttack,
            InputFlags.Dash,
            InputFlags.Grab,
        };

        readonly Random _rng;

        public RandomPolicy(int seed) => _rng = new Random(seed);

        public InputFlags Act(in GameState state, SimOptions options, int fighterIndex)
        {
            return Directions[_rng.Next(Directions.Length)] | Buttons[_rng.Next(Buttons.Length)];
        }

        // Random already samples uniformly; greedy/sample are identical here.
        public InputFlags ActSample(in GameState state, SimOptions options, int fighterIndex) =>
            Act(state, options, fighterIndex);

        public void Save(Stream s) { }

        public void Load(Stream s) { }
    }
}
