using System.Collections.Generic;
using Design.Configs;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Bottom-row roster. The directory calls <see cref="Initialize"/> with the
    /// roster and a random-skin config, which instantiates a
    /// <see cref="CharacterIcon"/> per character and, if the random-skin has a
    /// portrait, appends a Random tile after the roster icons. Child
    /// positioning is handled by an external layout component (e.g.
    /// HorizontalLayoutGroup on <see cref="_container"/>); the grid itself
    /// only instantiates and binds the icons. <see cref="GetSlotRect"/> lets
    /// cursors track slot positions in world space regardless of parenting.
    /// </summary>
    public class CharacterGrid : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _container;

        [SerializeField]
        private CharacterIcon _iconPrefab;

        private readonly List<CharacterIcon> _icons = new();
        private bool _hasRandomSlot;

        public int SlotCount => _icons.Count;
        public bool HasRandomSlot => _hasRandomSlot;
        public int RandomSlotIndex => _hasRandomSlot ? _icons.Count - 1 : -1;

        public void Initialize(CharacterConfig[] roster, SkinConfig randomSkin)
        {
            foreach (CharacterIcon icon in _icons)
            {
                if (icon != null)
                    Destroy(icon.gameObject);
            }
            _icons.Clear();

            if (_container == null || _iconPrefab == null || roster == null)
                return;

            for (int i = 0; i < roster.Length; i++)
            {
                CharacterIcon icon = Instantiate(_iconPrefab, _container, false);
                icon.SetCharacter(roster[i]);
                _icons.Add(icon);
            }

            _hasRandomSlot = randomSkin.Portrait != null;
            if (_hasRandomSlot)
            {
                CharacterIcon randomIcon = Instantiate(_iconPrefab, _container, false);
                randomIcon.SetSkin(randomSkin);
                _icons.Add(randomIcon);
            }
        }

        /// <summary>
        /// RectTransform of the slot at <paramref name="index"/>. Cursors use
        /// this to track slot positions in world space so they can be parented
        /// anywhere in the canvas hierarchy, not just as siblings of the icons.
        /// </summary>
        public RectTransform GetSlotRect(int index)
        {
            if (_icons.Count == 0)
                return null;
            int clamped = Mathf.Clamp(index, 0, _icons.Count - 1);
            return _icons[clamped].transform as RectTransform;
        }
    }
}
