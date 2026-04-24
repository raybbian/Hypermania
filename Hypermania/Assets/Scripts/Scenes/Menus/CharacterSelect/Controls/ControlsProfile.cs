using System;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils.EnumArray;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// User-owned, named controls profile persisted as JSON by
    /// <see cref="ControlsProfileStore"/>. Matches the EnumArray shape used
    /// by <see cref="ControlsConfig"/> so sim/view code can consume either.
    /// </summary>
    [Serializable]
    public class ControlsProfile
    {
        [SerializeField]
        private string _name;

        [SerializeField]
        private EnumArray<InputFlags, Binding> _bindings;

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public EnumArray<InputFlags, Binding> Bindings => _bindings ??= new EnumArray<InputFlags, Binding>();

        public ControlsProfile() { }

        public ControlsProfile(string name, EnumArray<InputFlags, Binding> bindings)
        {
            _name = name;
            _bindings = bindings;
        }

        public static ControlsProfile CreateWithDefaults(string name)
        {
            return new ControlsProfile(name, ControlsConfig.DefaultBindings);
        }

        // Binding has no setters for its composite halves, so every mutator
        // rebuilds the Binding from scratch while preserving the other half.
        public void SetKeyboardPrimary(InputFlags flag, Key key)
        {
            Binding existing = Bindings[flag];
            Key alt = existing?.GetAltKey() ?? Key.None;
            GamepadButtons gpPrimary = existing?.GetPrimaryGamepadButton() ?? GamepadButtons.None;
            GamepadButtons gpAlt = existing?.GetAltGamepadButton() ?? GamepadButtons.None;
            Bindings[flag] = new Binding(key, alt, gpPrimary, gpAlt);
        }

        public void SetKeyboardAlt(InputFlags flag, Key key)
        {
            Binding existing = Bindings[flag];
            Key primary = existing?.GetPrimaryKey() ?? Key.None;
            GamepadButtons gpPrimary = existing?.GetPrimaryGamepadButton() ?? GamepadButtons.None;
            GamepadButtons gpAlt = existing?.GetAltGamepadButton() ?? GamepadButtons.None;
            Bindings[flag] = new Binding(primary, key, gpPrimary, gpAlt);
        }

        public void SetGamepadPrimary(InputFlags flag, GamepadButtons button)
        {
            Binding existing = Bindings[flag];
            Key primary = existing?.GetPrimaryKey() ?? Key.None;
            Key alt = existing?.GetAltKey() ?? Key.None;
            GamepadButtons gpAlt = existing?.GetAltGamepadButton() ?? GamepadButtons.None;
            Bindings[flag] = new Binding(primary, alt, button, gpAlt);
        }

        public void SetGamepadAlt(InputFlags flag, GamepadButtons button)
        {
            Binding existing = Bindings[flag];
            Key primary = existing?.GetPrimaryKey() ?? Key.None;
            Key alt = existing?.GetAltKey() ?? Key.None;
            GamepadButtons gpPrimary = existing?.GetPrimaryGamepadButton() ?? GamepadButtons.None;
            Bindings[flag] = new Binding(primary, alt, gpPrimary, button);
        }
    }
}
