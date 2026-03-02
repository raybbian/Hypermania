using Design.Animation;
using Game;
using Game.View.Fighters;
using UnityEngine;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Configs
{
    [CreateAssetMenu(menuName = "Hypermania/Character Config")]
    public class CharacterConfig : ScriptableObject
    {
        public Character Character;
        public FighterView Prefab;
        public AnimatorOverrideController AnimationController;
        public sfloat CharacterHeight;
        public sfloat ForwardSpeed;
        public sfloat BackSpeed;
        public sfloat JumpVelocity;
        public sfloat Health;
        public sfloat BurstMax;
        public sfloat ForwardDashDistance;
        public sfloat BackDashDistance;
        public int NumAirDashes;
        public sfloat ForwardAirDashDistance;
        public sfloat BackAirDashDistance;
        public EnumArray<CharacterState, HitboxData> Hitboxes;

        public FrameData GetFrameData(CharacterState anim, int tick)
        {
            HitboxData data = GetHitboxData(anim);
            // By default loop the animation, but this should never happen because we would have switched to a different
            // state in the fighter state for ones that should not loop
            tick = ((tick % data.TotalTicks) + data.TotalTicks) % data.TotalTicks;
            return data.Frames[tick];
        }

        public bool AnimLoops(CharacterState anim)
        {
            return GetHitboxData(anim).Clip.isLooping;
        }

        public HitboxData GetHitboxData(CharacterState anim)
        {
            if (Hitboxes[anim] == null)
            {
                // if there is no hitbox data here, just do idle for testing
                return Hitboxes[CharacterState.Idle];
            }
            return Hitboxes[anim];
        }
    }
}
