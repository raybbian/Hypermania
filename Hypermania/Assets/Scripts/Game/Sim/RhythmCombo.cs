using System;
using System.Collections.Generic;
using Design.Configs;
using Utils;

namespace Game.Sim
{
    public struct RhythmComboManager
    {
        // returns number of hitstop frames
        public int StartRhythmCombo(
            Frame realFrame,
            ref ManiaState state,
            FighterFacing facingDir,
            GameOptions options,
            CharacterConfig characterConfig
        )
        {
            // set hitstop to the next beat
            Frame nextBeat = realFrame;
            while (nextBeat - realFrame < options.Global.ManiaSlowTicks)
            {
                nextBeat = options.Global.Audio.NextBeat(nextBeat + 1, AudioConfig.BeatSubdivision.QuarterNote);
            }

            // TODO: pick from valid starting move
            ComboConfig combo = characterConfig.Combos[0];

            int hitstop = nextBeat - (realFrame + options.Global.ManiaSlowTicks);
            for (int i = 0; i < combo.Moves.Count; i++)
            {
                InputFlags hitInput = combo.Moves[i].Input;
                if (facingDir == FighterFacing.Left)
                {
                    hitInput = GameInput.FlipHorizontalInputs(hitInput);
                }
                state.QueueNote(
                    i % 4,
                    new ManiaNote
                    {
                        Length = 0,
                        Tick = nextBeat,
                        HitInput = hitInput,
                    }
                );
                nextBeat = options.Global.Audio.NextBeat(nextBeat + 1, combo.Moves[i].DelayAfter);
            }

            state.Enable(nextBeat);
            return hitstop;
        }
    }
}
