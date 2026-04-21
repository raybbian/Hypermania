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
        public Color[] HypeBarColors;
        public SpriteLibraryAsset SpriteLibrary;
        public Texture2D Portrait;
        public Texture2D Splash;
    }

    [Serializable]
    public struct GatlingEntry
    {
        public CharacterState From;
        public CharacterState To;
    }

    [CreateAssetMenu(menuName = "Hypermania/Character Config")]
    public class CharacterConfig : ScriptableObject
    {
        public Character Character;
        public bool Enabled = true;
        public FighterView Prefab;
        public FighterMoveSfx MoveSfx;
        public SkinConfig[] Skins;
        public AnimatorOverrideController AnimationController;
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
        public SuperDisplayConfig SuperDisplay;
        public EnumArray<CharacterState, HitboxData> Hitboxes;
        public List<GatlingEntry> Gatlings;
        public List<ProjectileConfig> Projectiles;

        public bool HasGatling(CharacterState from, CharacterState to)
        {
            if (Gatlings == null)
                return false;
            for (int i = 0; i < Gatlings.Count; i++)
            {
                if (Gatlings[i].From == from && Gatlings[i].To == to)
                    return true;
            }
            return false;
        }

        private void OnEnable()
        {
            ValidateGatlings();
        }

        private void ValidateGatlings()
        {
            if (Gatlings == null)
                return;
            for (int i = 0; i < Gatlings.Count; i++)
            {
                GatlingEntry entry = Gatlings[i];
                HitboxData fromData = Hitboxes != null ? Hitboxes[entry.From] : null;
                HitboxData toData = Hitboxes != null ? Hitboxes[entry.To] : null;
                if (fromData == null || toData == null)
                    continue;

                int fromTotal = fromData.StartupTicks + fromData.ActiveTicks + fromData.RecoveryTicks;
                if (fromTotal == 0)
                    continue;
                if (toData.StartupTicks == 0)
                    continue;

                int cancelWindow = Mathsf.Max(0, toData.StartupTicks - fromData.OnHitAdvantage + 1);
                int overlap = cancelWindow - fromData.RecoveryTicks;
                if (overlap > 0)
                {
                    Debug.LogWarning(
                        $"[Gatling] {Character} {entry.From}->{entry.To}: cancel window "
                            + $"({cancelWindow}f) exceeds {entry.From} recovery "
                            + $"({fromData.RecoveryTicks}f) by {overlap} frame(s) — "
                            + $"window clamped to recovery phase, opponent escapes hitstun."
                    );
                }
            }
        }

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
