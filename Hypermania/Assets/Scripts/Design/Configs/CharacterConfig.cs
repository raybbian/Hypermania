using System;
using System.Collections.Generic;
using Design.Animation;
using Game;
using Game.View.Events;
using Game.View.Events.Vfx;
using Game.View.Fighters;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Configs
{
    [Serializable]
    public struct SuperDisplayConfig
    {
        public CharacterState AnimState;
        public int StartFrame;
        public Vector3 CameraLocalPosition;
        public sfloat CameraOrthoSize;
    }

    [Serializable]
    public struct SkinConfig
    {
        public Color MainColor;
        public Color LightColor;
        public Color AccentColor;
        public SpriteLibraryAsset SpriteLibrary;
        public Texture2D Portrait;
        public Texture2D Splash;
    }

    [CreateAssetMenu(menuName = "Hypermania/Character Config")]
    public class CharacterConfig : ScriptableObject
    {
        public Character Character;
        public FighterView Prefab;
        public FighterMoveSfx MoveSfx;
        public SkinConfig[] Skins;
        public AnimatorOverrideController AnimationController;
        public sfloat CharacterHeight;
        public sfloat ForwardSpeed;
        public sfloat BackSpeed;
        public sfloat JumpVelocity;
        public sfloat Health;
        public sfloat SuperMax;
        public sfloat BurstMax;
        public sfloat ForwardDashDistance;
        public sfloat BackDashDistance;
        public int NumAirDashes;
        public sfloat ForwardAirDashDistance;
        public sfloat BackAirDashDistance;
        public SuperDisplayConfig SuperDisplay;
        public EnumArray<CharacterState, HitboxData> Hitboxes;
        public List<ComboConfig> Combos;
        public List<ProjectileConfig> Projectiles;

        public FrameData GetFrameData(CharacterState anim, int tick)
        {
            HitboxData data = GetHitboxData(anim);
            if (data == null || data.TotalTicks == 0)
            {
                return new FrameData();
            }
            // By default loop the animation, but this should never happen because we would have switched to a different
            // state in the fighter state for ones that should not loop
            tick = ((tick % data.TotalTicks) + data.TotalTicks) % data.TotalTicks;
            return data.Frames[tick];
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
