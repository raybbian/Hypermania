using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Utils.EnumArray;

namespace Design.Configs
{
    [CreateAssetMenu(menuName = "Hypermania/Controls Config")]
    public class ControlsConfig : ScriptableObject
    {
        [SerializeField]
        private EnumArray<InputFlags, Binding> _controlScheme;

        public EnumArray<InputFlags, Binding> ControlScheme => _controlScheme;

        // Dictionary for default bindings
        private static readonly Dictionary<InputFlags, Binding> _defaultBindings = new()
        {
            { InputFlags.None, new Binding(Key.None, Key.None, GamepadButtons.None, GamepadButtons.None) },
            { InputFlags.Up, new Binding(Key.W, Key.Space, GamepadButtons.DpadUp, GamepadButtons.None) },
            { InputFlags.Down, new Binding(Key.S, Key.None, GamepadButtons.DpadDown, GamepadButtons.None) },
            { InputFlags.Left, new Binding(Key.A, Key.None, GamepadButtons.DpadLeft, GamepadButtons.None) },
            { InputFlags.Right, new Binding(Key.D, Key.None, GamepadButtons.DpadRight, GamepadButtons.None) },
            { InputFlags.LightAttack, new Binding(Key.J, Key.None, GamepadButtons.West, GamepadButtons.None) },
            { InputFlags.MediumAttack, new Binding(Key.K, Key.None, GamepadButtons.North, GamepadButtons.None) },
            { InputFlags.HeavyAttack, new Binding(Key.L, Key.None, GamepadButtons.South, GamepadButtons.None) },
            { InputFlags.SpecialAttack, new Binding(Key.I, Key.None, GamepadButtons.East, GamepadButtons.None) },
            { InputFlags.Burst, new Binding(Key.O, Key.None, GamepadButtons.RightShoulder, GamepadButtons.None) },
            { InputFlags.Mania1, new Binding(Key.D, Key.None, GamepadButtons.LeftShoulder, GamepadButtons.None) },
            { InputFlags.Mania2, new Binding(Key.J, Key.None, GamepadButtons.RightShoulder, GamepadButtons.None) },
            { InputFlags.Mania3, new Binding(Key.S, Key.None, GamepadButtons.LeftTrigger, GamepadButtons.None) },
            { InputFlags.Mania4, new Binding(Key.K, Key.None, GamepadButtons.RightTrigger, GamepadButtons.None) },
            { InputFlags.Mania5, new Binding(Key.None, Key.None, GamepadButtons.None, GamepadButtons.None) },
            { InputFlags.Mania6, new Binding(Key.None, Key.None, GamepadButtons.None, GamepadButtons.None) },
            { InputFlags.Dash, new Binding(Key.LeftShift, Key.None, GamepadButtons.LeftStick, GamepadButtons.None) },
            { InputFlags.Grab, new Binding(Key.U, Key.None, GamepadButtons.LeftShoulder, GamepadButtons.None) },
        };

        public static EnumArray<InputFlags, Binding> DefaultBindings
        {
            get
            {
                EnumArray<InputFlags, Binding> controls = new EnumArray<InputFlags, Binding>();
                foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
                {
                    controls[flag] = _defaultBindings[flag];
                }
                return controls;
            }
        }

        public ControlsConfig()
        {
            OnEnable();
        }

        // Sets Default Bindings onEnable to avoid Null Primary bindings
        private void OnEnable()
        {
            _controlScheme ??= new EnumArray<InputFlags, Binding>();

            foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
            {
                if (_controlScheme[flag] == null)
                {
                    _controlScheme[flag] = _defaultBindings[flag];
                }
            }
        }
    }

    [Serializable]
    public class GamepadBindings
    {
        [SerializeField]
        private GamepadButtons _primaryButton;

        [SerializeField]
        private GamepadButtons _altButton;

        public void SetPrimaryButton(GamepadButtons primaryButton)
        {
            _primaryButton = primaryButton;
        }

        public void SetAltButton(GamepadButtons altButton)
        {
            _altButton = altButton;
        }

        public GamepadButtons GetPrimaryButton()
        {
            return _primaryButton;
        }

        public GamepadButtons GetAltButton()
        {
            return _altButton;
        }
    }

    [Serializable]
    public class KeyboardBindings
    {
        [SerializeField]
        private Key _primaryKey;

        [SerializeField]
        private Key _altKey;

        public void SetPrimaryKey(Key primaryKey)
        {
            _primaryKey = primaryKey;
        }

        public void SetAltKey(Key altKey)
        {
            _altKey = altKey;
        }

        public Key GetPrimaryKey()
        {
            return _primaryKey;
        }

        public Key GetAltKey()
        {
            return _altKey;
        }
    }

    [Serializable]
    public class Binding
    {
        [SerializeField]
        private KeyboardBindings _keyboardBindings = new KeyboardBindings();

        [SerializeField]
        private GamepadBindings _gamepadBindings = new GamepadBindings();

        /**
         * Base Binding Constructor
         *
         * Constructs a Binding Structure to store control binds
         *
         * @param primaryKey - Stores the primary keyboard key
         * @param altKey - Stores an alternate keyboard key
         * @param primaryButton - Stores the primary gamepad button
         * @param altButton - Stores an alternate gamepad button
         *
         */
        public Binding(Key primaryKey, Key altKey, GamepadButtons primaryButton, GamepadButtons altButton)
        {
            _keyboardBindings.SetPrimaryKey(primaryKey);
            _keyboardBindings.SetAltKey(altKey);
            _gamepadBindings.SetPrimaryButton(primaryButton);
            _gamepadBindings.SetAltButton(altButton);
        }

        // Getters to Return keys stored in Binding Structure
        public Key GetPrimaryKey()
        {
            return _keyboardBindings.GetPrimaryKey();
        }

        public Key GetAltKey()
        {
            return _keyboardBindings.GetAltKey();
        }

        public GamepadButtons GetPrimaryGamepadButton()
        {
            return _gamepadBindings.GetPrimaryButton();
        }

        public GamepadButtons GetAltGamepadButton()
        {
            return _gamepadBindings.GetAltButton();
        }
    }
}

public enum GamepadButtons
{
    None = 34,
    DpadUp = GamepadButton.DpadUp,
    DpadDown = GamepadButton.DpadDown,
    DpadLeft = GamepadButton.DpadLeft,
    DpadRight = GamepadButton.DpadRight,
    North = GamepadButton.North,
    East = GamepadButton.East,
    South = GamepadButton.South,
    West = GamepadButton.West,
    LeftStick = GamepadButton.LeftStick,
    RightStick = GamepadButton.RightStick,
    LeftShoulder = GamepadButton.LeftShoulder,
    RightShoulder = GamepadButton.RightShoulder,
    Start = GamepadButton.Start,
    Select = GamepadButton.Select,
    LeftTrigger = GamepadButton.LeftTrigger,
    RightTrigger = GamepadButton.RightTrigger,
}
