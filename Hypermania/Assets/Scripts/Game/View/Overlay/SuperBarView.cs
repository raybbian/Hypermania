using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class SuperBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider1;

        [SerializeField]
        private Slider _slider2;

        [SerializeField]
        private Image _number1Image;

        [SerializeField]
        private Image _number2Image;

        [SerializeField]
        private float _lerpSpeed = 70f;

        private float _superCost;
        private float _targetValue;

        public void Init(float superCost)
        {
            _superCost = superCost;

            _slider1.minValue = 0f;
            _slider1.maxValue = _superCost;
            _slider1.value = 0f;

            _slider2.minValue = _superCost;
            _slider2.maxValue = _superCost + _superCost;
            _slider2.value = _superCost;

            _targetValue = 0f;
            UpdateNumbers(0f);
        }

        public void SetValue(float super)
        {
            _targetValue = super;
        }

        private void Update()
        {
            float s1Target = Mathf.Clamp(_targetValue, 0f, _superCost);
            _slider1.value = Mathf.Lerp(_slider1.value, s1Target, Time.deltaTime * _lerpSpeed);

            float doubleCost = _superCost + _superCost;
            float s2Target = Mathf.Clamp(_targetValue, _superCost, doubleCost);
            _slider2.value = Mathf.Lerp(_slider2.value, s2Target, Time.deltaTime * _lerpSpeed);

            UpdateNumbers(_targetValue);
        }

        private void UpdateNumbers(float super)
        {
            float doubleCost = _superCost + _superCost;
            bool canTier2 = super >= doubleCost;
            bool canTier1 = super >= _superCost;
            if (_number2Image != null)
                _number2Image.enabled = canTier2;
            if (_number1Image != null)
                _number1Image.enabled = canTier1 && !canTier2;
        }
    }
}
