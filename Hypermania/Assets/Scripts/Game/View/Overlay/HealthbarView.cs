using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class HealthBarView : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField]
        private GameObject _disk;

        [SerializeField]
        private Slider _healthSlider;

        [SerializeField]
        private Slider _healthShadowSlider;

        [Header("Spin")]
        [SerializeField]
        private float _diskSpinSpeed = 90f;

        [SerializeField]
        private float lerpSpeed = 30f; // heatlh bar shadow update smoothness, higher = faster

        private float _shadowTargetHealth;
        private int _prevComboCount;
        private float _prevHealth;

        public void SetMaxHealth(float health)
        {
            _healthSlider.maxValue = health;
            _healthSlider.value = health;
            SetMaxShadowHealth(health);
        }

        public void SetHealth(float health)
        {
            _healthSlider.value = health;
        }

        public void SetMaxShadowHealth(float health)
        {
            _healthShadowSlider.maxValue = health;
            _healthShadowSlider.value = health;
            _shadowTargetHealth = health;
        }

        public void SetCombo(int combo, int health)
        {
            if (_prevComboCount > 0 && combo == 0) // if combo ends
            {
                _shadowTargetHealth = health;
            }
            if (combo > _prevComboCount && _healthShadowSlider.value > _shadowTargetHealth) // character hit while shadow bar is draining
            {
                _healthShadowSlider.value = _shadowTargetHealth;
                _shadowTargetHealth = _prevHealth;
            }
            _prevHealth = health;
            _prevComboCount = combo;
        }

        void Update()
        {
            _disk.transform.Rotate(0f, 0f, _diskSpinSpeed * Time.deltaTime);

            _healthShadowSlider.value = Mathf.MoveTowards(
                _healthShadowSlider.value,
                _shadowTargetHealth,
                lerpSpeed * Time.deltaTime
            );
        }
    }
}
