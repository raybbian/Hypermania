using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Scenes.Menus.CharacterSelect.Controls
{
    public enum ConfigMenuColumn
    {
        ProfileList,
        TabRow,
        BindingList,
    }

    public enum ConfigDevice
    {
        Keyboard,
        Gamepad,
    }

    public enum BindingSlot
    {
        Primary,
        Alt,
    }

    /// <summary>
    /// Controls-configuration overlay. Opened with the <see cref="InputDevice"/>
    /// that triggered the open; only that device drives navigation, but bind
    /// capture accepts any device matching the active tab. Two half-screen
    /// instances can run concurrently without stealing each other's inputs —
    /// see <see cref="_liveMenus"/> / <see cref="IsDeviceOwnedByOtherMenu"/>.
    /// </summary>
    public class ConfigMenu : MonoBehaviour
    {
        private const float AxisThreshold = 0.75f;

        // Used during bind capture to skip inputs from a device currently
        // navigating another open menu.
        private static readonly HashSet<ConfigMenu> _liveMenus = new HashSet<ConfigMenu>();

        [Tooltip("Toggled by Open/Close. Falls back to this.gameObject when null.")]
        [SerializeField]
        private GameObject _contentRoot;

        [SerializeField]
        private ConfigProfileList _profileList;

        [SerializeField]
        private ConfigDeviceTab _deviceTab;

        [SerializeField]
        private ConfigInputMenu _inputMenu;

        private InputDevice _ownerDevice;
        private InputDevice _recentOwnerDevice;
        private int _recentOwnerFrame = -1;
        private bool _isOpen;

        private List<ControlsProfile> _profiles = new List<ControlsProfile>();
        private int _profileIndex;

        private ConfigMenuColumn _column;
        private ConfigDevice _device;
        private int _bindingRow;
        private BindingSlot _bindingSlot;

        private bool _listening;
        private IDisposable _listenSubscription;

        // Swallow the rest of the frame's edges after (a) listen mode ends,
        // so the bound key doesn't double-fire as nav, and (b) Open(), so the
        // L/R press that opened the menu doesn't immediately jump the cursor
        // off the profile list.
        private int _suppressEdgesThroughFrame = -1;

        private bool _prevLeft;
        private bool _prevRight;
        private bool _prevUp;
        private bool _prevDown;

        public event Action Closed;

        public bool IsOpen => _isOpen;

        public ControlsProfile ActiveProfile =>
            _profileIndex >= 0 && _profileIndex < _profiles.Count ? _profiles[_profileIndex] : null;

        public string ActiveProfileName => ActiveProfile?.Name;

        // Stays true for one frame after Close so the close-triggering edge
        // doesn't leak into the character-select controller.
        public bool OwnsDevice(InputDevice device)
        {
            if (device == null)
                return false;
            if (_isOpen && device == _ownerDevice)
                return true;
            return device == _recentOwnerDevice && Time.frameCount <= _recentOwnerFrame;
        }

        public void Open(InputDevice device, string preferredProfileName = null)
        {
            if (_isOpen || device == null)
                return;
            _ownerDevice = device;
            _isOpen = true;
            SetContentActive(true);

            _profiles = ControlsProfileStore.LoadAll();
            _profileIndex = FindProfileIndex(preferredProfileName);
            _column = ConfigMenuColumn.ProfileList;
            _device = DefaultTabFor(device);
            _bindingRow = 0;
            _bindingSlot = BindingSlot.Primary;
            StopListening();
            InitPrevAxesFromOwner();
            _suppressEdgesThroughFrame = Time.frameCount;

            RefreshAll();
        }

        // Seed _prev stick state from the owner's live axis values so a stick
        // still tilted from the L/R press that opened the menu doesn't get
        // re-read as a fresh edge by the next PollGamepad.
        private void InitPrevAxesFromOwner()
        {
            if (_ownerDevice is Gamepad gp)
            {
                _prevLeft = gp.leftStick.x.value < -AxisThreshold;
                _prevRight = gp.leftStick.x.value > AxisThreshold;
                _prevUp = gp.leftStick.y.value > AxisThreshold;
                _prevDown = gp.leftStick.y.value < -AxisThreshold;
            }
            else
            {
                _prevLeft = _prevRight = _prevUp = _prevDown = false;
            }
        }

        private int FindProfileIndex(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i] != null
                    && string.Equals(_profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        public void Close()
        {
            if (!_isOpen)
                return;
            StopListening();
            _isOpen = false;
            _recentOwnerDevice = _ownerDevice;
            _recentOwnerFrame = Time.frameCount;
            _ownerDevice = null;
            SetContentActive(false);
            Closed?.Invoke();
        }

        private void OnEnable()
        {
            _liveMenus.Add(this);
        }

        private void OnDisable()
        {
            _liveMenus.Remove(this);
            StopListening();
        }

        private void Update()
        {
            if (!_isOpen || _ownerDevice == null)
                return;
            if (_listening)
                return;
            if (Time.frameCount <= _suppressEdgesThroughFrame)
                return;

            PollOwnerEdges(out bool left, out bool right, out bool up, out bool down, out bool confirm, out bool back);
            if (!(left || right || up || down || confirm || back))
                return;

            switch (_column)
            {
                case ConfigMenuColumn.ProfileList:
                    HandleProfileList(left, right, up, down, confirm, back);
                    break;
                case ConfigMenuColumn.TabRow:
                    HandleTabRow(left, right, up, down, confirm, back);
                    break;
                case ConfigMenuColumn.BindingList:
                    HandleBindingList(left, right, up, down, confirm, back);
                    break;
            }

            RefreshAll();
        }

        private void HandleProfileList(bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            int total = _profileList != null ? _profileList.RowCount(_profiles) : _profiles.Count + 1;
            if (total <= 0)
                return;

            if (up)
                _profileIndex = (_profileIndex - 1 + total) % total;
            else if (down)
                _profileIndex = (_profileIndex + 1) % total;

            if (right && ActiveProfile != null)
            {
                _column = ConfigMenuColumn.TabRow;
                return;
            }

            if (confirm)
            {
                if (IsNewDummySelected)
                {
                    CreateNewProfile();
                }
                else if (ActiveProfile != null)
                {
                    _column = ConfigMenuColumn.TabRow;
                }
                return;
            }

            if (back)
            {
                Close();
            }
        }

        private void HandleTabRow(bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            if (left || right)
            {
                _device = _device == ConfigDevice.Keyboard ? ConfigDevice.Gamepad : ConfigDevice.Keyboard;
                return;
            }
            if (down)
            {
                _column = ConfigMenuColumn.BindingList;
                if (_inputMenu != null)
                    _bindingRow = Mathf.Clamp(_bindingRow, 0, Mathf.Max(0, _inputMenu.RowCount - 1));
                return;
            }
            if (back)
            {
                _column = ConfigMenuColumn.ProfileList;
            }
        }

        private void HandleBindingList(bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            int rowCount = _inputMenu != null ? _inputMenu.RowCount : 0;

            if (left || right)
            {
                _bindingSlot = _bindingSlot == BindingSlot.Primary ? BindingSlot.Alt : BindingSlot.Primary;
                return;
            }
            if (up)
            {
                if (_bindingRow <= 0)
                {
                    _column = ConfigMenuColumn.TabRow;
                }
                else
                {
                    _bindingRow--;
                }
                return;
            }
            if (down)
            {
                if (rowCount > 0 && _bindingRow < rowCount - 1)
                    _bindingRow++;
                return;
            }
            if (confirm)
            {
                if (ActiveProfile != null && _inputMenu != null && rowCount > 0)
                    StartListening();
                return;
            }
            if (back)
            {
                _column = ConfigMenuColumn.ProfileList;
            }
        }

        private bool IsNewDummySelected => _profileIndex == _profiles.Count;

        private void CreateNewProfile()
        {
            // Reload so a concurrent "+ New Profile" in the other menu is
            // visible to SuggestName and the two players don't collide.
            _profiles = ControlsProfileStore.LoadAll();
            string name = ControlsProfileStore.SuggestName(_profiles);
            ControlsProfile profile = ControlsProfile.CreateWithDefaults(name);
            _profiles.Add(profile);
            _profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _profileIndex = _profiles.IndexOf(profile);
            ControlsProfileStore.Save(profile);
        }

        private void StartListening()
        {
            if (_listening)
                return;
            _listening = true;
            ArmListenOnce();
        }

        private void ArmListenOnce()
        {
            _listenSubscription?.Dispose();
            _listenSubscription = InputSystem.onAnyButtonPress.CallOnce(OnAnyButtonPressed);
        }

        private void StopListening()
        {
            if (_listening)
            {
                _suppressEdgesThroughFrame = Time.frameCount;
                _listening = false;
            }
            if (_listenSubscription != null)
            {
                _listenSubscription.Dispose();
                _listenSubscription = null;
            }
        }

        private void OnAnyButtonPressed(InputControl ctrl)
        {
            if (!_isOpen || !_listening || ctrl == null)
                return;

            // Escape is un-bindable; instead it clears the focused slot.
            if (ctrl is KeyControl esc && esc.keyCode == Key.Escape)
            {
                ClearCurrentSlot();
                StopListening();
                RefreshAll();
                return;
            }

            bool accepted = false;
            if (IsDeviceOwnedByOtherMenu(ctrl.device))
            {
                // re-arm
            }
            else if (_device == ConfigDevice.Keyboard)
            {
                if (ctrl.device is Keyboard && ctrl is KeyControl kc)
                {
                    ApplyKeyboardBinding(kc.keyCode);
                    accepted = true;
                }
            }
            else
            {
                if (ctrl.device is Gamepad gp)
                {
                    GamepadButtons btn = GamepadButtonFromControl(gp, ctrl);
                    if (btn != GamepadButtons.None)
                    {
                        ApplyGamepadBinding(btn);
                        accepted = true;
                    }
                }
            }

            if (accepted)
            {
                StopListening();
                RefreshAll();
            }
            else
            {
                ArmListenOnce();
            }
        }

        private void ClearCurrentSlot()
        {
            ControlsProfile profile = ActiveProfile;
            if (profile == null || _inputMenu == null)
                return;
            InputFlags flag = _inputMenu.FlagAt(_bindingRow);
            if (flag == InputFlags.None)
                return;
            if (_device == ConfigDevice.Keyboard)
            {
                if (_bindingSlot == BindingSlot.Primary)
                    profile.SetKeyboardPrimary(flag, Key.None);
                else
                    profile.SetKeyboardAlt(flag, Key.None);
            }
            else
            {
                if (_bindingSlot == BindingSlot.Primary)
                    profile.SetGamepadPrimary(flag, GamepadButtons.None);
                else
                    profile.SetGamepadAlt(flag, GamepadButtons.None);
            }
            ControlsProfileStore.Save(profile);
        }

        private bool IsDeviceOwnedByOtherMenu(InputDevice device)
        {
            if (device == null)
                return false;
            foreach (ConfigMenu other in _liveMenus)
            {
                if (other == this || other == null)
                    continue;
                if (other._isOpen && other._ownerDevice == device)
                    return true;
            }
            return false;
        }

        private void ApplyKeyboardBinding(Key key)
        {
            ControlsProfile profile = ActiveProfile;
            if (profile == null || _inputMenu == null)
                return;
            InputFlags flag = _inputMenu.FlagAt(_bindingRow);
            if (flag == InputFlags.None)
                return;
            if (_bindingSlot == BindingSlot.Primary)
                profile.SetKeyboardPrimary(flag, key);
            else
                profile.SetKeyboardAlt(flag, key);
            ControlsProfileStore.Save(profile);
        }

        private void ApplyGamepadBinding(GamepadButtons button)
        {
            ControlsProfile profile = ActiveProfile;
            if (profile == null || _inputMenu == null)
                return;
            InputFlags flag = _inputMenu.FlagAt(_bindingRow);
            if (flag == InputFlags.None)
                return;
            if (_bindingSlot == BindingSlot.Primary)
                profile.SetGamepadPrimary(flag, button);
            else
                profile.SetGamepadAlt(flag, button);
            ControlsProfileStore.Save(profile);
        }

        // Reference comparison survives layout/locale variations that would
        // break name-based matches.
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

        private static ConfigDevice DefaultTabFor(InputDevice device)
        {
            return device is Gamepad ? ConfigDevice.Gamepad : ConfigDevice.Keyboard;
        }

        private void RefreshAll()
        {
            if (_profileList != null)
                // focused=true always — the modified profile stays highlighted
                // even when the cursor moves off to the tab/binding column.
                _profileList.Refresh(_profiles, _profileIndex, focused: true);
            if (_deviceTab != null)
                _deviceTab.SetDisplay(_device, _column == ConfigMenuColumn.TabRow);
            if (_inputMenu != null)
            {
                _inputMenu.Refresh(ActiveProfile, _device);
                _inputMenu.SetFocus(
                    _column == ConfigMenuColumn.BindingList ? _bindingRow : -1,
                    _bindingSlot,
                    _listening
                );
            }
        }

        private void SetContentActive(bool active)
        {
            GameObject target = _contentRoot != null ? _contentRoot : gameObject;
            if (target.activeSelf != active)
                target.SetActive(active);
        }

        private void PollOwnerEdges(
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            left = right = up = down = confirm = back = false;
            switch (_ownerDevice)
            {
                case Gamepad gp:
                    PollGamepad(gp, out left, out right, out up, out down, out confirm, out back);
                    break;
                case Keyboard kb:
                    PollKeyboard(kb, out left, out right, out up, out down, out confirm, out back);
                    break;
            }
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

            _prevLeft = _prevRight = _prevUp = _prevDown = false;
        }
    }
}
