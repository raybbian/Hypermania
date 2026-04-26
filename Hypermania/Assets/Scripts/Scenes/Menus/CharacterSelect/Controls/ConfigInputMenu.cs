using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Right-side binding column: one <see cref="ConfigInputListener"/> per
    /// <see cref="InputFlags"/> value (minus <see cref="InputFlags.None"/>),
    /// ordered by the enum. Pools clones of <see cref="_rowTemplate"/>.
    /// </summary>
    public class ConfigInputMenu : MonoBehaviour
    {
        [Tooltip("Row template — disabled on Awake so only clones show.")]
        [SerializeField]
        private ConfigInputListener _rowTemplate;

        [Tooltip("Parent for instantiated rows. Attach a VerticalLayoutGroup for spacing.")]
        [SerializeField]
        private RectTransform _container;

        private readonly List<ConfigInputListener> _rows = new List<ConfigInputListener>();
        private readonly List<InputFlags> _flagsOrder = new List<InputFlags>();
        private bool _flagsResolved;

        public int RowCount => EnsureFlagsResolved().Count;

        public InputFlags FlagAt(int rowIndex)
        {
            IReadOnlyList<InputFlags> order = EnsureFlagsResolved();
            if (rowIndex < 0 || rowIndex >= order.Count)
                return InputFlags.None;
            return order[rowIndex];
        }

        private void Awake()
        {
            if (_rowTemplate != null)
                _rowTemplate.gameObject.SetActive(false);
        }

        public void Refresh(ControlsProfile profile, ConfigDevice device)
        {
            IReadOnlyList<InputFlags> order = EnsureFlagsResolved();
            EnsureRowCount(order.Count);

            for (int i = 0; i < _rows.Count; i++)
            {
                ConfigInputListener row = _rows[i];
                if (row == null)
                    continue;
                InputFlags flag = order[i];
                Binding binding = profile != null ? profile.Bindings[flag] : null;
                row.Bind(flag, device, binding);
            }
        }

        /// <summary>Negative <paramref name="focusedRow"/> clears focus everywhere.</summary>
        public void SetFocus(int focusedRow, BindingSlot slot, bool listening)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ConfigInputListener row = _rows[i];
                if (row == null)
                    continue;
                row.SetFocus(i == focusedRow, slot, listening && i == focusedRow);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ConfigInputListener row = _rows[i];
                if (row != null)
                    row.Bind(InputFlags.None, ConfigDevice.Keyboard, null);
            }
        }

        private IReadOnlyList<InputFlags> EnsureFlagsResolved()
        {
            if (_flagsResolved)
                return _flagsOrder;
            _flagsOrder.Clear();
            foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
            {
                if (flag == InputFlags.None)
                    continue;
                _flagsOrder.Add(flag);
            }
            _flagsResolved = true;
            return _flagsOrder;
        }

        private void EnsureRowCount(int desired)
        {
            if (_rowTemplate == null || _container == null)
                return;
            while (_rows.Count < desired)
            {
                ConfigInputListener clone = Instantiate(_rowTemplate, _container);
                clone.gameObject.SetActive(true);
                _rows.Add(clone);
            }
            while (_rows.Count > desired)
            {
                ConfigInputListener extra = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                if (extra != null)
                    Destroy(extra.gameObject);
            }
        }
    }
}
