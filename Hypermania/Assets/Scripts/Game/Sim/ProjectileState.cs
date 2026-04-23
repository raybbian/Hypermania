using Design.Animation;
using Design.Configs;
using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    [MemoryPackable]
    public partial struct ProjectileState
    {
        public bool Active;
        public int Owner;
        public SVector2 Position;
        public SVector2 Velocity;
        public Frame CreationFrame;
        public int LifetimeTicks;
        public FighterFacing FacingDir;
        public bool MarkedForDestroy;
        public int ConfigIndex;
        public bool IsDying;
        public Frame DeathFrame;

        /// <summary>
        /// Advances this projectile by one tick: applies gravity/friction, checks
        /// lifetime/bounds/destruction, updates position. When HasOnDeath is set
        /// and the projectile would despawn (lifetime expired or hit), transitions
        /// into a dying state that plays out the OnDeathHitbox frames instead of
        /// immediately deactivating. Sets Active = false once fully done.
        /// </summary>
        public void Advance(Frame simFrame, GameOptions options, ProjectileConfig config)
        {
            if (!Active)
                return;

            if (IsDying)
            {
                int onDeathDuration =
                    config != null && config.OnDeathHitbox != null ? config.OnDeathHitbox.TotalTicks : 0;
                if (simFrame - DeathFrame >= onDeathDuration)
                {
                    Active = false;
                }
                return;
            }

            if (MarkedForDestroy)
            {
                if (TryBeginDeath(simFrame, config))
                    return;
                Active = false;
                return;
            }

            int age = simFrame - CreationFrame;
            if (age >= LifetimeTicks)
            {
                if (TryBeginDeath(simFrame, config))
                    return;
                Active = false;
                return;
            }

            FrameData curFrame = config?.HitboxData?.GetFrame(age);
            if (curFrame != null && curFrame.GravityEnabled && Position.y > options.Global.GroundY)
            {
                Velocity.y += options.Global.Gravity * 1 / GameManager.TPS;
            }

            Position += Velocity * 1 / GameManager.TPS;

            if (Position.y <= options.Global.GroundY)
            {
                Position.y = options.Global.GroundY;
                if (Velocity.y < sfloat.Zero)
                    Velocity.y = sfloat.Zero;

                if (config != null && config.DieOnContact && !config.Lingers)
                {
                    MarkedForDestroy = true;
                }
            }

            if (Position.x > options.Global.WallsX + 2 || Position.x < -options.Global.WallsX - 2)
            {
                Active = false;
            }
        }

        private bool TryBeginDeath(Frame simFrame, ProjectileConfig config)
        {
            if (config == null || !config.HasOnDeath || config.OnDeathHitbox == null)
                return false;

            IsDying = true;
            DeathFrame = simFrame;
            MarkedForDestroy = false;
            Velocity = SVector2.zero;
            return true;
        }

        /// <summary>
        /// Adds this projectile's hitboxes to the physics context for collision detection.
        /// While dying, uses the config's OnDeathHitbox ticked from DeathFrame; otherwise
        /// uses the normal HitboxData ticked from CreationFrame.
        /// </summary>
        public void AddBoxes(Frame simFrame, ProjectileConfig config, Physics<BoxProps> physics, int projectileIndex)
        {
            if (!Active || config == null)
                return;

            HitboxData data;
            int tick;
            if (IsDying)
            {
                data = config.OnDeathHitbox;
                tick = simFrame - DeathFrame;
            }
            else
            {
                data = config.HitboxData;
                tick = simFrame - CreationFrame;
            }

            if (data == null)
                return;

            FrameData frameData = data.GetFrame(tick);
            if (frameData == null)
                return;

            foreach (var box in frameData.Boxes)
            {
                SVector2 centerLocal = box.CenterLocal;
                if (FacingDir == FighterFacing.Left)
                {
                    centerLocal.x *= -1;
                }

                SVector2 centerWorld = Position + centerLocal;
                BoxProps newProps = box.Props;
                if (FacingDir == FighterFacing.Left)
                {
                    newProps.Knockback.x *= -1;
                }

                physics.AddBox(Owner, centerWorld, box.SizeLocal, newProps, projectileIndex, data.IgnoreOwner);
            }
        }
    }
}
