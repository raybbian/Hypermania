using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Utils;
using Utils.EnumArray;
using Hypermania.Game;
using Hypermania.Game.Configs;
using Hypermania.Shared;

namespace Game.View.Overlay
{
    public class FrameDataCell : MonoBehaviour
    {
        [SerializeField]
        private EnumArray<FrameType, Color> _cellColors;

        [SerializeField]
        private Image _cellBackground;

        public FrameType CurType { get; private set; } = FrameType.Neutral;

        public void SetType(Frame frame, in FighterState state, EnumArray<CharacterState, HitboxData> hitboxes)
        {
            FrameData data = LookupHitbox(hitboxes, state.State).GetFrame(frame - state.StateStart);
            FrameType res = data == null ? FrameType.Neutral : data.FrameType;
            SetType(res);
        }

        public void SetType(Frame frame, CharacterState animState, Frame stateStart, EnumArray<CharacterState, HitboxData> hitboxes)
        {
            FrameData data = LookupHitbox(hitboxes, animState).GetFrame(frame - stateStart);
            FrameType res = data == null ? FrameType.Neutral : data.FrameType;
            SetType(res);
        }

        private static HitboxData LookupHitbox(EnumArray<CharacterState, HitboxData> hitboxes, CharacterState anim)
        {
            return hitboxes[anim] ?? hitboxes[CharacterState.Idle];
        }

        public void SetType(FrameType type)
        {
            CurType = type;
            _cellBackground.color = _cellColors[type];
        }
    }
}
