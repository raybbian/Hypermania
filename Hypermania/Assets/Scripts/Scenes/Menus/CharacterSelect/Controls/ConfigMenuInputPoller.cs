using UnityEngine.InputSystem;

namespace Scenes.Menus.CharacterSelect.Controls
{
    // EdgeSet lives in the parent Scenes.Menus.CharacterSelect namespace — we
    // reference it directly below via the unqualified name.
    /// <summary>
    /// Device edge-poller tailored for the controls menu. Differs from
    /// <see cref="LocalSelectionController.PollGamepad"/> by deliberately NOT
    /// treating shoulders/triggers as L/R — in this menu the player may be
    /// testing those as binds, and they shouldn't also drive navigation.
    /// Constructor seeds stick-edge state from the device's current axis
    /// values so a stick still tilted from the press that opened the menu
    /// doesn't re-fire as a phantom edge on the next Poll.
    /// </summary>
    public class ConfigMenuInputPoller
    {
        private const float AxisThreshold = 0.75f;

        private readonly InputDevice _device;
        private bool _prevLeft;
        private bool _prevRight;
        private bool _prevUp;
        private bool _prevDown;

        public ConfigMenuInputPoller(InputDevice device)
        {
            _device = device;
            if (device is Gamepad gp)
            {
                _prevLeft = gp.leftStick.x.value < -AxisThreshold;
                _prevRight = gp.leftStick.x.value > AxisThreshold;
                _prevUp = gp.leftStick.y.value > AxisThreshold;
                _prevDown = gp.leftStick.y.value < -AxisThreshold;
            }
        }

        public EdgeSet Poll()
        {
            bool left = false,
                right = false,
                up = false,
                down = false,
                confirm = false,
                back = false;
            switch (_device)
            {
                case Gamepad gp:
                    PollGamepad(gp, out left, out right, out up, out down, out confirm, out back);
                    break;
                case Keyboard kb:
                    PollKeyboard(kb, out left, out right, out up, out down, out confirm, out back);
                    break;
            }
            return new EdgeSet(left, right, up, down, confirm, back);
        }

        private void PollGamepad(
            Gamepad gp,
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            bool stickLeft = gp.leftStick.x.value < -AxisThreshold;
            bool stickRight = gp.leftStick.x.value > AxisThreshold;
            bool stickUp = gp.leftStick.y.value > AxisThreshold;
            bool stickDown = gp.leftStick.y.value < -AxisThreshold;

            left = gp.dpad.left.wasPressedThisFrame || (stickLeft && !_prevLeft);
            right = gp.dpad.right.wasPressedThisFrame || (stickRight && !_prevRight);
            up = gp.dpad.up.wasPressedThisFrame || (stickUp && !_prevUp);
            down = gp.dpad.down.wasPressedThisFrame || (stickDown && !_prevDown);

            _prevLeft = stickLeft;
            _prevRight = stickRight;
            _prevUp = stickUp;
            _prevDown = stickDown;

            confirm = gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame;
            back = gp.buttonEast.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame;
        }

        private void PollKeyboard(
            Keyboard kb,
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            left = kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
            right = kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
            up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            confirm = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            back = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
        }
    }
}
