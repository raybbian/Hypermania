using Design.Configs;
using Utils;

namespace Game.Sim
{
    public struct RhythmComboManager
    {
        /// <summary>
        /// Generates a dynamic combo using simulation and queues the resulting notes.
        /// Returns the number of hitstop frames to apply.
        /// </summary>
        public int StartRhythmCombo(
            Frame realFrame,
            ref ManiaState state,
            FighterFacing facingDir,
            GameOptions options,
            CharacterConfig characterConfig,
            GameState gameState,
            int attackerIndex
        )
        {
            // Hitstop bridges slow-mo end to the nearest beat boundary,
            // independent of where the first authored note falls.
            int fpb = options.Global.Audio.FramesPerBeat;
            Frame earliestStart = realFrame + options.Global.ManiaSlowTicks;

            // Next quarter-note boundary at or after earliestStart.
            int delta = earliestStart - options.Global.Audio.FirstMusicalBeat;
            int beats =
                delta >= 0
                    ? (delta + fpb - 1) / fpb // ceil
                    : delta / fpb; // C# truncation toward zero == ceil for negatives
            Frame nextBeat = options.Global.Audio.FirstMusicalBeat + options.Global.Audio.BeatsToFrame(beats);

            int hitstop = nextBeat - earliestStart;

            Frame[] noteFrames = options.Global.Audio.SliceFrom(earliestStart);

            if (noteFrames.Length == 0)
            {
                // Song chart exhausted — no combo to run this trigger.
                return 0;
            }

            // Generate combo dynamically via simulation against the authored
            // frame slice.
            GeneratedCombo combo = ComboGenerator.Generate(gameState, options, attackerIndex, noteFrames, hitstop);

            // Queue notes to mania channels. ComboGenerator already emits
            // world-space inputs (e.g. Dash | Left for a left-facing attacker's
            // forward dash), computed from the sim's per-beat facing. Do not
            // flip them here — the blanket flip based on combo-start facing
            // both inverts the dash direction and mishandles mid-combo
            // cross-ups, where the attacker's facing changes between beats.
            for (int i = 0; i < combo.Moves.Count; i++)
            {
                state.QueueNote(
                    i % 4,
                    new ManiaNote
                    {
                        Length = 0,
                        Tick = combo.Moves[i].BeatFrame,
                        HitInput = combo.Moves[i].Input,
                    }
                );
            }

            state.Enable(combo.EndFrame);
            return hitstop;
        }
    }
}
