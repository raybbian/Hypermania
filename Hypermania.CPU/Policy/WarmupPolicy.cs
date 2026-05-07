using System;
using System.IO;
using Hypermania.Game;

namespace Hypermania.CPU.Policy
{
    // Scripted warmup opponent. Blocks any attack it sees (state-aware
    // low/high, holds through blockstun so multi-hit strings stay blocked)
    // and fires a heavy on a fixed cadence. Lives in the opponent pool from
    // update 1 so the learner gets non-trivial signal before any neural
    // snapshot has been promoted.
    //
    // Stateless aside from the seeded RNG used by ActSample for variation.
    // No torch, no obs vector - reads GameState directly.
    public sealed class WarmupPolicy : IPolicy
    {
        public const byte SnapshotVersion = 1;

        // One heavy every N frames, anchored to round start. Sparse and
        // predictable on purpose - the learner should be able to time its
        // whiff punishes.
        const int AttackPeriodFrames = 60;

        // Probability of dropping the action to None on any given ActSample
        // call. Adds enough noise that the learner doesn't see a frame-perfect
        // schedule. Greedy Act ignores this for reproducible eval.
        const double IdleNoiseRate = 0.05;

        readonly Random _rng;

        public WarmupPolicy(int seed)
        {
            _rng = new Random(seed);
        }

        public InputFlags Act(in GameState state, SimOptions options, int fighterIndex) =>
            Decide(state, fighterIndex, addNoise: false);

        public InputFlags ActSample(in GameState state, SimOptions options, int fighterIndex) =>
            Decide(state, fighterIndex, addNoise: true);

        InputFlags Decide(in GameState state, int fighterIndex, bool addNoise)
        {
            FighterState self = state.Fighters[fighterIndex];
            FighterState opp = state.Fighters[1 - fighterIndex];

            bool inBlockstun =
                self.State == CharacterState.BlockStand
                || self.State == CharacterState.BlockCrouch;

            // Block any attack we see, regardless of distance. Intentionally
            // over-eager - the learner needs to see clean blocked strings,
            // not a spaced-out reactive blocker.
            //
            // Hold back through blockstun even though the fighter isn't
            // Actionable: releasing back ends the block and the next hit of
            // a multi-hit string lands clean.
            if (inBlockstun || opp.State.IsAttacking())
            {
                bool isLow = opp.State.IsCrouchingAttack();
                return self.BackwardInput | (isLow ? InputFlags.Down : InputFlags.None);
            }

            // Hitstun, knockdown, grabbed, super-freeze: nothing useful to do.
            if (!self.State.IsActionable())
                return InputFlags.None;

            // Heavy on a fixed cadence anchored to round start. No range gate
            // so the bot whiffs in neutral - the learner is supposed to learn
            // to punish that.
            int sinceRoundStart = state.SimFrame.No - state.RoundStart.No;
            if (sinceRoundStart >= 0 && sinceRoundStart % AttackPeriodFrames == 0)
                return InputFlags.HeavyAttack;

            if (addNoise && _rng.NextDouble() < IdleNoiseRate)
                return InputFlags.None;

            // Default: walk toward the opponent.
            return self.ForwardInput;
        }

        public void Save(Stream s)
        {
            s.WriteByte(SnapshotVersion);
        }

        public void Load(Stream s)
        {
            int b = s.ReadByte();
            if (b < 0)
                throw new EndOfStreamException("WarmupPolicy snapshot truncated");
            if ((byte)b != SnapshotVersion)
                throw new InvalidDataException(
                    $"unsupported WarmupPolicy snapshot version: {b}"
                );
        }
    }
}
