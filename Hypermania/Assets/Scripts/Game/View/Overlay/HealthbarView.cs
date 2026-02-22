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
        private Slider _slider;

        [Header("Spin")]
        [SerializeField]
        private float _diskSpinSpeed = 90f;

        [Header("Audio Visualizer")]
        [SerializeField]
        private AudioSource _audioSource;

        // FFT
        [SerializeField]
        private int _fftSize = 1024;

        [SerializeField]
        private FFTWindow _fftWindow = FFTWindow.BlackmanHarris;

        // Bass band (Hz)
        [SerializeField]
        private float _bassLowHz = 20f;

        [SerializeField]
        private float _bassHighHz = 180f;

        // Scaling
        [SerializeField]
        private float _scaleBase = 1.0f;

        [SerializeField]
        private float _scaleRange = 0.35f;

        [SerializeField]
        private float _floorFollow = 0.5f; // slower = more stable baseline

        [SerializeField]
        private float _peakDecay = 0.25f; // how fast peak falls (per second)

        [SerializeField]
        private float _attack = 18f; // rise speed

        [SerializeField]
        private float _release = 10f; // fall speed

        [SerializeField]
        private float _compress = 0.6f; // 0..1, higher = more compression

        [SerializeField] 
        private float lerpSpeed = 25f; // heatlh bar update smoothness, higher = faster

        private float _floor = 0.00001f;
        private float _peak = 0.00002f;
        private float _smoothed;
        private float[] _spectrum;
        private Vector3 _diskBaseScale;

        void Awake()
        {
            _diskBaseScale = _disk.transform.localScale;
            _fftSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_fftSize, 64, 8192));
            _spectrum = new float[_fftSize];
        }

        public void SetMaxHealth(float health)
        {
            _slider.maxValue = health;
            _slider.value = health;
        }

        public void SetHealth(float health)
        {
            _slider.value = Mathf.Lerp(_slider.value, health, Time.deltaTime * lerpSpeed);
        }

        void Update()
        {
            _disk.transform.Rotate(0f, 0f, _diskSpinSpeed * Time.deltaTime);

            _audioSource.GetSpectrumData(_spectrum, 0, _fftWindow);

            float bandEnergy = ComputeBandEnergy(_spectrum, _bassLowHz, _bassHighHz, AudioSettings.outputSampleRate);

            // Update baseline (floor): follow downward slowly, upward faster
            _floor = Mathf.Lerp(_floor, bandEnergy, 1f - Mathf.Exp(-_floorFollow * Time.deltaTime));

            // Update peak: snap up, decay down
            _peak = Mathf.Max(_peak, bandEnergy);
            _peak = Mathf.Lerp(_peak, _floor, 1f - Mathf.Exp(-_peakDecay * Time.deltaTime));

            // Normalize to 0..1 between floor and peak
            float denom = Mathf.Max(1e-6f, _peak - _floor);
            float norm = Mathf.Clamp01((bandEnergy - _floor) / denom);

            // Optional compression so it’s less “spiky”
            if (_compress > 0f)
                norm = Mathf.Pow(norm, 1f - _compress); // e.g. compress=0.6 => pow(norm,0.4)

            // Smooth output (attack/release)
            float rate = (norm > _smoothed) ? _attack : _release;
            _smoothed = Mathf.Lerp(_smoothed, norm, 1f - Mathf.Exp(-rate * Time.deltaTime));

            float s = _scaleBase + _smoothed * _scaleRange;
            _disk.transform.localScale = _diskBaseScale * s;
        }

        private static float ComputeBandEnergy(float[] spectrum, float lowHz, float highHz, int sampleRate)
        {
            float nyquist = sampleRate * 0.5f;
            lowHz = Mathf.Clamp(lowHz, 0f, nyquist);
            highHz = Mathf.Clamp(highHz, lowHz, nyquist);

            int n = spectrum.Length;
            int lo = Mathf.Clamp(Mathf.FloorToInt((lowHz / nyquist) * (n - 1)), 0, n - 1);
            int hi = Mathf.Clamp(Mathf.CeilToInt((highHz / nyquist) * (n - 1)), 0, n - 1);

            // Energy (square) is more stable than raw magnitudes
            float sum = 0f;
            int count = 0;
            for (int i = lo; i <= hi; i++)
            {
                float v = spectrum[i];
                sum += v * v;
                count++;
            }
            return (count > 0) ? (sum / count) : 0f;
        }
    }
}
