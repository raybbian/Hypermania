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

        /// <summary>
        /// Advances this projectile by one tick: checks lifetime/bounds, updates position.
        /// Sets Active = false if the projectile should despawn.
        /// </summary>
        public void Advance(Frame simFrame, sfloat wallsX)
        {
            if (!Active)
                return;

            if (MarkedForDestroy)
            {
                Active = false;
                return;
            }

            int age = simFrame - CreationFrame;
            if (age >= LifetimeTicks)
            {
                Active = false;
                return;
            }

            Position += Velocity * 1 / GameManager.TPS;

            if (Position.x > wallsX + 2 || Position.x < -wallsX - 2)
            {
                Active = false;
            }
        }

        /// <summary>
        /// Adds this projectile's hitboxes to the physics context for collision detection.
        /// </summary>
        public void AddBoxes(Frame simFrame, ProjectileConfig config, Physics<BoxProps> physics, int projectileIndex)
        {
            if (!Active)
                return;

            if (config?.HitboxData == null)
                return;

            int tick = simFrame - CreationFrame;
            FrameData frameData = config.HitboxData.GetFrame(tick);
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

                physics.AddBox(Owner, centerWorld, box.SizeLocal, newProps, projectileIndex);
            }
        }
    }
}
