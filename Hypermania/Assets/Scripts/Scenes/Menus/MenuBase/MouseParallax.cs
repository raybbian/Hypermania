using UnityEngine;

namespace Scenes.Menus.MenuBase
{
    public class MouseParallax : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private RectTransform _canvasRect;
        private Canvas _canvas;
        private Vector2 _maxDisplacement;

        [SerializeField]
        private float _smoothing = 5f;

        public void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>().rootCanvas;
            _canvasRect = _canvas.GetComponent<RectTransform>();

            Vector2 elementSize = _rectTransform.rect.size;
            Vector2 canvasSize = _canvasRect.rect.size;
            _maxDisplacement = Vector2.Max((elementSize - canvasSize) / 2f, Vector2.zero);
        }

        public void Update()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                Input.mousePosition,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
                out Vector2 localPoint
            );

            Vector2 canvasSize = _canvasRect.rect.size;
            Vector2 normalized = new Vector2(
                Mathf.Clamp(localPoint.x / (canvasSize.x / 2f), -1f, 1f),
                Mathf.Clamp(localPoint.y / (canvasSize.y / 2f), -1f, 1f)
            );

            Vector2 target = -normalized * _maxDisplacement;
            _rectTransform.anchoredPosition = Vector2.Lerp(
                _rectTransform.anchoredPosition,
                target,
                Time.deltaTime * _smoothing
            );
        }
    }
}
