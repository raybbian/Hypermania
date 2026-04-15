using System;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Utils.EnumArray;

namespace Game
{
    public class InputBuffer
    {
        private EnumArray<InputFlags, Binding> _controlScheme;
        private InputDevice _inputDevice;
        private float _joystickDeadzone;
        private float _triggerThreshold;

        /**
         * Base InputBuffer Constructor
         *
         * Constructs an InputBuffer to accept user input
         *
         * @param config - The Scriptable ControlsConfig Object to Reference
         * @param inputDevice - The InputDevice to read inputs from
         *
         */
        public InputBuffer(
            InputDevice inputDevice,
            EnumArray<InputFlags, Binding> controlScheme,
            float joystickDeadzone = 0.25f,
            float triggerThreshold = 0.25f
        )
        {
            _controlScheme = controlScheme;
            _inputDevice = inputDevice;
            _joystickDeadzone = joystickDeadzone;
            _triggerThreshold = triggerThreshold;
        }

        private InputFlags _input = InputFlags.None;
        private static (InputFlags dir, InputFlags opp)[] _dirPairs =
        {
            (InputFlags.Left, InputFlags.Right),
            (InputFlags.Up, InputFlags.Down),
        };

        public void Saturate()
        {
            if (_inputDevice == null)
                return;

            if (_inputDevice is Keyboard keyboard)
            {
                foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
                {
                    if (flag == InputFlags.None)
                    {
                        continue; // Skips the None InputFlag (Does Not Have a Key Press)
                    }

                    if (
                        (
                            _controlScheme[flag].GetPrimaryKey() != Key.None
                            && keyboard[_controlScheme[flag].GetPrimaryKey()].isPressed
                        )
                        || (
                            _controlScheme[flag].GetAltKey() != Key.None
                            && keyboard[_controlScheme[flag].GetAltKey()].isPressed
                        )
                    )
                    {
                        _input |= flag;
                    }
                }
            }
            else if (_inputDevice is Gamepad gamePad)
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
                        IsGamepadButtonPressed(gamePad, _controlScheme[flag].GetPrimaryGamepadButton())
                        || IsGamepadButtonPressed(gamePad, _controlScheme[flag].GetAltGamepadButton())
                    )
                    {
                        _input |= flag;
                    }
                }

                // TODO: fixme hardcode
                if (gamePad.leftStick.x.value < -_joystickDeadzone)
                {
                    _input |= InputFlags.Left;
                }
                if (gamePad.leftStick.x.value > _joystickDeadzone)
                {
                    _input |= InputFlags.Right;
                }
                if (gamePad.leftStick.y.value < -_joystickDeadzone)
                {
                    _input |= InputFlags.Down;
                }
                if (gamePad.leftStick.y.value > _joystickDeadzone)
                {
                    _input |= InputFlags.Up;
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

        // Handles both binary and analog trigger reports. Binary-reporting devices
        // populate the bit/value as 0/1 and register via ButtonControl.isPressed.
        // Analog-reporting devices get a lower _triggerThreshold so partial pulls
        // register instead of having to cross Unity's default pressPoint (0.5).
        private bool IsGamepadButtonPressed(Gamepad gamepad, GamepadButtons button)
        {
            if (button == GamepadButtons.None)
                return false;
            if (gamepad[(GamepadButton)button].isPressed)
                return true;
            if (button == GamepadButtons.LeftTrigger)
                return gamepad.leftTrigger.value >= _triggerThreshold;
            if (button == GamepadButtons.RightTrigger)
                return gamepad.rightTrigger.value >= _triggerThreshold;
            return false;
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
