using UnityEngine;
using UnityEngine.VFX;

namespace Game.View.Events.Vfx
{
    [RequireComponent(typeof(VisualEffect))]
    public class DashDushEffect : VfxEffect
    {
        private VisualEffect _vfx;

        public override void StartEffect(ViewEvent<VfxEvent> ev)
        {
            transform.position = new Vector3(ev.Event.Position.x, ev.Event.Position.y, transform.position.z);
            transform.localScale =
                ev.Event.Direction.x < 0
                    ? new Vector3(-transform.localScale.x, transform.localScale.y, transform.localScale.z)
                    : transform.localScale;
            _vfx = GetComponent<VisualEffect>();
            _vfx.Play();
        }

        public override void EndEffect()
        {
            _vfx.Stop();
        }

        public override bool EffectIsFinished()
        {
            return _vfx.aliveParticleCount == 0;
        }
    }
}
