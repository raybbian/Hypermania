using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Shared coordination object for the two on-screen <see cref="ConfigMenu"/>
    /// instances. Owned by <c>CharacterSelectDirectory</c> — not a singleton —
    /// so lifecycle ties to scene load/unload rather than domain reload. The
    /// one <see cref="Changed"/> event fans out to every registered menu on
    /// any cursor/profile/listen mutation, which is how one menu's display
    /// auto-updates when the other moves.
    /// </summary>
    public class ControlsMenuSession
    {
        private readonly List<ConfigMenu> _menus = new List<ConfigMenu>();
        private readonly Func<ConfigMenu, string, bool> _externalClaimCheck;

        public event Action Changed;

        /// <param name="externalClaimCheck">
        /// Optional predicate that returns true when <paramref name="name"/>
        /// is claimed by something outside the live-menu set — typically the
        /// other player's <c>PlayerSelectionState.ControlsProfileName</c>.
        /// Keeps a profile locked even after its owner closes their menu.
        /// </param>
        public ControlsMenuSession(Func<ConfigMenu, string, bool> externalClaimCheck = null)
        {
            _externalClaimCheck = externalClaimCheck;
        }

        public void Register(ConfigMenu menu)
        {
            if (menu == null || _menus.Contains(menu))
                return;
            _menus.Add(menu);
        }

        public void Unregister(ConfigMenu menu)
        {
            _menus.Remove(menu);
        }

        public bool IsProfileTaken(ConfigMenu asker, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            for (int i = 0; i < _menus.Count; i++)
            {
                ConfigMenu other = _menus[i];
                if (other == null || other == asker || !other.IsOpen)
                    continue;
                if (string.Equals(other.ActiveProfileName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return _externalClaimCheck != null && _externalClaimCheck(asker, name);
        }

        public bool IsDeviceOwnedElsewhere(ConfigMenu asker, InputDevice device)
        {
            if (device == null)
                return false;
            for (int i = 0; i < _menus.Count; i++)
            {
                ConfigMenu other = _menus[i];
                if (other == null || other == asker || !other.IsOpen)
                    continue;
                if (other.OwnerDevice == device)
                    return true;
            }
            return false;
        }

        // Snapshot subscribers before invoking so a handler that calls
        // Register/Unregister doesn't mutate the list under iteration.
        public void NotifyChanged()
        {
            Action handler = Changed;
            handler?.Invoke();
        }
    }
}
