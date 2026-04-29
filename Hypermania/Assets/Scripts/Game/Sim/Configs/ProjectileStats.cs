using UnityEngine;
using Utils.SoftFloat;
using Game.Sim;

namespace Game.Sim.Configs
{
    // Deterministic projectile data. The companion ProjectilePresentation SO
    // (view side) holds the spawned prefab. The sim's ProjectileState only
    // reads from ProjectileStats.
    [CreateAssetMenu(menuName = "Hypermania/Sim/Projectile Stats")]
    public class ProjectileStats : ScriptableObject
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
