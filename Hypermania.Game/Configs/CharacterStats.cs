using System;
using System.Collections.Generic;
using Hypermania.Shared;
using Hypermania.Shared.SoftFloat;
using MemoryPack;

namespace Hypermania.Game.Configs
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
    [MemoryPackable]
    public partial class CharacterStats
    {
        [MemoryPackAllowSerialize]
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
        public List<GatlingEntry> Gatlings;
        public List<ProjectileStats> Projectiles;

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
    }
}
