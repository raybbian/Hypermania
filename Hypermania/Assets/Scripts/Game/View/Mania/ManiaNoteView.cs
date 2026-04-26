using Game.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Mania
{
    public class ManiaNoteView : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _tail;

        [SerializeField]
        private Image _head;

        public void ApplySprites(Sprite inactive, Sprite active)
        {
            if (_head != null)
            {
                _head.sprite = active;
            }
        }

        public void Render(float x, float y, ManiaNote note, float scrollSpeed)
        {
            transform.localPosition = new Vector3(x, y, -1);
            _tail.anchoredPosition = new Vector3(x, y);
            _tail.sizeDelta = new Vector2(_tail.sizeDelta.x, scrollSpeed * note.Length);
        }
    }
}
