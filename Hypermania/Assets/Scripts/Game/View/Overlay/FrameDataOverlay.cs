using Design.Animation;
using Design.Configs;
using Game.Sim;
using TMPro;
using UnityEngine;
using Utils;

namespace Game.View.Overlay
{
    public class FrameDataOverlay : MonoBehaviour
    {
        [SerializeField]
        private GameObject _cellsObject;

        [SerializeField]
        private GameObject _overlaysObject;

        [SerializeField]
        private GameObject _curFrameBar;

        [SerializeField]
        private GameObject _cellPrefab;

        [SerializeField]
        private GameObject _fontPrefab;

        [SerializeField]
        private int _numColumns;

        private float _cellWidth;
        private float _cellHeight;
        private FrameDataCell[,] _cells;
        private TextMeshProUGUI[,] _consecText;
        private int[,] _consecCount;

        public void Awake()
        {
            int numRows = 3;
            RectTransform rect = _cellsObject.GetComponent<RectTransform>();
            _cellWidth = rect.rect.width / _numColumns;
            _cellHeight = rect.rect.height / numRows;
            _cells = new FrameDataCell[numRows, _numColumns];
            _consecText = new TextMeshProUGUI[2, _numColumns];
            _consecCount = new int[2, _numColumns];

            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < _numColumns; j++)
                {
                    GameObject cell = Instantiate(_cellPrefab, _cellsObject.transform, false);

                    RectTransform cellRect = cell.GetComponent<RectTransform>();

                    cellRect.anchorMin = new Vector2(0f, 1f);
                    cellRect.anchorMax = new Vector2(0f, 1f);
                    cellRect.pivot = new Vector2(0f, 1f);

                    cellRect.sizeDelta = new Vector2(_cellWidth, _cellHeight);

                    float x = j * _cellWidth;
                    float y = -i * _cellHeight;
                    cellRect.anchoredPosition = new Vector2(x, y);

                    _cells[i, j] = cell.GetComponent<FrameDataCell>();
                    _cells[i, j].SetType(FrameType.Neutral);
                }
            }

            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < _numColumns; j++)
                {
                    float x = j * _cellWidth;
                    float y = -i * _cellHeight;

                    GameObject textObject = Instantiate(_fontPrefab, _overlaysObject.transform, false);
                    textObject.SetActive(false);

                    RectTransform textRect = textObject.GetComponent<RectTransform>();
                    textRect.anchorMin = new Vector2(0f, 1f);
                    textRect.anchorMax = new Vector2(0f, 1f);
                    textRect.pivot = new Vector2(0.5f, 1f);

                    textRect.sizeDelta = new Vector2(_cellWidth * 4, _cellHeight);
                    textRect.anchoredPosition = new Vector2(x + _cellWidth, y);

                    _consecText[i, j] = textObject.GetComponent<TextMeshProUGUI>();
                    _consecText[i, j].SetText("");

                    _consecCount[i, j] = 0;
                }
            }
        }

        public void AddFrameData(in GameState state, CharacterConfig[] characterConfigs, AudioConfig audioConfig)
        {
            if (state.SimFrame == Frame.NullFrame)
            {
                return;
            }
            int baseIdx = state.SimFrame.No % _numColumns;
            for (int i = 0; i < 2; i++)
            {
                _cells[i, baseIdx].SetType(state.SimFrame, state.Fighters[i], characterConfigs[i]);
                _consecText[i, baseIdx].gameObject.SetActive(false);
                int prevIdx = (baseIdx + _numColumns - 1) % _numColumns;

                if (_cells[i, baseIdx].CurType == _cells[i, prevIdx].CurType)
                    _consecCount[i, baseIdx] = _consecCount[i, prevIdx] + 1;
                else
                {
                    _consecCount[i, baseIdx] = 1;
                    if (_cells[i, prevIdx].CurType != FrameType.Neutral)
                    {
                        _consecText[i, prevIdx].gameObject.SetActive(true);
                        _consecText[i, prevIdx].SetText(_consecCount[i, prevIdx].ToString());
                    }
                }
            }

            _curFrameBar.GetComponent<RectTransform>().anchoredPosition = new Vector2((baseIdx + 1) * _cellWidth, 0f);

            if (audioConfig.BeatWithinWindow(state.SimFrame, AudioConfig.BeatSubdivision.QuarterNote, 0))
            {
                _cells[2, baseIdx].SetType(FrameType.Active);
            }
            else
            {
                _cells[2, baseIdx].SetType(FrameType.Neutral);
            }
        }
    }
}
