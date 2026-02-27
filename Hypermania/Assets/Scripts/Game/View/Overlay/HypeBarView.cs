using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class HypeBarView : MonoBehaviour
    {
        [SerializeField]
        private Slider _slider;

        public void SetMaxHype(float hype)
        {
            _slider.maxValue = 2 * hype;
        }

        public void SetHype(float hype)
        {
            float normalizedHype = hype + _slider.maxValue / 2;
            _slider.value = normalizedHype;
        }
    }
}
