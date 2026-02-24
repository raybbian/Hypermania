using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    public class BurstBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider;

        public void SetMaxBurst(float burst)
        {
            _slider.maxValue = burst;
            _slider.value = burst;
        }

        public void SetBurst(float burst)
        {
            _slider.value = burst;
        }
    }
}
