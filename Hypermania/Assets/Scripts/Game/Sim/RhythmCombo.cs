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
            GameOptions options,
            in GameState gameState,
            int attackerIndex,
            int comboBeatCount
        )
        {
            // Hitstop bridges slow-mo end to the nearest beat boundary,
            // independent of where the first authored note falls.
            //
            // Shift earliestStart forward past both the ManiaSlowTicks
            // boundary AND the first note's own hit window so that the
            // entire input window [firstNote - halfRange, firstNote + halfRange]
            // lies inside GameMode.Mania (where DoManiaStep runs
            // ManiaState.Tick). Without this padding, a beat aligned right
            // at the end of the slow-mo would put the early frames of its
            // hit window inside ManiaStart — those frames never tick the
            // mania, so an otherwise-valid press would be silently dropped
            // and the note would auto-miss. The +3 covers the 1-2 RealFrame
            // gap between DoManiaStart's nominal ManiaSlowTicks boundary
            // and the first frame DoManiaStep actually runs (the gap comes
            // from PartialSimFrameCount sub-frame gating under
            // SpeedRatio=0.5, plus the switch statement re-entering on
            // GameMode==Mania the frame after the transition).
            int fpb = options.Global.Audio.FramesPerBeat;
            int halfRange = state.Config.HitHalfRange;
            Frame earliestStart = realFrame + options.Global.ManiaSlowTicks + halfRange + 3;

            // Next quarter-note boundary at or after earliestStart.
            int delta = earliestStart - options.Global.Audio.FirstMusicalBeat;
            int beats =
                delta >= 0
                    ? (delta + fpb - 1) / fpb // ceil
                    : delta / fpb; // C# truncation toward zero == ceil for negatives
            Frame nextBeat = options.Global.Audio.FirstMusicalBeat + options.Global.Audio.BeatsToFrame(beats);

            int hitstop = nextBeat - earliestStart;

            Frame[] noteFrames = options.Global.Audio.SliceFrom(earliestStart, comboBeatCount);

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

            if (options.InfoOptions != null && options.InfoOptions.VerifyComboPrediction && combo.BeatSnapshots != null)
            {
                for (int i = 0; i < combo.BeatSnapshots.Count; i++)
                {
                    ComboBeatSnapshot snap = combo.BeatSnapshots[i];
                    ComboVerifyDebug.StorePrediction(snap.CompareFrame, snap.Predicted, attackerIndex);
                }
            }

            return hitstop;
        }
    }
}
