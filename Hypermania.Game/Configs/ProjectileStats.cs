using Hypermania.Shared.SoftFloat;
using MemoryPack;

namespace Hypermania.Game.Configs
{
    [MemoryPackable]
    public partial class ProjectileStats
    {
        public CharacterState TriggerState;
        public int SpawnTick;
        public HitboxData HitboxData;
        public SVector2 SpawnOffset;
        public SVector2 Velocity;
        public int LifetimeTicks;
        public bool Unique;
        public bool DieOnContact;
        public bool Lingers;
        public bool HasOnDeath;
        public HitboxData OnDeathHitbox;
    }
}
