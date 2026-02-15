using System;
using Game.Sim;
using UnityEngine;
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

        InputFlags input = InputFlags.None;

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
                    input |= flag;
                }
            }
        }

        public void Clear()
        {
            input = InputFlags.None;
        }

        public GameInput Poll()
        {
            return new GameInput(input);
        }
    }
}
