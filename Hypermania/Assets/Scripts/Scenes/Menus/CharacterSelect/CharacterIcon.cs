using Design.Configs;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// One character slot in the bottom-row roster. Owns its own portrait
    /// <see cref="RawImage"/> and a list of <see cref="Image"/>s to accent-tint
    /// from the character's first skin. <see cref="CharacterGrid"/> instantiates
    /// these per roster entry.
    /// </summary>
    public class CharacterIcon : MonoBehaviour
    {
        [SerializeField]
        private RawImage _portrait;

        [SerializeField]
        private Image[] _tintImages;

        public void SetCharacter(CharacterConfig config)
        {
            bool hasSkin = config != null && config.Skins != null && config.Skins.Length > 0;
            SetSkin(hasSkin ? config.Skins[0] : default, hasSkin);
        }

        public void SetSkin(SkinConfig skin)
        {
            SetSkin(skin, true);
        }

        private void SetSkin(SkinConfig skin, bool hasSkin)
        {
            if (_portrait != null)
            {
                _portrait.texture = hasSkin ? skin.Portrait : null;
                _portrait.enabled = hasSkin && skin.Portrait != null;
            }

            if (_tintImages == null)
                return;
            Color tint = hasSkin ? skin.AccentColor : Color.white;
            foreach (Image img in _tintImages)
            {
                if (img != null)
                    img.color = tint;
            }
        }
    }
}
