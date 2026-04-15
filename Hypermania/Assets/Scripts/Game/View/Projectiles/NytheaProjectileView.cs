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

            float normalizedTime = _config.HitboxData.GetAnimNormalizedTime(simFrame - state.CreationFrame);
            _animator.Play(_animStateName, 0, normalizedTime);
            _animator.Update(0f);
        }
    }
}
