using Design.Configs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class IntroCharacterPanel : MonoBehaviour
    {
        [SerializeField]
        private Image[] _mainColorTints;

        [SerializeField]
        private Image[] _lightColorTints;

        [SerializeField]
        private Image[] _accentColorTints;

        [SerializeField]
        private Image _splash;

        [SerializeField]
        private TMP_Text _characterName;

        private Sprite _runtimeSplash;

        public void SetCharacter(CharacterConfig config, int skinIndex)
        {
            SkinConfig skin = config.Skins[skinIndex];

            Tint(_mainColorTints, skin.MainColor);
            Tint(_lightColorTints, skin.LightColor);
            Tint(_accentColorTints, skin.AccentColor);

            if (_splash != null)
            {
                if (_runtimeSplash != null)
                    Destroy(_runtimeSplash);

                if (skin.Splash != null)
                {
                    Texture2D tex = skin.Splash;
                    _runtimeSplash = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    _splash.sprite = _runtimeSplash;
                }
                else
                {
                    _runtimeSplash = null;
                    _splash.sprite = null;
                }
            }

            if (_characterName != null)
                _characterName.text = config.Character.ToString();
        }

        private void OnDestroy()
        {
            if (_runtimeSplash != null)
                Destroy(_runtimeSplash);
        }

        private static void Tint(Image[] images, Color color)
        {
            if (images == null)
                return;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null)
                    images[i].color = color;
            }
        }
    }
}
