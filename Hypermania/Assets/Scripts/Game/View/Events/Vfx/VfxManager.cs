using System;
using System.Collections;
using UnityEngine;
using Utils;

namespace Game.View.Events.Vfx
{
    public struct VfxEvent : IEquatable<VfxEvent>
    {
        public VfxKind Kind;
        public Vector2 Position;
        public Vector2 Direction;

        public bool Equals(VfxEvent other)
        {
            return Kind == other.Kind;
        }
    }

    public class VfxManager : ViewEventManager<VfxEvent, VfxEffect>
    {
        [SerializeField]
        private VfxLibrary _vfxLibrary;

        public override VfxEffect OnStartEffect(ViewEvent<VfxEvent> ev)
        {
            GameObject vfx = Instantiate(_vfxLibrary.Library[ev.Event.Kind].Effect, transform, true);
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

        public void AddDesired(
            VfxKind kind,
            Frame frame,
            int hash = 0,
            Vector2 position = default,
            Vector2 direction = default
        )
        {
            AddDesired(
                new ViewEvent<VfxEvent>
                {
                    Event = new VfxEvent
                    {
                        Kind = kind,
                        Position = position,
                        Direction = direction,
                    },
                    StartFrame = frame,
                    Hash = hash,
                }
            );
        }

        public override bool EffectIsFinished(VfxEffect effect)
        {
            return effect.EffectIsFinished();
        }
    }
}
