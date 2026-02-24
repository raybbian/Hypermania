using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Utils;
using Utils.EnumArray;

namespace Game.View
{
    public struct SfxEvent : IEquatable<SfxEvent>
    {
        public Frame StartFrame;
        public SfxKind Kind;
        public int Hash;

        public override int GetHashCode()
        {
            return HashCode.Combine(StartFrame, Hash, Kind);
        }

        public override bool Equals(object obj)
        {
            return obj is SfxEvent other && Equals(other);
        }

        public bool Equals(SfxEvent other)
        {
            return StartFrame.Equals(other.StartFrame) && Kind == other.Kind && Hash == other.Hash;
        }

        public static bool operator ==(SfxEvent left, SfxEvent right) => left.Equals(right);

        public static bool operator !=(SfxEvent left, SfxEvent right) => !left.Equals(right);
    }

    public class SfxManager : MonoBehaviour
    {
        [SerializeField]
        private SfxLibrary _sfxLibrary;

        [SerializeField]
        private float _fadeOutDuration;

        // randomly choose between variants for sfx, not critical to state/gameplay
        [SerializeField]
        private System.Random _random;

        private Dictionary<SfxEvent, AudioSource> _curPlaying;

        public void Awake()
        {
            _curPlaying = new Dictionary<SfxEvent, AudioSource>();
            _random = new System.Random();
        }

        public void InvalidateAndPlay(Frame start, Frame end, HashSet<SfxEvent> desired)
        {
            // stop and remove all sfx playing from start to end not in toPlay
            List<(SfxEvent ev, bool instant)> toRemove = new List<(SfxEvent, bool)>();
            foreach ((var ev, var source) in _curPlaying)
            {
                if (!desired.Contains(ev) && start <= ev.StartFrame && ev.StartFrame <= end)
                {
                    toRemove.Add((ev, false));
                }
                // also remove stopped clips
                if (!source.isPlaying)
                {
                    toRemove.Add((ev, true));
                }
            }
            foreach (var rem in toRemove)
            {
                if (_curPlaying.Remove(rem.ev, out var source))
                {
                    if (rem.instant)
                    {
                        source.Stop();
                        Destroy(source.gameObject);
                    }
                    else
                    {
                        StartCoroutine(FadeOut(source, _fadeOutDuration));
                    }
                }
            }

            // start all sfx not in cur playing but in desired
            foreach (var ev in desired)
            {
                if (!_curPlaying.ContainsKey(ev) && _sfxLibrary.Library[ev.Kind].Clips != null)
                {
                    // TODO: pool gameobjects?
                    GameObject source = new GameObject($"{ev.Kind} Sfx");
                    source.transform.SetParent(transform);
                    // TODO: set location of source?
                    AudioSource asource = source.AddComponent<AudioSource>();
                    int clipInd = _random.Next(0, _sfxLibrary.Library[ev.Kind].Clips.Length - 1);
                    asource.clip = _sfxLibrary.Library[ev.Kind].Clips[clipInd];
                    asource.Play();

                    _curPlaying[ev] = asource;
                }
            }
        }

        IEnumerator FadeOut(AudioSource s, float duration)
        {
            float start = s.volume;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                s.volume = Mathf.Lerp(start, 0f, t / duration);
                yield return null;
            }
            s.Stop();
            Destroy(s.gameObject);
        }
    }
}
