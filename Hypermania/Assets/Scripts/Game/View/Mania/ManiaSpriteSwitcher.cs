using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.View.Mania
{
    [RequireComponent(typeof(Image))]
    public class ManiaSpriteSwitcher : MonoBehaviour
    {
        [FormerlySerializedAs("spriteDefault")]
        public Sprite SpriteDefault;

        [FormerlySerializedAs("spritePressed")]
        public Sprite SpritePressed;

        private Image _imageComponent;

        void Awake()
        {
            _imageComponent = GetComponent<Image>();
            if (_imageComponent != null)
            {
                _imageComponent.sprite = SpriteDefault;
            }
        }

        public void ChangeSprite(bool pressed)
        {
            if (_imageComponent == null)
            {
                return;
            }
            if (pressed)
            {
                _imageComponent.sprite = SpritePressed;
            }
            else
            {
                _imageComponent.sprite = SpriteDefault;
            }
        }

        public void ApplySprites(Sprite inactive, Sprite active)
        {
            SpriteDefault = inactive;
            SpritePressed = active;
            if (_imageComponent == null)
            {
                _imageComponent = GetComponent<Image>();
            }
            if (_imageComponent != null)
            {
                _imageComponent.sprite = SpriteDefault;
            }
        }
    }
}
