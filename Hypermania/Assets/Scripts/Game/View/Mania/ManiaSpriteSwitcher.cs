using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Mania
{
    public class ManiaSpriteSwitcher : MonoBehaviour
    {
        public Sprite spriteDefault;
        public Sprite spritePressed;

        private Image imageComp;

        void Start()
        {
            imageComp = GetComponent<Image>();
            if (imageComp != null)
            {
                imageComp.sprite = spriteDefault;
            }
        }

        public void ChangeSprite(bool pressed)
        {
            if (pressed)
            {
                imageComp.sprite = spritePressed;
            }
            else
            {
                imageComp.sprite = spriteDefault;
            }
        }
    }
}
