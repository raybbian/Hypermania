using System;
using Design.Configs;
using Game.Sim;
using UnityEngine.InputSystem;
using Utils.EnumArray;

namespace Game
{
    public class InputBuffer
    {
        private ControlsConfig _controlsConfig;
        private EnumArray<InputFlags, Binding> _controlScheme;

        /**
         * Base InputBuffer Constructor
         *
         * Constructs an InputBuffer to accept user input
         *
         * @param config - The Scriptable ControlsConfig Object to Reference
         *
         */
        public InputBuffer(ControlsConfig config)
        {
            _controlsConfig = config;
            _controlScheme = _controlsConfig.GetControlScheme();
        }

        private InputFlags _input = InputFlags.None;
        private static (InputFlags dir, InputFlags opp)[] _dirPairs =
        {
            (InputFlags.Left, InputFlags.Right),
            (InputFlags.Up, InputFlags.Down),
        };

        public void Saturate()
        {
            foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
            {
                if (flag == InputFlags.None)
                {
                    continue; // Skips the None InputFlag (Does Not Have a Key Press)
                }

                // Checks if either the primary or alt button set in config is pressed
                // Ignores keys set to none
                if (
                    (
                        _controlScheme[flag].GetPrimaryKey() != Key.None
                        && Keyboard.current[_controlScheme[flag].GetPrimaryKey()].isPressed
                    )
                    || (
                        _controlScheme[flag].GetAltKey() != Key.None
                        && Keyboard.current[_controlScheme[flag].GetAltKey()].isPressed
                    )
                )
                {
                    _input |= flag;
                }
            }

            // clean inputs: cancel directionals
            foreach ((InputFlags dir, InputFlags opp) in _dirPairs)
            {
                if ((_input & dir) != 0 && (_input & opp) != 0)
                {
                    _input &= ~dir;
                    _input &= ~opp;
                }
            }
        }

        public void Clear()
        {
            _input = InputFlags.None;
        }

        public GameInput Poll()
        {
            return new GameInput(_input);
        }
    }
}
