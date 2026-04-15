using UnityEngine;
using UnityEngine.VFX;

namespace Game.View.Events.Vfx
{
    [RequireComponent(typeof(VisualEffect))]
    public class ParticleVfxEffect : VfxEffect
    {
        [SerializeField]
        private bool _flipXWithDirection;

        private VisualEffect _vfx;

        public override void StartEffect(ViewEvent<VfxEvent> ev)
        {
            transform.position = new Vector3(ev.Event.Position.x, ev.Event.Position.y, transform.position.z);

            if (_flipXWithDirection && ev.Event.Direction.x < 0)
            {
                var s = transform.localScale;
                transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
            }

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
