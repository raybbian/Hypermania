using System;
using Game.View.Configs.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Owns the listen-mode lifecycle: arm via
    /// <c>InputSystem.onAnyButtonPress.CallOnce</c>, filter by active tab and
    /// an owner-exclusion predicate, re-arm on rejected inputs, and fire
    /// <see cref="KeyboardBound"/> / <see cref="GamepadBound"/> /
    /// <see cref="ClearRequested"/> once the capture resolves. Escape is
    /// un-bindable; it clears the slot instead.
    /// </summary>
    public class BindingListener
    {
        public event Action<Key> KeyboardBound;
        public event Action<GamepadButtons> GamepadBound;
        public event Action ClearRequested;

        private IDisposable _subscription;
        private ConfigDevice _tab;
        private Func<InputDevice, bool> _deviceExcluded;

        public bool IsActive { get; private set; }

        public void Start(ConfigDevice tab, Func<InputDevice, bool> deviceExcluded)
        {
            _tab = tab;
            _deviceExcluded = deviceExcluded;
            IsActive = true;
            Arm();
        }

        public void Stop()
        {
            IsActive = false;
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Arm()
        {
            _subscription?.Dispose();
            _subscription = InputSystem.onAnyButtonPress.CallOnce(OnAnyButtonPressed);
        }

        private void OnAnyButtonPressed(InputControl ctrl)
        {
            if (!IsActive || ctrl == null)
                return;

            // Escape is un-bindable; clear the focused slot and exit listen.
            if (ctrl is KeyControl esc && esc.keyCode == Key.Escape)
            {
                Stop();
                ClearRequested?.Invoke();
                return;
            }

            // Skip inputs from a device currently navigating another menu.
            if (_deviceExcluded != null && _deviceExcluded(ctrl.device))
            {
                Arm();
                return;
            }

            if (_tab == ConfigDevice.Keyboard && ctrl.device is Keyboard && ctrl is KeyControl kc)
            {
                Stop();
                KeyboardBound?.Invoke(kc.keyCode);
                return;
            }

            if (_tab == ConfigDevice.Gamepad && ctrl.device is Gamepad gp)
            {
                GamepadButtons btn = GamepadButtonFromControl(gp, ctrl);
                if (btn != GamepadButtons.None)
                {
                    Stop();
                    GamepadBound?.Invoke(btn);
                    return;
                }
            }

            Arm();
        }

        // Reference comparison against each gamepad control survives
        // layout/locale variations that would break name-based matches.
        private static GamepadButtons GamepadButtonFromControl(Gamepad gp, InputControl ctrl)
        {
            if (ctrl == gp.buttonSouth)
                return GamepadButtons.South;
            if (ctrl == gp.buttonNorth)
                return GamepadButtons.North;
            if (ctrl == gp.buttonEast)
                return GamepadButtons.East;
            if (ctrl == gp.buttonWest)
                return GamepadButtons.West;
            if (ctrl == gp.leftShoulder)
                return GamepadButtons.LeftShoulder;
            if (ctrl == gp.rightShoulder)
                return GamepadButtons.RightShoulder;
            if (ctrl == gp.leftTrigger)
                return GamepadButtons.LeftTrigger;
            if (ctrl == gp.rightTrigger)
                return GamepadButtons.RightTrigger;
            if (ctrl == gp.leftStickButton)
                return GamepadButtons.LeftStick;
            if (ctrl == gp.rightStickButton)
                return GamepadButtons.RightStick;
            if (ctrl == gp.startButton)
                return GamepadButtons.Start;
            if (ctrl == gp.selectButton)
                return GamepadButtons.Select;
            if (ctrl == gp.dpad.up)
                return GamepadButtons.DpadUp;
            if (ctrl == gp.dpad.down)
                return GamepadButtons.DpadDown;
            if (ctrl == gp.dpad.left)
                return GamepadButtons.DpadLeft;
            if (ctrl == gp.dpad.right)
                return GamepadButtons.DpadRight;
            return GamepadButtons.None;
        }
    }
}
