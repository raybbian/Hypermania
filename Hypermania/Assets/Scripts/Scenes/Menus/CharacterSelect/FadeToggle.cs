using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Wraps a <see cref="CanvasGroup"/> and fades its alpha toward 0 or 1
    /// each frame based on the current visibility flag. Raycasts and
    /// interactability are only enabled while fully visible so mid-fade UI
    /// doesn't swallow input.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class FadeToggle : MonoBehaviour
    {
        [SerializeField]
        private float _fadeSpeed = 8f;

        [SerializeField]
        private bool _visibleOnAwake;

        private CanvasGroup _group;
        private bool _visible;

        public bool Visible => _visible;

        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        /// <summary>
        /// Snap to the given visibility without fading. Useful on bind to
        /// avoid a fade-in flash when the panel is first shown.
        /// </summary>
        public void SnapTo(bool visible)
        {
            EnsureGroup();
            _visible = visible;
            float target = visible ? 1f : 0f;
            _group.alpha = target;
            _group.interactable = visible;
            _group.blocksRaycasts = visible;
        }

        private void Awake()
        {
            EnsureGroup();
            SnapTo(_visibleOnAwake);
        }

        private void Update()
        {
            float target = _visible ? 1f : 0f;
            if (!Mathf.Approximately(_group.alpha, target))
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, _fadeSpeed * Time.deltaTime);
            }
            bool fullyVisible = _visible && _group.alpha >= 1f;
            _group.interactable = fullyVisible;
            _group.blocksRaycasts = fullyVisible;
        }

        private void EnsureGroup()
        {
            if (_group == null)
                _group = GetComponent<CanvasGroup>();
        }
    }
}
