using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
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

        // How many frames every effect gets drawn for while prewarming.
        [SerializeField]
        private int _prewarmFrames = 12;

        // Sped up so the later parts of an effect get drawn in those frames too.
        [SerializeField]
        private float _prewarmPlayRate = 8f;

        // Plays every effect once so its shaders are built up front. The
        // first use of an effect otherwise stalls the game while they
        // compile. The effects really are drawn, so the position has to be
        // on camera, and this wants to run behind the load screen.
        public IEnumerator Prewarm(Vector2 position)
        {
            List<VfxEffect> effects = new List<VfxEffect>();
            foreach (VfxKind kind in Enum.GetValues(typeof(VfxKind)))
            {
                if (_vfxLibrary.Library[kind].Effect == null)
                    continue;

                VfxEvent ev = new VfxEvent
                {
                    Kind = kind,
                    Position = position,
                    Direction = Vector2.right,
                };
                VfxEffect effect = OnStartEffect(new ViewEvent<VfxEvent> { Event = ev });
                foreach (VisualEffect graph in effect.GetComponentsInChildren<VisualEffect>())
                {
                    graph.playRate = _prewarmPlayRate;
                }
                effects.Add(effect);
            }

            for (int i = 0; i < _prewarmFrames; i++)
            {
                yield return null;
            }

            // The scene can be gone by now if the match was left right away.
            foreach (VfxEffect effect in effects)
            {
                if (effect != null)
                    OnEndEffect(effect);
            }
        }

        public override VfxEffect OnStartEffect(ViewEvent<VfxEvent> ev)
        {
            // worldPositionStays: false so the spawned VFX starts at local
            // (0,0,0) under the VfxManager. StartEffect then only overwrites
            // world x/y, leaving local z = 0 so the effect sits on the
            // VfxManager parent's z plane (in front of the player) rather
            // than wherever the prefab was saved in world space.
            GameObject vfx = Instantiate(_vfxLibrary.Library[ev.Event.Kind].Effect, transform, false);
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
