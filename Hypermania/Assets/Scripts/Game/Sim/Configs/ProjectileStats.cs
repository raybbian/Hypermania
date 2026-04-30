using Game.Sim;
using MemoryPack;
using UnityEngine;
using Utils.SoftFloat;

namespace Game.Sim.Configs
{
    // Deterministic projectile data. The companion ProjectilePresentation SO
    // (view side) holds the spawned prefab. The sim's ProjectileState only
    // reads from ProjectileStats.
    [CreateAssetMenu(menuName = "Hypermania/Sim/Projectile Stats")]
    [MemoryPackable]
    public partial class ProjectileStats : ScriptableObject
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
    }
}
