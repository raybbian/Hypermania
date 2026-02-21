using System;
using System.Collections;
using UnityEngine;

namespace Game.View.Events
{
    public struct SfxEvent : IEquatable<SfxEvent>
    {
        public SfxKind Kind;

        public bool Equals(SfxEvent other)
        {
            return Kind == other.Kind;
        }
    }

    public class SfxManager : ViewEventManager<SfxEvent, AudioSource>
    {
        [SerializeField]
        private SfxLibrary _sfxLibrary;

        [SerializeField]
        private float _fadeOutDuration;

        // randomly choose between variants for sfx, not critical to state/gameplay
        [SerializeField]
        private System.Random _random;

        public void Awake()
        {
            _random = new System.Random();
        }

        public override AudioSource OnStartEffect(ViewEvent<SfxEvent> ev)
        {
            GameObject source = new GameObject($"{ev.Event.Kind} Sfx");
            source.transform.SetParent(transform);
            // TODO: set location of source?
            AudioSource asource = source.AddComponent<AudioSource>();
            int clipInd = _random.Next(0, _sfxLibrary.Library[ev.Event.Kind].Clips.Length - 1);
            asource.clip = _sfxLibrary.Library[ev.Event.Kind].Clips[clipInd];
            asource.Play();
            return asource;
        }

        public override void OnEndEffect(AudioSource source)
        {
            if (!source.isPlaying)
            {
                Destroy(source.gameObject);
            }
            else
            {
                StartCoroutine(FadeOut(source, _fadeOutDuration));
            }
        }

        public override bool EffectIsFinished(AudioSource effect)
        {
            return !effect.isPlaying;
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
