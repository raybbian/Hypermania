using Game.Sim.Configs;
using Game.View.Projectiles;
using UnityEngine;

namespace Game.View.Configs
{
    // View-side counterpart of ProjectileStats: holds the spawn prefab. The
    // sim's ProjectileState only reads ProjectileStats.
    [CreateAssetMenu(menuName = "Hypermania/View/Projectile Presentation")]
    public class ProjectilePresentation : ScriptableObject
    {
        public ProjectileStats Stats;
        public ProjectileView Prefab;
    }
}
