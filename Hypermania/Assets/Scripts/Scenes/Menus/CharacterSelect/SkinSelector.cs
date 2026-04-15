using System.Collections.Generic;
using Design.Configs;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Visual skin picker for one player's options panel. Spawns a
    /// <see cref="SkinIcon"/> per available skin on the current character,
    /// lays them out via the <see cref="_iconContainer"/>'s layout component,
    /// and lerps <see cref="_cursor"/> to the active icon. Mirrors
    /// <see cref="CharacterGrid"/> / <see cref="CharacterCursor"/> patterns.
    /// On the Random slot, icons and cursor hide and <see cref="_emptyStateRoot"/>
    /// is shown — the row's CanvasGroup (managed by the options panel) handles
    /// the grayed-out alpha.
    /// </summary>
    public class SkinSelector : MonoBehaviour
    {
        [Header("Icon layout")]
        [SerializeField]
        [Tooltip("Child container with a HorizontalLayoutGroup — icons are spawned here.")]
        private RectTransform _iconContainer;

        [SerializeField]
        private SkinIcon _iconPrefab;

        [Header("Cursor")]
        [SerializeField]
        private RectTransform _cursor;

        [SerializeField]
        private float _cursorMoveSpeed = 18f;

        [Header("Empty state (shown when player is on the Random slot)")]
        [SerializeField]
        private GameObject _emptyStateRoot;

        private PlayerSelectionState _state;
        private PlayerSelectionState _otherState;
        private CharacterConfig[] _roster;
        private readonly List<SkinIcon> _icons = new();

        // Cached state so we only rebuild the icon list when the character
        // changes (or when crossing in/out of the Random slot).
        private int _builtCharIndex = -1;
        private bool _builtRandom = false;

        public void Bind(PlayerSelectionState state, PlayerSelectionState otherState, CharacterConfig[] roster)
        {
            _state = state;
            _otherState = otherState;
            _roster = roster;
            _builtCharIndex = -1;
            _builtRandom = false;
        }

        private void Update()
        {
            if (_state == null || _roster == null)
                return;

            bool onRandom = _state.CharacterIndex >= _roster.Length;

            if (onRandom)
            {
                if (!_builtRandom)
                {
                    ClearIcons();
                    _builtRandom = true;
                    _builtCharIndex = -1;
                }
                if (_emptyStateRoot != null && !_emptyStateRoot.activeSelf)
                    _emptyStateRoot.SetActive(true);
                if (_cursor != null && _cursor.gameObject.activeSelf)
                    _cursor.gameObject.SetActive(false);
                return;
            }

            int charIdx = Mathf.Clamp(_state.CharacterIndex, 0, _roster.Length - 1);
            if (_builtRandom || charIdx != _builtCharIndex)
            {
                Rebuild(_roster[charIdx]);
                _builtCharIndex = charIdx;
                _builtRandom = false;
            }

            if (_emptyStateRoot != null && _emptyStateRoot.activeSelf)
                _emptyStateRoot.SetActive(false);
            if (_cursor != null && !_cursor.gameObject.activeSelf)
                _cursor.gameObject.SetActive(true);

            RefreshTakenState(charIdx);
            RefreshCursor();
        }

        private void Rebuild(CharacterConfig config)
        {
            ClearIcons();

            if (_iconContainer == null || _iconPrefab == null)
                return;
            if (config == null || config.Skins == null || config.Skins.Length == 0)
                return;

            for (int i = 0; i < config.Skins.Length; i++)
            {
                SkinIcon icon = Instantiate(_iconPrefab, _iconContainer, false);
                icon.SetSkin(config.Skins[i]);
                icon.SetTaken(false);
                _icons.Add(icon);
            }
        }

        private void ClearIcons()
        {
            foreach (SkinIcon icon in _icons)
            {
                if (icon != null)
                    Destroy(icon.gameObject);
            }
            _icons.Clear();
        }

        private void RefreshTakenState(int charIdx)
        {
            // An icon is "taken" only when the other player is on the same
            // character AND has progressed past Character phase. Mirrors the
            // OtherCharacter = -1 guard in CharacterSelectDirectory.ApplyEdgesToSlot
            // — a player still browsing the grid hasn't actually claimed a skin.
            bool otherOnSameChar =
                _otherState != null
                && _otherState.Phase != SelectPhase.Character
                && _otherState.CharacterIndex == charIdx;
            int takenIdx = otherOnSameChar ? _otherState.SkinIndex : -1;
            for (int i = 0; i < _icons.Count; i++)
            {
                if (_icons[i] != null)
                    _icons[i].SetTaken(i == takenIdx);
            }
        }

        private void RefreshCursor()
        {
            if (_cursor == null || _icons.Count == 0)
                return;
            int idx = Mathf.Clamp(_state.SkinIndex, 0, _icons.Count - 1);
            SkinIcon target = _icons[idx];
            if (target == null)
                return;

            // World-space lerp so the cursor can be parented anywhere in the
            // canvas hierarchy, matching the CharacterCursor pattern.
            Vector3 targetWorld = target.transform.position;
            _cursor.position = Vector3.Lerp(_cursor.position, targetWorld, _cursorMoveSpeed * Time.deltaTime);
        }
    }
}
