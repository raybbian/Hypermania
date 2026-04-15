using Game.View.Overlay;
using UnityEngine;

namespace Scenes.Menus.MenuBase
{
    [RequireComponent(typeof(MusicReactive))]
    public class MusicReactiveIcon : MonoBehaviour
    {
        [Header("Scale")]
        [SerializeField]
        private bool _useScale;

        [SerializeField]
        private float _scaleFactor = 0.1f;

        [Header("Rotation")]
        [SerializeField]
        private bool _useRotation;

        [SerializeField]
        private float _rotationAmplitude = 5f;

        [SerializeField]
        private float _rotationPeriod = 2f;

        [Header("Displacement")]
        [SerializeField]
        private bool _useDisplacement;

        [SerializeField]
        private Vector2 _displacementDirection = Vector2.up;

        [SerializeField]
        private float _displacementDistance = 10f;

        private MusicReactive _musicReactive;
        private RectTransform _rectTransform;
        private Vector3 _baseScale;
        private Vector2 _basePosition;
        private float _rotationOffset;

        public void Awake()
        {
            _musicReactive = GetComponent<MusicReactive>();
            _rectTransform = GetComponent<RectTransform>();
            _baseScale = _rectTransform.localScale;
            _basePosition = _rectTransform.anchoredPosition;
            _rotationOffset = Random.Range(0f, _rotationPeriod);
        }

        public void Update()
        {
            float musicValue = _musicReactive.GetMusicValue();

            if (_useScale)
            {
                _rectTransform.localScale = _baseScale + musicValue * _scaleFactor * _baseScale;
            }

            if (_useRotation)
            {
                float t = Mathf.Sin((Time.realtimeSinceStartup + _rotationOffset) * 2f * Mathf.PI / _rotationPeriod);
                transform.localRotation = Quaternion.Euler(0f, 0f, t * _rotationAmplitude);
            }

            if (_useDisplacement)
            {
                Vector2 offset = _displacementDirection.normalized * _displacementDistance * musicValue;
                _rectTransform.anchoredPosition = _basePosition + offset;
            }
        }
    }
}
