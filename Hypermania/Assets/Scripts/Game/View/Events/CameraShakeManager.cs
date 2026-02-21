using System;
using System.Collections;
using UnityEngine;

namespace Game.View.Events
{
    public struct CameraShakeEvent : IEquatable<CameraShakeEvent>
    {
        public float Strength;
        public float Frequency;
        public int NumBounces;
        public Vector2 KnockbackVector;

        public bool Equals(CameraShakeEvent other)
        {
            return Strength == other.Strength && Frequency == other.Frequency && NumBounces == other.NumBounces;
        }
    }

    public class CameraShakeManager : ViewEventManager<CameraShakeEvent, CameraShake>
    {
        [SerializeField]
        private Camera _camera;

        public override CameraShake OnStartEffect(ViewEvent<CameraShakeEvent> ev)
        {
            CameraShake camShake = _camera.gameObject.AddComponent<CameraShake>();
            camShake.Init(
                new CameraShake.Params
                {
                    PositionStrength = ev.Event.Strength,
                    Freq = ev.Event.Frequency,
                    NumBounces = ev.Event.NumBounces,
                    Randomness = 0.5f,
                },
                ev.Event.KnockbackVector
            );
            return camShake;
        }

        public override void OnEndEffect(CameraShake effect)
        {
            Destroy(effect);
        }

        public override bool EffectIsFinished(CameraShake effect)
        {
            return effect.IsFinished;
        }
    }
}
