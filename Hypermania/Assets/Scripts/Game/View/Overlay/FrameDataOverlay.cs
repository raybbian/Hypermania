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
        private bool _displayHitstopFrames = true;

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

        public void AddFrameData(in GameState state, GameOptions options)
        {
            if (state.SimFrame == Frame.NullFrame)
            {
                return;
            }

            Frame frame = _displayHitstopFrames ? state.RealFrame : state.SimFrame;
            int baseIdx = frame.No % _numColumns;
            for (int i = 0; i < 2; i++)
            {
                if (state.Fighters[i].PostActionState.HasValue)
                {
                    CharacterState displayState = state.Fighters[i].PostActionState.Value;
                    Frame displayStateStart = state.Fighters[i].PostActionStateStart.Value;

                    if (displayState == CharacterState.Grabbed)
                    {
                        _cells[i, baseIdx].SetType(FrameType.Grabbed);
                    }
                    else
                    {
                        _cells[i, baseIdx]
                            .SetType(state.SimFrame, displayState, displayStateStart, options.Players[i].Character);
                    }
                }
                else if (_displayHitstopFrames && state.HitstopFramesRemaining > 0)
                {
                    _cells[i, baseIdx].SetType(FrameType.Hitstop);
                }
                else if (state.Fighters[i].State == CharacterState.Grabbed)
                {
                    _cells[i, baseIdx].SetType(FrameType.Grabbed);
                }
                else
                {
                    _cells[i, baseIdx].SetType(state.SimFrame, state.Fighters[i], options.Players[i].Character);
                }
                _consecText[i, baseIdx].gameObject.SetActive(false);
                int prevIdx = (baseIdx + _numColumns - 1) % _numColumns;

                if (_cells[i, baseIdx].CurType == _cells[i, prevIdx].CurType)
                    _consecCount[i, baseIdx] = _consecCount[i, prevIdx] + 1;
                else
                {
                    _consecCount[i, baseIdx] = 1;
                    if (_cells[i, prevIdx].CurType != FrameType.Neutral && _consecCount[i, prevIdx] > 1)
                    {
                        _consecText[i, prevIdx].gameObject.SetActive(true);
                        _consecText[i, prevIdx].SetText(_consecCount[i, prevIdx].ToString());
                    }
                }
            }

            _curFrameBar.GetComponent<RectTransform>().anchoredPosition = new Vector2((baseIdx + 1) * _cellWidth, 0f);

            _cells[2, baseIdx].SetType(InActiveManiaHitWindow(state) ? FrameType.Active : FrameType.Neutral);
        }

        private static bool InActiveManiaHitWindow(in GameState state)
        {
            for (int p = 0; p < state.Manias.Length; p++)
            {
                if (!state.Manias[p].Enabled(state.RealFrame))
                    continue;

                int halfRange = state.Manias[p].Config.HitHalfRange;
                ManiaNoteChannel[] channels = state.Manias[p].Channels;
                for (int c = 0; c < channels.Length; c++)
                {
                    Deque<ManiaNote> notes = channels[c].Notes;
                    for (int n = 0; n < notes.Count; n++)
                    {
                        int delta = state.RealFrame - notes[n].Tick;
                        if (delta >= -halfRange && delta <= halfRange)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
