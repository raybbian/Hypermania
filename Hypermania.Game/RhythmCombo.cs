
using Hypermania.Shared;
using Hypermania.Shared.SoftFloat;
using MemoryPack;

namespace Hypermania.Game
{
    [MemoryPackable]
    public partial struct RhythmComboManager
    {
        // Generates a dynamic combo by simulating it, queues the resulting
        // notes, and returns the number of hitstop frames to apply.
        public int StartRhythmCombo(
            Frame realFrame,
            ref ManiaState state,
            SimOptions options,
            in GameState gameState,
            int attackerIndex,
            int comboBeatCount
        )
        {
            // Hitstop bridges the end of slow-mo to the next beat boundary,
            // independent of where the first authored note falls.
            //
            // We shift earliestStart forward past both the ManiaSlowTicks
            // boundary AND the first note's own hit window, so the entire
            // input window [firstNote - halfRange, firstNote + halfRange]
            // sits inside GameMode.Mania (where DoManiaStep runs
            // ManiaState.Tick). Without this padding, a beat aligned right
            // at the end of slow-mo would put the early frames of its hit
            // window inside ManiaStart, which never ticks the mania, so an
            // otherwise-valid press would be silently dropped and the note
            // would auto-miss. The +3 covers the 1-2 RealFrame gap between
            // DoManiaStart's nominal ManiaSlowTicks boundary and the first
            // frame DoManiaStep actually runs (the gap comes from
            // PartialSimFrameCount sub-frame gating under SpeedRatio=0.5,
            // plus the switch statement re-entering on GameMode==Mania the
            // frame after the transition).
            //
            // ManiaStartPaddingTicks adds extra sim frames after slow-mo
            // ends (inside GameMode.Mania at SpeedRatio=1, before any queued
            // note), so heavy aerial knockback or super followups can resolve
            // and the defender can land before the first note fires.
            AudioStats audio = options.Global.Audio;
            int halfRange = state.Config.HitHalfRange;
            Frame earliestStart =
                realFrame + options.Global.ManiaSlowTicks + options.Global.ManiaStartPaddingTicks + halfRange + 3;

            // Next quarter-note boundary at or after earliestStart. Use the
            // exact fpb ratio rather than the pre-rounded FramesPerBeat. An
            // integer ceil against a rounded fpb can pick the wrong beat
            // when fpb's rounding direction disagrees with the true 3600/BPM,
            // occasionally yielding nextBeat < earliestStart.
            sfloat framesPerBeatExact = (sfloat)60f / audio.Bpm * (sfloat)SimConstants.TPS;
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
                // Song chart exhausted, no combo to run this trigger.
                return 0;
            }

            // Generate combo dynamically via simulation against the authored
            // (frame, channel) slice.
            GeneratedCombo combo = ComboGenerator.Generate(gameState, options, attackerIndex, notes, hitstop);

            // Queue notes to mania channels. ComboGenerator already emits
            // world-space inputs (e.g. Dash | Left for a left-facing
            // attacker's forward dash), computed from the sim's per-beat
            // facing. Don't flip them here. The old blanket flip based on
            // combo-start facing both inverted the dash direction and broke
            // mid-combo cross-ups where the attacker's facing changes between
            // beats. Channel comes from the authored beatmap (.osz column),
            // and ComboGenerator threads it onto each emitted move.
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
            return hitstop;
        }
    }
}
