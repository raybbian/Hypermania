using Design.Configs;
using Game;
using Utils;
using Utils.SoftFloat;

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
            //
            // ManiaStartPaddingTicks adds additional sim frames after slow-mo
            // ends (inside GameMode.Mania at SpeedRatio=1, before any queued
            // note), so heavy aerial knockback / super followups can resolve
            // and the defender can land before the first note fires.
            AudioConfig audio = options.Global.Audio;
            int halfRange = state.Config.HitHalfRange;
            Frame earliestStart =
                realFrame + options.Global.ManiaSlowTicks + options.Global.ManiaStartPaddingTicks + halfRange + 3;

            // Next quarter-note boundary at or after earliestStart. Use the
            // exact fpb ratio rather than the pre-rounded FramesPerBeat — the
            // integer ceil against a rounded fpb could pick the wrong beat
            // when fpb's rounding direction disagreed with the true 3600/BPM,
            // occasionally yielding nextBeat < earliestStart.
            sfloat framesPerBeatExact = (sfloat)60f / audio.Bpm * (sfloat)GameManager.TPS;
            Frame firstBeat = audio.FirstBeatFrame(options.Global.PreGameDelayTicks);
            int delta = earliestStart - firstBeat;
            int beats = Mathsf.CeilToInt((sfloat)delta / framesPerBeatExact);
            Frame nextBeat = firstBeat + audio.BeatsToFrame(beats);

            int hitstop = nextBeat - earliestStart;

            ManiaDifficulty difficulty = options.Players[attackerIndex].ManiaDifficulty;
            BeatmapNote[] notes = audio.SliceFrom(
                earliestStart,
                difficulty,
                options.Global.PreGameDelayTicks,
                comboBeatCount
            );

            if (notes.Length == 0)
            {
                // Song chart exhausted — no combo to run this trigger.
                return 0;
            }

            // Generate combo dynamically via simulation against the authored
            // (frame, channel) slice.
            GeneratedCombo combo = ComboGenerator.Generate(gameState, options, attackerIndex, notes, hitstop);

            // Queue notes to mania channels. ComboGenerator already emits
            // world-space inputs (e.g. Dash | Left for a left-facing attacker's
            // forward dash), computed from the sim's per-beat facing. Do not
            // flip them here — the blanket flip based on combo-start facing
            // both inverts the dash direction and mishandles mid-combo
            // cross-ups, where the attacker's facing changes between beats.
            // Channel comes from the authored beatmap (.osz column), routed by
            // ComboGenerator onto each emitted move.
            for (int i = 0; i < combo.Moves.Count; i++)
            {
                state.QueueNote(
                    combo.Moves[i].Channel,
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
