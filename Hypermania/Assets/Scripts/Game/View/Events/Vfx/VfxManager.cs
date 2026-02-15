using System;
using System.Collections;
using UnityEngine;

namespace Game.View.Events.Vfx
{
    public struct VfxEvent : IEquatable<VfxEvent>
    {
        public VfxKind Kind;
        public Vector2 Position;
        public Vector2 KnockbackVector;

        public bool Equals(VfxEvent other)
        {
            return Kind == other.Kind;
        }
    }

    // TODO: should create vfxeffect base component that determines how to start/stop/play each effect, and then call
    // that here
    public class VfxManager : ViewEventManager<VfxEvent, VfxEffect>
    {
        [SerializeField]
        private VfxLibrary _vfxLibrary;

        public override VfxEffect OnStartEffect(ViewEvent<VfxEvent> ev)
        {
            GameObject vfx = Instantiate(_vfxLibrary.Library[ev.Event.Kind].Effect);
            vfx.transform.SetParent(transform);
            VfxEffect effect = vfx.GetComponent<VfxEffect>();
            if (effect == null)
            {
                throw new InvalidOperationException(
                    "All vfx prefabs in the vfx library must have a VfxEffect component!"
                );
            }
            effect.StartEffect(ev);
            return effect;
        }

        public override void OnEndEffect(VfxEffect effect)
        {
            effect.EndEffect();
            Destroy(effect.gameObject);
        }

        public override bool EffectIsFinished(VfxEffect effect)
        {
            return effect.EffectIsFinished();
        }
    }
}
