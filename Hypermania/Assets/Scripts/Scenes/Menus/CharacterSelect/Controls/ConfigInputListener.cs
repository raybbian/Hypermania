using Game.Sim;
using Game.View.Configs.Input;
using TMPro;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Display-only row for a single binding. <see cref="ConfigMenu"/> owns
    /// all navigation state and pushes updates via Bind/SetFocus.
    /// </summary>
    public class ConfigInputListener : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _titleText;

        [SerializeField]
        private TMP_Text _primaryText;

        [SerializeField]
        private TMP_Text _altText;

        [Tooltip("Shown when the primary slot is the focused cell.")]
        [SerializeField]
        private GameObject _primaryFocusIndicator;

        [Tooltip("Shown when the alternate slot is the focused cell.")]
        [SerializeField]
        private GameObject _altFocusIndicator;

        [SerializeField]
        private string _unboundLabel = "—";

        [SerializeField]
        private string _listeningLabel = "...";

        private InputFlags _flag = InputFlags.None;
        private ConfigDevice _device;
        private Binding _binding;

        public InputFlags Flag => _flag;

        public void Bind(InputFlags flag, ConfigDevice device, Binding binding)
        {
            _flag = flag;
            _device = device;
            _binding = binding;
            if (_titleText != null)
                _titleText.text = FormatFlag(flag);
            RefreshTexts();
        }

        public void SetFocus(bool focused, BindingSlot slot, bool listening)
        {
            if (_primaryFocusIndicator != null)
                _primaryFocusIndicator.SetActive(focused && slot == BindingSlot.Primary);
            if (_altFocusIndicator != null)
                _altFocusIndicator.SetActive(focused && slot == BindingSlot.Alt);

            RefreshTexts();
            if (focused && listening)
            {
                if (slot == BindingSlot.Primary && _primaryText != null)
                    _primaryText.text = _listeningLabel;
                else if (slot == BindingSlot.Alt && _altText != null)
                    _altText.text = _listeningLabel;
            }
        }

        private void RefreshTexts()
        {
            if (_primaryText != null)
                _primaryText.text = FormatPrimary();
            if (_altText != null)
                _altText.text = FormatAlt();
        }

        private string FormatPrimary()
        {
            if (_binding == null)
                return _unboundLabel;
            return _device == ConfigDevice.Keyboard
                ? FormatKey(_binding.GetPrimaryKey())
                : FormatGamepad(_binding.GetPrimaryGamepadButton());
        }

        private string FormatAlt()
        {
            if (_binding == null)
                return _unboundLabel;
            return _device == ConfigDevice.Keyboard
                ? FormatKey(_binding.GetAltKey())
                : FormatGamepad(_binding.GetAltGamepadButton());
        }

        private string FormatKey(UnityEngine.InputSystem.Key key)
        {
            return key == UnityEngine.InputSystem.Key.None ? _unboundLabel : key.ToString();
        }

        private string FormatGamepad(GamepadButtons btn)
        {
            return btn == GamepadButtons.None ? _unboundLabel : btn.ToString();
        }

        private static string FormatFlag(InputFlags flag)
        {
            return flag.ToString();
        }
    }
}
