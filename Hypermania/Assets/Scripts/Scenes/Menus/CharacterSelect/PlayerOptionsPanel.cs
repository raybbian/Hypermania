using Design.Configs;
using Game.Sim;
using TMPro;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Renders the five-row options panel for a single player. The directory
    /// binds the panel with the shared <see cref="PlayerSelectionState"/>,
    /// the character roster, and the controls presets; the panel watches the
    /// state every frame and redraws row values + row visibility on its own.
    /// </summary>
    public class PlayerOptionsPanel : MonoBehaviour
    {
        [Header("Row containers (indexed 0..4 per OptionsRows)")]
        [SerializeField]
        private RectTransform[] _rowContainers = new RectTransform[OptionsRows.Count];

        [SerializeField]
        private TMP_Text[] _rowValues = new TMP_Text[OptionsRows.Count];

        [Header("Skin row — visual picker replaces the TMP_Text value")]
        [SerializeField]
        private SkinSelector _skinSelector;

        [Header("Highlight indicator moved to current row")]
        [SerializeField]
        private RectTransform _rowHighlight;

        [SerializeField]
        private float _highlightMoveSpeed = 18f;

        [Header("Row grayed-out look (applied via CanvasGroup.alpha if present)")]
        [SerializeField]
        private float _enabledAlpha = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        private float _disabledAlpha = 0.35f;

        private PlayerSelectionState _state;
        private CharacterConfig[] _roster;
        private ControlsConfig[] _controlsPresets;
        private bool _isLocal;

        // Lazily resolved once per row on the first visibility pass. Rows
        // without a CanvasGroup simply skip the gray-out effect.
        private readonly CanvasGroup[] _rowGroups = new CanvasGroup[OptionsRows.Count];
        private bool _rowGroupsResolved;

        public void Bind(
            PlayerSelectionState state,
            PlayerSelectionState otherState,
            CharacterConfig[] roster,
            ControlsConfig[] controlsPresets,
            bool isLocal
        )
        {
            _state = state;
            _roster = roster;
            _controlsPresets = controlsPresets;
            _isLocal = isLocal;

            if (_skinSelector != null)
                _skinSelector.Bind(state, otherState, roster);
        }

        private void Update()
        {
            if (_state == null)
                return;

            EnsureRowGroupsResolved();
            RefreshRowStateVisuals();
            RefreshRowValues();
            RefreshHighlight();
        }

        private void EnsureRowGroupsResolved()
        {
            if (_rowGroupsResolved)
                return;
            for (int i = 0; i < _rowContainers.Length && i < _rowGroups.Length; i++)
            {
                if (_rowContainers[i] != null)
                    _rowGroups[i] = _rowContainers[i].GetComponent<CanvasGroup>();
            }
            _rowGroupsResolved = true;
        }

        /// <summary>
        /// Hides rows that aren't visible at all and applies the grayed-out
        /// alpha to rows that are visible but non-interactable. Interactable
        /// rows render fully opaque.
        /// </summary>
        private void RefreshRowStateVisuals()
        {
            for (int i = 0; i < _rowContainers.Length; i++)
            {
                if (_rowContainers[i] == null)
                    continue;
                bool visible = OptionsRows.IsVisible(_state, _isLocal, i);
                if (_rowContainers[i].gameObject.activeSelf != visible)
                {
                    _rowContainers[i].gameObject.SetActive(visible);
                }
                if (!visible)
                    continue;

                CanvasGroup group = _rowGroups[i];
                if (group == null)
                    continue;
                bool interactable = OptionsRows.IsInteractable(
                    _state,
                    _isLocal,
                    i,
                    _roster != null ? _roster.Length : int.MaxValue
                );
                float targetAlpha = interactable ? _enabledAlpha : _disabledAlpha;
                if (!Mathf.Approximately(group.alpha, targetAlpha))
                    group.alpha = targetAlpha;
                group.interactable = interactable;
                group.blocksRaycasts = interactable;
            }
        }

        private void RefreshRowValues()
        {
            SetRowText(OptionsRows.ComboMode, _state.ComboMode == ComboMode.Assisted ? "Assisted" : "Freestyle");
            // OptionsRows.Skin is rendered by _skinSelector, not a TMP_Text.
            SetRowText(
                OptionsRows.ManiaDifficulty,
                _state.ManiaDifficulty == ManiaDifficulty.Normal ? "Normal" : "Hard"
            );
            SetRowText(OptionsRows.BeatCancel, FormatBeatCancel(_state.BeatCancelWindow));
            SetRowText(OptionsRows.ControlsPreset, FormatControls(_state.ControlsIndex));
        }

        private void SetRowText(int row, string value)
        {
            if (row < 0 || row >= _rowValues.Length)
                return;
            TMP_Text label = _rowValues[row];
            if (label == null)
                return;
            if (label.text != value)
            {
                label.text = value;
            }
        }

        private static string FormatBeatCancel(BeatCancelWindow window)
        {
            switch (window)
            {
                case BeatCancelWindow.Medium:
                    return "Medium (5)";
                case BeatCancelWindow.Hard:
                    return "Hard (3)";
                default:
                    return window.ToString();
            }
        }

        private string FormatControls(int idx)
        {
            // Controls are inherently local and never synced, so the remote
            // panel always shows the placeholder rather than our own preset.
            if (!_isLocal)
                return "-";
            if (_controlsPresets == null || _controlsPresets.Length == 0)
                return "-";
            int clamped = Mathf.Clamp(idx, 0, _controlsPresets.Length - 1);
            ControlsConfig config = _controlsPresets[clamped];
            return config != null ? config.name : $"Preset {clamped + 1}";
        }

        /// <summary>
        /// Lerp the highlight to the active row. Uses world-space positions so
        /// the highlight can be parented anywhere in the hierarchy (not just as
        /// a sibling of the row containers).
        /// </summary>
        private void RefreshHighlight()
        {
            if (_rowHighlight == null)
                return;
            int row = _state.OptionsRow;
            if (row < 0 || row >= _rowContainers.Length)
                return;
            RectTransform target = _rowContainers[row];
            if (target == null || !target.gameObject.activeSelf)
                return;

            Vector3 targetWorld = target.position;
            _rowHighlight.position = Vector3.Lerp(
                _rowHighlight.position,
                targetWorld,
                _highlightMoveSpeed * Time.deltaTime
            );
        }
    }
}
