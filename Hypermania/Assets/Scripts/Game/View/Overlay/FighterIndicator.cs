using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    // A map pin for a fighter who is off screen: their portrait in a disc,
    // with a tail that points to where they are. It has its own overlay
    // canvas because the fighters' glow outline is painted over everything
    // in the world, and would cover it.
    [RequireComponent(typeof(Canvas))]
    public class FighterIndicator : MonoBehaviour
    {
        // Matches _TipDistance in UI/FighterIndicator/FighterIndicatorPin.shader.
        private static readonly int TipDistanceId = Shader.PropertyToID("_TipDistance");

        // Everything that moves and scales.
        [SerializeField]
        private RectTransform _content;

        [SerializeField]
        private RawImage _portrait;

        // Holds the pin's shape. Its tail runs along its local up.
        [SerializeField]
        private Transform _pin;

        [SerializeField]
        private RawImage _body;

        // The same shape as the body, a little bigger and drawn behind it.
        [SerializeField]
        private RawImage _border;

        // World units per unit of the layout. The pin is sized and placed
        // as if it stood in the world, so it follows the camera's zoom.
        [SerializeField]
        private float _worldScale = 0.005f;

        // How long it takes to grow in or shrink away.
        [SerializeField]
        private float _showSeconds = 0.15f;

        private Canvas _canvas;
        private Camera _camera;

        // From the middle of the head to the very tip, in world units at full size.
        private float _tipDistance;

        private Vector2 _head;
        private Vector2 _direction;
        private bool _visible;

        // 0 when fully hidden, 1 when fully out.
        private float _shown;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.enabled = false;
            float tipInRect = _border.rectTransform.rect.height * 0.5f * _border.material.GetFloat(TipDistanceId);
            _tipDistance = tipInRect * _worldScale;
        }

        private void Update()
        {
            float target = _visible ? 1f : 0f;
            if (_shown == target)
                return;

            _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime / Mathf.Max(_showSeconds, 1e-4f));
            Place();
        }

        public void Init(Camera camera, Texture portrait, Color color, Color borderColor)
        {
            _camera = camera;
            _portrait.texture = portrait;
            _portrait.color = color;
            _body.color = color;
            _border.color = borderColor;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        // Where the head sits in the world at full size, and which way the
        // tail points from there. Only the pin turns, so the portrait stays
        // upright.
        public void PointAt(Vector2 head, Vector2 direction)
        {
            _head = head;
            _direction = direction.normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            _pin.rotation = Quaternion.Euler(0f, 0f, angle);
            Place();
        }

        private void Place()
        {
            // Quick at the small end and gentle at full size, both ways.
            float eased = 1f - (1f - _shown) * (1f - _shown);

            // It grows out of its tip, so the tip stays put and the head
            // slides back from it.
            Vector2 world = _head + _direction * (_tipDistance * (1f - eased));
            float pixelsPerUnit = _camera.pixelHeight / (2f * _camera.orthographicSize);

            // An overlay canvas is laid out in screen pixels.
            _content.position = (Vector2)_camera.WorldToScreenPoint(world);
            _content.localScale = Vector3.one * (_worldScale * pixelsPerUnit * eased);
            _canvas.enabled = _shown > 0f;
        }
    }
}
