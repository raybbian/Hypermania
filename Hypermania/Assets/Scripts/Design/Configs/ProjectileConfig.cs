using Design.Animation;
using Game;
using Game.View.Projectiles;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Configs
{
    [CreateAssetMenu(menuName = "Hypermania/Projectile Config")]
    public class ProjectileConfig : ScriptableObject
    {
        public CharacterState TriggerState;
        public int SpawnTick;
        public ProjectileView Prefab;
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
