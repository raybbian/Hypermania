using UnityEngine;

namespace Design.Animation.Sorting
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteSortItem : MonoBehaviour
    {
        public SpriteRenderer Renderer
        {
            get => GetComponent<SpriteRenderer>();
        }

        // Used only for editor UI; if null we use renderer.sprite.
        [SerializeField]
        private Sprite _thumbnailOverride;
        public Sprite ThumbnailOverride => _thumbnailOverride;

        // Optional: stable tiebreaker if you ever want auto-sorting by some key.
        [SerializeField]
        private int _priority;
        public int Priority => _priority;
    }
}
