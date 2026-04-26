using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Keyboard/controller tab at the top of the right panel. L/R while
    /// focused toggles which half of the binding table is being edited.
    /// </summary>
    public class ConfigDeviceTab : MonoBehaviour
    {
        [Tooltip("Shown while the keyboard tab is active.")]
        [SerializeField]
        private GameObject _keyboardActiveIndicator;

        [Tooltip("Shown while the controller tab is active.")]
        [SerializeField]
        private GameObject _gamepadActiveIndicator;

        [Tooltip("Shown while the tab row itself has nav focus.")]
        [SerializeField]
        private GameObject _focusIndicator;

        public void SetDisplay(ConfigDevice device, bool focused)
        {
            if (_keyboardActiveIndicator != null)
                _keyboardActiveIndicator.SetActive(device == ConfigDevice.Keyboard);
            if (_gamepadActiveIndicator != null)
                _gamepadActiveIndicator.SetActive(device == ConfigDevice.Gamepad);
            if (_focusIndicator != null)
                _focusIndicator.SetActive(focused);
        }
    }
}
