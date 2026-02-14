using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils.EnumArray;

namespace Game
{
    public class InputBuffer
    {
        protected ControlsConfig controlsConfig;
        protected EnumArray<InputFlags, Binding> controlScheme;

        public InputBuffer(ControlsConfig config)
        {
            controlsConfig = config;
            controlScheme = controlsConfig.GetControlScheme();
        }

        public InputBuffer() { }

        InputFlags input = InputFlags.None;

        public void Saturate()
        {
            foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
            {
                if (flag == InputFlags.None)
                {
                    continue; //The None Input Flag does not need to be set for Key Pressed
                }

                //Checks if there are 0 inputs in the config
                if (controlScheme[flag].GetPrimaryKey() == Key.None && controlScheme[flag].GetAltKey() == Key.None)
                {
                    if (Keyboard.current[controlsConfig.GetDefaultBinding(flag)].isPressed)
                        input |= flag; //Switches to Default Bindings
                }
                else if //Checks if either primary/alt button set by controlConfig is pressed; Skips if not set
                (
                    (
                        controlScheme[flag].GetPrimaryKey() != Key.None
                        && Keyboard.current[controlScheme[flag].GetPrimaryKey()].isPressed
                    )
                    || (
                        controlScheme[flag].GetAltKey() != Key.None
                        && Keyboard.current[controlScheme[flag].GetAltKey()].isPressed
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
