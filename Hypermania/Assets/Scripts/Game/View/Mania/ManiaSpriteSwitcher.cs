using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.View.Mania
{
    public class ManiaSpriteSwitcher : MonoBehaviour
    {
        [FormerlySerializedAs("spriteDefault")]
        public Sprite SpriteDefault;

        [FormerlySerializedAs("spritePressed")]
        public Sprite SpritePressed;

        private Image _imageComponent;

        void Start()
        {
            _imageComponent = GetComponent<Image>();
            if (_imageComponent != null)
            {
                _imageComponent.sprite = SpriteDefault;
            }
        }

        public void ChangeSprite(bool pressed)
        {
            if (pressed)
            {
                _imageComponent.sprite = SpritePressed;
            }
            else
            {
                _imageComponent.sprite = SpriteDefault;
            }
        }
    }
}
