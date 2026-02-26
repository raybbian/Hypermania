using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class HypeBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider;
        public float maxValue;

        public void SetMaxHype(float hype)
        {
            _slider.maxValue = hype;
        }

        public void SetHype(float hype)
        {
            float normalizedHype = (hype + maxValue) / (2 * maxValue);
            _slider.value = normalizedHype;
        }
    }
}
