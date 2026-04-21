using Design.Configs;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class HealthBarView : MonoBehaviour
    {
        public void SetOutlinePlayerIndex(int playerIndex)
        {
            EntityView.SetLayerRecursive(gameObject, 6 + playerIndex);
        }

        [Header("UI")]
        [SerializeField]
        private Transform[] _disks;

        [SerializeField]
        private RawImage _portrait;

        [SerializeField]
        private Image[] _tint;

        [SerializeField]
        private Slider _healthSlider;

        [SerializeField]
        private Slider _healthShadowSlider;

        [SerializeField]
        private float _diskSpinSpeed = 90f;

        [SerializeField]
        private float _diskScaleRange = 0.35f;

        [SerializeField]
        private float lerpSpeed = 30f; // heatlh bar shadow update smoothness, higher = faster

        private float _shadowTargetHealth;
        private int _prevComboCount;
        private float _prevHealth;
        private Vector3 _baseScale;

        public void Init(CharacterConfig config, int skinIndex)
        {
            _baseScale = _disks[0].localScale;
            _portrait.texture = config.Skins[skinIndex].Portrait;
            foreach (var tint in _tint)
                tint.color = config.Skins[skinIndex].AccentColor;
        }

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
            foreach (Transform disk in _disks)
            {
                disk.Rotate(0f, 0f, _diskSpinSpeed * Time.deltaTime);
                disk.localScale =
                    _baseScale + Vector3.one * GetComponent<MusicReactive>().GetMusicValue() * _diskScaleRange;
            }

            _healthShadowSlider.value = Mathf.MoveTowards(
                _healthShadowSlider.value,
                _shadowTargetHealth,
                lerpSpeed * Time.deltaTime
            );
        }
    }
}
