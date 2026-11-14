using Design.Configs;
using UnityEngine;

namespace Game.View.Overlay
{
    public enum VictoryKind
    {
        Empty,
        Normal,
        Perfect,
    }

    // A player's row of round wins, one mark per round they can take. The
    // layout group on this object places the marks.
    public class VictoryMarkView : MonoBehaviour
    {
        [SerializeField]
        private VictoryMark _markPrefab;

        private Texture _portrait;
        private Color _outline;
        private VictoryMark[] _marks;

        public void Init(CharacterConfig config, int skinIndex)
        {
            _portrait = config.Skins[skinIndex].Portrait;
            // Same colour as the rim of the disc by their health bar.
            _outline = config.Skins[skinIndex].AccentColor;
            ClearMarks();
        }

        public void SetVictories(VictoryKind[] victories)
        {
            if (_marks == null || _marks.Length != victories.Length)
            {
                CreateMarks(victories.Length);
            }

            for (int i = 0; i < victories.Length; i++)
            {
                _marks[i].Show(victories[i]);
            }
        }

        private void CreateMarks(int count)
        {
            ClearMarks();
            _marks = new VictoryMark[count];
            for (int i = 0; i < count; i++)
            {
                _marks[i] = Instantiate(_markPrefab, transform);
                _marks[i].Init(_portrait, _outline);
            }
        }

        private void ClearMarks()
        {
            if (_marks == null)
                return;

            foreach (VictoryMark mark in _marks)
            {
                Destroy(mark.gameObject);
            }
            _marks = null;
        }
    }
}
