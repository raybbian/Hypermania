using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class BurstBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider;

        [SerializeField] 
        private float lerpSpeed = 30f; // burst bar update smoothness, higher = faster

        public void SetMaxBurst(float burst) 
        {
            _slider.maxValue = burst;
            _slider.value = burst;
        }

        public void SetBurst(float burst)
        {
            _slider.value = Mathf.Lerp(_slider.value, burst, Time.deltaTime * lerpSpeed);
        }
    }
}
