using Design.Configs;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// One skin slot in a <see cref="SkinSelector"/>. Owns three tint channels
    /// (main / light / accent) applied to child <see cref="Image"/>s from a
    /// <see cref="SkinConfig"/>. The whole icon dims via its own CanvasGroup
    /// when the skin is taken by the other player.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SkinIcon : MonoBehaviour
    {
        [SerializeField]
        private Image[] _mainImages;

        [SerializeField]
        private Image[] _lightImages;

        [SerializeField]
        private Image[] _accentImages;

        [SerializeField]
        [Range(0f, 1f)]
        private float _takenAlpha = 0.35f;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void SetSkin(SkinConfig skin)
        {
            ApplyTint(_mainImages, skin.MainColor);
            ApplyTint(_lightImages, skin.LightColor);
            ApplyTint(_accentImages, skin.AccentColor);
        }

        public void SetTaken(bool taken)
        {
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = taken ? _takenAlpha : 1f;
        }

        private static void ApplyTint(Image[] images, Color color)
        {
            if (images == null)
                return;
            foreach (Image img in images)
            {
                if (img != null)
                    img.color = color;
            }
        }
    }
}
