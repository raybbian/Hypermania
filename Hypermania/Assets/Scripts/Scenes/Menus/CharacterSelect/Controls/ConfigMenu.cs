using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Hypermania.Game;
using Hypermania.Shared;

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
    /// Controls-configuration overlay. Orchestrates a <see cref="ConfigMenuInputPoller"/>,
    /// a <see cref="BindingListener"/>, and three view components against a
    /// <see cref="ControlsMenuSession"/> injected by <c>CharacterSelectDirectory</c>.
    /// Cross-menu sync happens via the session's <c>Changed</c> event —
    /// menu A mutating its cursor re-renders menu B's display the same frame.
    /// </summary>
    public class ConfigMenu : MonoBehaviour
    {
        [Tooltip("Toggled by Open/Close. Falls back to this.gameObject when null.")]
        [SerializeField]
        private GameObject _contentRoot;

        [SerializeField]
        private ConfigProfileList _profileList;

        [SerializeField]
        private ConfigDeviceTab _deviceTab;

        [SerializeField]
        private ConfigInputMenu _inputMenu;

        private ControlsMenuSession _session;
        private bool _attached;
        private ConfigMenuInputPoller _poller;
        private BindingListener _listener;

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

        // Swallows the rest of the frame's edges after (a) listen mode ends
        // so the bound key doesn't double-fire as nav, and (b) Open() so the
        // L/R press that opened the menu doesn't immediately jump the cursor.
        private int _suppressEdgesThroughFrame = -1;

        public event Action Closed;

        public bool IsOpen => _isOpen;
        public InputDevice OwnerDevice => _ownerDevice;
        public ControlsProfile ActiveProfile =>
            _profileIndex >= 0 && _profileIndex < _profiles.Count ? _profiles[_profileIndex] : null;
        public string ActiveProfileName => ActiveProfile?.Name;

        /// <summary>
        /// Stays true for one frame after Close so the close-triggering edge
        /// doesn't leak into the character-select controller.
        /// </summary>
        public bool OwnsDevice(InputDevice device)
        {
            if (device == null)
                return false;
            if (_isOpen && device == _ownerDevice)
                return true;
            return device == _recentOwnerDevice && Time.frameCount <= _recentOwnerFrame;
        }

        public void SetSession(ControlsMenuSession session)
        {
            if (_session == session)
                return;
            DetachFromSession();
            _session = session;
            AttachToSession();
        }

        // Idempotent: guards against OnEnable firing after SetSession has
        // already attached, which would double-subscribe RefreshViews.
        private void AttachToSession()
        {
            if (_session == null || _attached)
                return;
            _session.Register(this);
            _session.Changed += RefreshViews;
            _attached = true;
        }

        private void DetachFromSession()
        {
            if (_session == null || !_attached)
                return;
            _session.Changed -= RefreshViews;
            _session.Unregister(this);
            _attached = false;
        }

        public void Open(InputDevice device, string preferredProfileName = null)
        {
            if (_isOpen || device == null)
                return;
            _ownerDevice = device;
            _isOpen = true;
            SetContentActive(true);

            _profiles = ControlsProfileStore.LoadAll();
            _profileIndex = FindInitialProfileIndex(preferredProfileName);
            _column = ConfigMenuColumn.ProfileList;
            _device = device is Gamepad ? ConfigDevice.Gamepad : ConfigDevice.Keyboard;
            _bindingRow = 0;
            _bindingSlot = BindingSlot.Primary;

            _poller = new ConfigMenuInputPoller(device);
            _listener = new BindingListener();
            _listener.KeyboardBound += OnKeyboardBound;
            _listener.GamepadBound += OnGamepadBound;
            _listener.ClearRequested += OnClearRequested;

            _suppressEdgesThroughFrame = Time.frameCount;

            NotifySessionChanged();
        }

        public void Close()
        {
            if (!_isOpen)
                return;
            StopListeningAndSuppress();
            DetachListener();
            _poller = null;
            _isOpen = false;
            _recentOwnerDevice = _ownerDevice;
            _recentOwnerFrame = Time.frameCount;
            _ownerDevice = null;
            SetContentActive(false);
            Closed?.Invoke();
            NotifySessionChanged();
        }

        private void OnEnable()
        {
            // Re-attach in case the gameObject was toggled off/on; session
            // itself is set once by the directory and survives the cycle.
            AttachToSession();
        }

        private void OnDisable()
        {
            StopListeningAndSuppress();
            DetachListener();
            DetachFromSession();
        }

        private void Update()
        {
            if (!_isOpen || _ownerDevice == null)
                return;
            if (_listener != null && _listener.IsActive)
                return;
            if (Time.frameCount <= _suppressEdgesThroughFrame)
                return;

            EdgeSet edges = _poller.Poll();
            if (!(edges.Left || edges.Right || edges.Up || edges.Down || edges.Confirm || edges.Back))
                return;

            switch (_column)
            {
                case ConfigMenuColumn.ProfileList:
                    HandleProfileList(edges);
                    break;
                case ConfigMenuColumn.TabRow:
                    HandleTabRow(edges);
                    break;
                case ConfigMenuColumn.BindingList:
                    HandleBindingList(edges);
                    break;
            }

            NotifySessionChanged();
        }

        private void HandleProfileList(in EdgeSet edges)
        {
            int total = _profiles.Count + 1; // +1 for the "+ New Profile" dummy
            if (edges.Up)
                _profileIndex = StepProfileIndex(total, -1);
            else if (edges.Down)
                _profileIndex = StepProfileIndex(total, 1);

            if (edges.Right && ActiveProfile != null)
            {
                _column = ConfigMenuColumn.TabRow;
                return;
            }

            if (edges.Confirm)
            {
                if (IsNewDummySelected)
                    CreateNewProfile();
                else if (ActiveProfile != null)
                    _column = ConfigMenuColumn.TabRow;
                return;
            }

            if (edges.Back)
                Close();
        }

        private void HandleTabRow(in EdgeSet edges)
        {
            if (edges.Left || edges.Right)
            {
                _device = _device == ConfigDevice.Keyboard ? ConfigDevice.Gamepad : ConfigDevice.Keyboard;
                return;
            }
            if (edges.Down)
            {
                _column = ConfigMenuColumn.BindingList;
                if (_inputMenu != null)
                    _bindingRow = Mathf.Clamp(_bindingRow, 0, Mathf.Max(0, _inputMenu.RowCount - 1));
                return;
            }
            if (edges.Back)
                _column = ConfigMenuColumn.ProfileList;
        }

        private void HandleBindingList(in EdgeSet edges)
        {
            int rowCount = _inputMenu != null ? _inputMenu.RowCount : 0;

            if (edges.Left || edges.Right)
            {
                _bindingSlot = _bindingSlot == BindingSlot.Primary ? BindingSlot.Alt : BindingSlot.Primary;
                return;
            }
            if (edges.Up)
            {
                if (_bindingRow <= 0)
                    _column = ConfigMenuColumn.TabRow;
                else
                    _bindingRow--;
                return;
            }
            if (edges.Down)
            {
                if (rowCount > 0 && _bindingRow < rowCount - 1)
                    _bindingRow++;
                return;
            }
            if (edges.Confirm)
            {
                if (ActiveProfile != null && _inputMenu != null && rowCount > 0)
                    StartListening();
                return;
            }
            if (edges.Back)
                _column = ConfigMenuColumn.ProfileList;
        }

        private bool IsNewDummySelected => _profileIndex == _profiles.Count;

        // Preferred profile wins if it exists and isn't claimed; otherwise the
        // first free profile, else the "+ New Profile" dummy.
        private int FindInitialProfileIndex(string preferred)
        {
            if (!string.IsNullOrEmpty(preferred))
            {
                for (int i = 0; i < _profiles.Count; i++)
                {
                    if (
                        _profiles[i] != null
                        && string.Equals(_profiles[i].Name, preferred, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return IsProfileTaken(_profiles[i].Name) ? FirstFreeProfileIndex() : i;
                    }
                }
            }
            return FirstFreeProfileIndex();
        }

        private int FirstFreeProfileIndex()
        {
            for (int i = 0; i < _profiles.Count; i++)
            {
                string n = _profiles[i]?.Name;
                if (!string.IsNullOrEmpty(n) && !IsProfileTaken(n))
                    return i;
            }
            return _profiles.Count;
        }

        // Dummy row is always selectable; real rows claimed by another open
        // menu are skipped.
        private int StepProfileIndex(int total, int dir)
        {
            for (int step = 1; step <= total; step++)
            {
                int candidate = ((_profileIndex + dir * step) % total + total) % total;
                if (candidate == _profiles.Count)
                    return candidate;
                string n = _profiles[candidate]?.Name;
                if (!string.IsNullOrEmpty(n) && !IsProfileTaken(n))
                    return candidate;
            }
            return _profileIndex;
        }

        private void CreateNewProfile()
        {
            // Reload first so a concurrent create in the other menu is visible
            // to SuggestName; otherwise both players would pick the same fruit.
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
            if (_listener == null || _listener.IsActive)
                return;
            _listener.Start(_device, IsDeviceExcludedFromCapture);
        }

        private void StopListeningAndSuppress()
        {
            if (_listener != null && _listener.IsActive)
            {
                _listener.Stop();
                _suppressEdgesThroughFrame = Time.frameCount;
            }
        }

        private void DetachListener()
        {
            if (_listener == null)
                return;
            _listener.KeyboardBound -= OnKeyboardBound;
            _listener.GamepadBound -= OnGamepadBound;
            _listener.ClearRequested -= OnClearRequested;
            _listener = null;
        }

        private bool IsDeviceExcludedFromCapture(InputDevice device)
        {
            return _session != null && _session.IsDeviceOwnedElsewhere(this, device);
        }

        private void OnKeyboardBound(Key key)
        {
            WriteBinding(profile =>
            {
                InputFlags flag = _inputMenu.FlagAt(_bindingRow);
                if (flag == InputFlags.None)
                    return;
                if (_bindingSlot == BindingSlot.Primary)
                    profile.SetKeyboardPrimary(flag, key);
                else
                    profile.SetKeyboardAlt(flag, key);
            });
        }

        private void OnGamepadBound(GamepadButtons button)
        {
            WriteBinding(profile =>
            {
                InputFlags flag = _inputMenu.FlagAt(_bindingRow);
                if (flag == InputFlags.None)
                    return;
                if (_bindingSlot == BindingSlot.Primary)
                    profile.SetGamepadPrimary(flag, button);
                else
                    profile.SetGamepadAlt(flag, button);
            });
        }

        private void OnClearRequested()
        {
            WriteBinding(profile =>
            {
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
            });
        }

        private void WriteBinding(Action<ControlsProfile> mutate)
        {
            ControlsProfile profile = ActiveProfile;
            if (profile == null || _inputMenu == null)
                return;
            mutate(profile);
            ControlsProfileStore.Save(profile);
            _suppressEdgesThroughFrame = Time.frameCount;
            NotifySessionChanged();
        }

        private bool IsProfileTaken(string name) => _session != null && _session.IsProfileTaken(this, name);

        private void NotifySessionChanged()
        {
            if (_session != null)
                _session.NotifyChanged();
            else
                RefreshViews();
        }

        private void RefreshViews()
        {
            if (_profileList != null)
                // focused=true always — the profile being modified stays
                // highlighted even when the cursor moves off to the right.
                _profileList.Refresh(_profiles, _profileIndex, focused: true, IsProfileTaken);
            if (_deviceTab != null)
                _deviceTab.SetDisplay(_device, _column == ConfigMenuColumn.TabRow);
            if (_inputMenu != null)
            {
                _inputMenu.Refresh(ActiveProfile, _device);
                _inputMenu.SetFocus(
                    _column == ConfigMenuColumn.BindingList ? _bindingRow : -1,
                    _bindingSlot,
                    _listener != null && _listener.IsActive
                );
            }
        }

        private void SetContentActive(bool active)
        {
            GameObject target = _contentRoot != null ? _contentRoot : gameObject;
            if (target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
