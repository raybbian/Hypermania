using Design.Configs;
using Game.Sim;
using UnityEngine;
using Utils;

namespace Game.View.Projectiles
{
    public class NytheaProjectileView : ProjectileView
    {
        [SerializeField]
        private ProjectileConfig _config;

        [SerializeField]
        private string _animStateName;

        public override void Render(Frame simFrame, in ProjectileState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;
            transform.position = pos;

            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            int ticks = simFrame - state.CreationFrame;
            int totalTicks = _config.HitboxData.TotalTicks;
            if (_config.HitboxData.Clip.isLooping)
            {
                ticks %= totalTicks;
            }
            else
            {
                ticks = Mathf.Min(ticks, totalTicks - 1);
            }

            _animator.Play(_animStateName, 0, (float)ticks / (totalTicks - 1));
            _animator.Update(0f);
        }
    }
}
