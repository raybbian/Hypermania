using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

namespace Game.View.Events
{
    public struct ViewEvent<TEvent> : IEquatable<ViewEvent<TEvent>>
        where TEvent : IEquatable<TEvent>
    {
        public Frame StartFrame;
        public int Hash;
        public TEvent Event;

        public override int GetHashCode()
        {
            return HashCode.Combine(StartFrame, Hash, Event);
        }

        public override bool Equals(object obj)
        {
            return obj is ViewEvent<TEvent> other && Equals(other);
        }

        public bool Equals(ViewEvent<TEvent> other)
        {
            return StartFrame.Equals(other.StartFrame) && Event.Equals(other.Event) && Hash == other.Hash;
        }

        public static bool operator ==(ViewEvent<TEvent> left, ViewEvent<TEvent> right) => left.Equals(right);

        public static bool operator !=(ViewEvent<TEvent> left, ViewEvent<TEvent> right) => !left.Equals(right);
    }

    public abstract class ViewEventManager<TEvent, TEffect> : MonoBehaviour
        where TEvent : IEquatable<TEvent>
    {
        private Dictionary<ViewEvent<TEvent>, TEffect> _curActive;
        private HashSet<ViewEvent<TEvent>> _desired;

        public ViewEventManager()
        {
            _curActive = new Dictionary<ViewEvent<TEvent>, TEffect>();
            _desired = new HashSet<ViewEvent<TEvent>>();
        }

        public void AddDesired(ViewEvent<TEvent> ev)
        {
            _desired.Add(ev);
        }

        public void InvalidateAndConsume(Frame start, Frame end)
        {
            List<ViewEvent<TEvent>> toRemove = new List<ViewEvent<TEvent>>();
            foreach ((var ev, var effect) in _curActive)
            {
                if (!_desired.Contains(ev) && start <= ev.StartFrame && ev.StartFrame <= end)
                {
                    toRemove.Add(ev);
                }
                if (EffectIsFinished(effect))
                {
                    toRemove.Add(ev);
                }
            }
            foreach (var rem in toRemove)
            {
                if (_curActive.Remove(rem, out var source))
                {
                    OnEndEffect(source);
                }
            }

            // start all sfx not in cur playing but in desired
            foreach (var ev in _desired)
            {
                if (!_curActive.ContainsKey(ev))
                {
                    _curActive[ev] = OnStartEffect(ev);
                }
            }
            _desired.Clear();
        }

        public abstract TEffect OnStartEffect(ViewEvent<TEvent> effect);
        public abstract void OnEndEffect(TEffect effect);
        public abstract bool EffectIsFinished(TEffect effect);
    }
}
