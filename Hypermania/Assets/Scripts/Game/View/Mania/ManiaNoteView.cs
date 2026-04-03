using Game.Sim;
using UnityEngine;

namespace Game.View.Mania
{
    public class ManiaNoteView : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _tail;

        public void Render(float x, float y, ManiaNote note, float scrollSpeed)
        {
            transform.localPosition = new Vector3(x, y, -1);
            _tail.anchoredPosition = new Vector3(x, y);
            _tail.sizeDelta = new Vector2(_tail.sizeDelta.x, scrollSpeed * note.Length);
        }
    }
}
