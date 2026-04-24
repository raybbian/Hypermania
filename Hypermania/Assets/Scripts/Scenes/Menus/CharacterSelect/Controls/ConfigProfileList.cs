using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Left-side column of the controls menu: one row per profile plus a
    /// trailing "+ New Profile" dummy that creates a fresh profile when
    /// confirmed. Pools clones of <see cref="_rowTemplate"/>.
    /// </summary>
    public class ConfigProfileList : MonoBehaviour
    {
        [Tooltip("Row template — disabled on Awake so only clones show.")]
        [SerializeField]
        private ConfigProfileRow _rowTemplate;

        [Tooltip("Parent for instantiated rows. Attach a VerticalLayoutGroup for spacing.")]
        [SerializeField]
        private RectTransform _container;

        [SerializeField]
        private string _newProfileLabel = "+ New Profile";

        private readonly List<ConfigProfileRow> _rows = new List<ConfigProfileRow>();

        private void Awake()
        {
            if (_rowTemplate != null)
                _rowTemplate.gameObject.SetActive(false);
        }

        public int RowCount(List<ControlsProfile> profiles)
        {
            return (profiles?.Count ?? 0) + 1;
        }

        public bool IsNewProfileRow(List<ControlsProfile> profiles, int index)
        {
            return index == (profiles?.Count ?? 0);
        }

        public void Refresh(
            List<ControlsProfile> profiles,
            int selectedIndex,
            bool focused,
            Func<string, bool> isTakenByOther
        )
        {
            int profileCount = profiles?.Count ?? 0;
            int desired = profileCount + 1;

            EnsureRowCount(desired);

            for (int i = 0; i < _rows.Count; i++)
            {
                ConfigProfileRow row = _rows[i];
                if (row == null)
                    continue;
                bool isNew = i == profileCount;
                string label = isNew ? _newProfileLabel : profiles[i].Name;
                bool taken = !isNew && isTakenByOther != null && isTakenByOther(label);
                row.SetDisplay(label, isNew, focused && i == selectedIndex, taken);
            }
        }

        private void EnsureRowCount(int desired)
        {
            if (_rowTemplate == null || _container == null)
                return;
            while (_rows.Count < desired)
            {
                ConfigProfileRow clone = Instantiate(_rowTemplate, _container);
                clone.gameObject.SetActive(true);
                _rows.Add(clone);
            }
            while (_rows.Count > desired)
            {
                ConfigProfileRow extra = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                if (extra != null)
                    Destroy(extra.gameObject);
            }
        }
    }
}
