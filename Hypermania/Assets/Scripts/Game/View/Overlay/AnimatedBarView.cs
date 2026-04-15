using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class AnimatedBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider;

        [FormerlySerializedAs("lerpSpeed")]
        [SerializeField]
        private float _lerpSpeed = 70f; // burst bar update smoothness, higher = faster

        public void SetMaxValue(float burst)
        {
            _slider.maxValue = burst;
            _slider.value = burst;
        }

        public void SetValue(float burst)
        {
            _slider.value = Mathf.Lerp(_slider.value, burst, Time.deltaTime * _lerpSpeed);
        }
    }
}
