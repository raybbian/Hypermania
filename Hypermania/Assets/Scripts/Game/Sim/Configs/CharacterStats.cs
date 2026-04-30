using System;
using System.Collections.Generic;
using Game.Sim;
using MemoryPack;
using UnityEngine;
using Utils;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Game.Sim.Configs
{
    [Serializable]
    [MemoryPackable]
    public partial struct GatlingEntry
    {
        public CharacterState From;
        public CharacterState To;
    }

    // Deterministic character data. The companion CharacterPresentation SO
    // (view side) holds the prefab, skins, animation controller, sfx, and
    // super-display camera layout for the same character. Sim consumers only
    // ever reach for CharacterStats - the trainer needs nothing else.
    [CreateAssetMenu(menuName = "Hypermania/Sim/Character Stats")]
    [MemoryPackable]
    public partial class CharacterStats : ScriptableObject
    {
        public Character Character;
        public bool Enabled = true;
        public sfloat CharacterHeight;
        public sfloat ForwardSpeed;
        public sfloat BackSpeed;
        public sfloat JumpVelocity;
        public sfloat Health;
        public sfloat BurstMax;
        public sfloat ForwardDashDistance;
        public sfloat BackDashDistance;
        public int BackDashRecoveryTicks = 2;
        public int NumAirDashes;
        public sfloat ForwardAirDashDistance;
        public sfloat BackAirDashDistance;
        public EnumArray<CharacterState, HitboxData> Hitboxes;
        public List<GatlingEntry> Gatlings;
        public List<ProjectileStats> Projectiles;

        [MemoryPackIgnore]
        public new string name
        {
            get => base.name;
            set => base.name = value;
        }

        [MemoryPackIgnore]
        public new HideFlags hideFlags
        {
            get => base.hideFlags;
            set => base.hideFlags = value;
        }

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

        public FrameData GetFrameData(CharacterState anim, int tick)
        {
            HitboxData data = GetHitboxData(anim);
            if (data == null || data.TotalTicks == 0)
            {
                return new FrameData();
            }
            if (data.AnimLoops)
            {
                tick = ((tick % data.TotalTicks) + data.TotalTicks) % data.TotalTicks;
            }
            else
            {
                tick = Mathsf.Clamp(tick, 0, data.TotalTicks - 1);
            }
            return data.Frames[tick];
        }

        public HitboxData GetHitboxData(CharacterState anim)
        {
            if (Hitboxes[anim] == null)
            {
                return Hitboxes[CharacterState.Idle];
            }
            return Hitboxes[anim];
        }
    }
}
