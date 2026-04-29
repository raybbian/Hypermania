using Game.Sim;
using Game.Sim.Configs;
using UnityEngine;
using Utils;

namespace Game.View.Projectiles
{
    public class SimpleProjectileView : ProjectileView
    {
        public override void Render(Frame simFrame, in ProjectileState state, ProjectileStats stats)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;
            transform.position = pos;

            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            if (stats == null)
                return;

            HitboxData data = state.IsDying ? stats.OnDeathHitbox : stats.HitboxData;
            int tick = state.IsDying ? simFrame - state.DeathFrame : simFrame - state.CreationFrame;

            if (data == null || data.TotalTicks == 0 || string.IsNullOrEmpty(data.ClipName))
                return;

            int animTick = data.AnimLoops ? tick % data.TotalTicks : Mathf.Min(tick, data.TotalTicks - 1);
            float normalizedTime = (float)animTick / (data.TotalTicks - 1);
            _animator.Play(data.ClipName, 0, normalizedTime);
            _animator.Update(0f);
        }
    }
}
