using Design.Configs;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View.Overlay
{
    public class HypeBarView : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField]
        private RectTransform _container;

        [SerializeField]
        private int _numRows = 4;

        [SerializeField]
        private int _numCols = 30;

        [Header("FFT")]
        [SerializeField]
        private int _fftSize = 1024;

        [SerializeField]
        private FFTWindow _fftWindow = FFTWindow.BlackmanHarris;

        [SerializeField]
        private AudioSource _audioSource;

        [Header("Frequency Range (Hz)")]
        [SerializeField]
        private float _minHz = 40f;

        [SerializeField]
        private float _maxHz = 12000f;

        [Header("Per-Band Smoothing")]
        [SerializeField]
        private float _floorFollow = 0.5f;

        [SerializeField]
        private float _peakDecay = 0.25f;

        [SerializeField]
        private float _attack = 18f;

        [SerializeField]
        private float _release = 10f;

        [SerializeField]
        private float _compress = 0.6f;

        [Header("Visuals")]
        [SerializeField]
        private Color _inactiveColor = Color.black;

        private Image[,] _cells;
        private float[] _spectrum;
        private float[] _bandLowHz;
        private float[] _bandHighHz;
        private float[] _bandFloor;
        private float[] _bandPeak;
        private float[] _bandSmoothed;

        private float _maxHype;
        private float _currentHype;
        private Color[] _p0HypeColors;
        private Color[] _p1HypeColors;

        private void Awake()
        {
            _fftSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_fftSize, 64, 8192));
            _spectrum = new float[_fftSize];

            _bandLowHz = new float[_numCols];
            _bandHighHz = new float[_numCols];
            _bandFloor = new float[_numCols];
            _bandPeak = new float[_numCols];
            _bandSmoothed = new float[_numCols];

            // Log-spaced band edges across [_minHz, _maxHz]
            float logMin = Mathf.Log(_minHz);
            float logMax = Mathf.Log(_maxHz);
            for (int c = 0; c < _numCols; c++)
            {
                float t0 = (float)c / _numCols;
                float t1 = (float)(c + 1) / _numCols;
                _bandLowHz[c] = Mathf.Exp(Mathf.Lerp(logMin, logMax, t0));
                _bandHighHz[c] = Mathf.Exp(Mathf.Lerp(logMin, logMax, t1));
                _bandFloor[c] = 0.00001f;
                _bandPeak[c] = 0.00002f;
            }

            // Spawn the 4x30 grid of Images under the container
            float cellWidth = _container.rect.width / _numCols;
            float cellHeight = _container.rect.height / _numRows;
            _cells = new Image[_numRows, _numCols];
            for (int row = 0; row < _numRows; row++)
            {
                for (int col = 0; col < _numCols; col++)
                {
                    GameObject cell = new GameObject("Cell");
                    cell.transform.SetParent(_container, false);
                    Image img = cell.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.color = _inactiveColor;

                    RectTransform rt = cell.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = new Vector2(cellWidth, cellHeight);
                    rt.anchoredPosition = new Vector2(col * cellWidth, -row * cellHeight);

                    _cells[row, col] = img;
                }
            }
        }

        public void Init(float maxHype, SkinConfig skin0, SkinConfig skin1)
        {
            _maxHype = maxHype;
            _currentHype = 0f;
            _p0HypeColors = ResolveHypeColors(skin0);
            _p1HypeColors = ResolveHypeColors(skin1);
        }

        public void SetHype(float hype)
        {
            _currentHype = hype;
        }

        private void Update()
        {
            if (_p0HypeColors == null || _p1HypeColors == null)
                return;

            if (_audioSource != null)
                _audioSource.GetSpectrumData(_spectrum, 0, _fftWindow);
            else
                AudioListener.GetSpectrumData(_spectrum, 0, _fftWindow);

            int sampleRate = AudioSettings.outputSampleRate;
            for (int c = 0; c < _numCols; c++)
            {
                float energy = ComputeBandEnergy(_spectrum, _bandLowHz[c], _bandHighHz[c], sampleRate);
                SmoothBand(c, energy);
            }

            int splitCol = Mathf.Clamp(
                Mathf.RoundToInt((_currentHype + _maxHype) / (2f * _maxHype) * _numCols),
                0,
                _numCols
            );

            for (int col = 0; col < _numCols; col++)
            {
                // Always keep the bottom row lit, even at silence
                int bandHeight = Mathf.Max(1, Mathf.RoundToInt(_bandSmoothed[col] * _numRows));
                int activeStartRow = _numRows - bandHeight;
                Color[] palette = (col < splitCol) ? _p0HypeColors : _p1HypeColors;

                for (int row = 0; row < _numRows; row++)
                {
                    Color color = (row >= activeStartRow) ? palette[row] : _inactiveColor;
                    _cells[row, col].color = color;
                }
            }
        }

        private void SmoothBand(int band, float energy)
        {
            _bandFloor[band] = Mathf.Lerp(_bandFloor[band], energy, 1f - Mathf.Exp(-_floorFollow * Time.deltaTime));

            _bandPeak[band] = Mathf.Max(_bandPeak[band], energy);
            _bandPeak[band] = Mathf.Lerp(
                _bandPeak[band],
                _bandFloor[band],
                1f - Mathf.Exp(-_peakDecay * Time.deltaTime)
            );

            float denom = Mathf.Max(1e-6f, _bandPeak[band] - _bandFloor[band]);
            float norm = Mathf.Clamp01((energy - _bandFloor[band]) / denom);

            if (_compress > 0f)
                norm = Mathf.Pow(norm, 1f - _compress);

            float rate = (norm > _bandSmoothed[band]) ? _attack : _release;
            _bandSmoothed[band] = Mathf.Lerp(_bandSmoothed[band], norm, 1f - Mathf.Exp(-rate * Time.deltaTime));
        }

        private static float ComputeBandEnergy(float[] spectrum, float lowHz, float highHz, int sampleRate)
        {
            float nyquist = sampleRate * 0.5f;
            lowHz = Mathf.Clamp(lowHz, 0f, nyquist);
            highHz = Mathf.Clamp(highHz, lowHz, nyquist);

            int n = spectrum.Length;
            int lo = Mathf.Clamp(Mathf.FloorToInt((lowHz / nyquist) * (n - 1)), 0, n - 1);
            int hi = Mathf.Clamp(Mathf.CeilToInt((highHz / nyquist) * (n - 1)), 0, n - 1);

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

        private Color[] ResolveHypeColors(SkinConfig skin)
        {
            if (skin.HypeBarColors != null && skin.HypeBarColors.Length == _numRows)
                return skin.HypeBarColors;

            // Fallback: derive from existing skin colors when HypeBarColors isn't configured
            Color[] fallback = new Color[_numRows];
            for (int i = 0; i < _numRows; i++)
            {
                fallback[i] = i switch
                {
                    0 => skin.AccentColor,
                    1 => skin.LightColor,
                    _ => skin.MainColor,
                };
            }
            return fallback;
        }
    }
}
