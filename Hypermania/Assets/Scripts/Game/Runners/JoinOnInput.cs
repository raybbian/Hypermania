using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runners
{
    public class JoinOnInput : MonoBehaviour
    {
        private InputAction _anyButtonAction;
        public InputDevice P1_Input = null,
            P2_Input = null;

        private void OnEnable()
        {
            /* Search for Any Button Press */
            _anyButtonAction = new InputAction(binding: "/*/<button>");
            _anyButtonAction.performed += OnAnyButtonPressed;
            _anyButtonAction.Enable();
        }

        public void OnDisable()
        {
            _anyButtonAction.Disable();
            _anyButtonAction.performed -= OnAnyButtonPressed;
        }

        private void OnAnyButtonPressed(InputAction.CallbackContext context)
        {
            /* Stop Input Detection if Both Player Inputs are Handled*/
            if (P1_Input != null && P2_Input != null)
            {
                return;
            }
            /* Reject Unsupported Devices */
            if (context.control.device is not Gamepad && context.control.device is not Keyboard)
            {
                Debug.Log(context.control.device + " is not a supported device");
                return;
            }
            /* Reject if device is already connected */
            if (context.control.device == P1_Input || context.control.device == P2_Input)
            {
                Debug.Log(context.control.device + " has already been connected");
                return;
            }
            /* Prioritize Setting Player 1 Input First */
            if (P1_Input == null && !context.control.device.Equals(P2_Input))
            {
                P1_Input = context.control.device;
                Debug.Log("Player 1: " + P1_Input.name);
            }
            else if (P2_Input == null && !context.control.device.Equals(P1_Input))
            {
                P2_Input = context.control.device;
                Debug.Log("Player 2: " + P2_Input.name);
            }

            /* Search for New Device Input*/
            if (P1_Input == null || P2_Input == null)
            {
                _anyButtonAction = new InputAction(binding: "/*/<button>");
            }
        }

        /**
         * Getter to Return Player's Input Devuce
         *
         * @param index
         */
        public InputDevice GetPlayerInputDevice(int index)
        {
            if (index == 0)
            {
                return P1_Input;
            }
            if (index == 1)
            {
                return P2_Input;
            }
            return null;
        }
    }
}
