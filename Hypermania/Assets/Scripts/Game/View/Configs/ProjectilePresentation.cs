using Game.View.Projectiles;
using UnityEngine;
using Hypermania.Game.Configs;
using Hypermania.Game;
using Hypermania.Shared;

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
