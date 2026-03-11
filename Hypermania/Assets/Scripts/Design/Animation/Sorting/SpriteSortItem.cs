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

        [SerializeField]
        private Sprite _thumbnailOverride;
        public Sprite ThumbnailOverride => _thumbnailOverride;

        [SerializeField]
        private int _priority;
        public int Priority => _priority;
    }
}
