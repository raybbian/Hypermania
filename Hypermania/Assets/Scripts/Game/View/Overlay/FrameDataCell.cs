using Design.Animation;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
using Utils;
using Utils.EnumArray;

namespace Game.View.Overlay
{
    public class FrameDataCell : MonoBehaviour
    {
        [SerializeField]
        private EnumArray<FrameType, Color> _cellColors;

        [SerializeField]
        private Image _cellBackground;

        public FrameType CurType { get; private set; } = FrameType.Neutral;

        public void SetType(Frame frame, in FighterState state, CharacterConfig characterConfig)
        {
            FrameData data = characterConfig.GetHitboxData(state.State).GetFrame(frame - state.StateStart);
            FrameType res = data == null ? FrameType.Neutral : data.FrameType;
            SetType(res);
        }

        public void SetType(FrameType type)
        {
            CurType = type;
            _cellBackground.color = _cellColors[type];
        }
    }
}
