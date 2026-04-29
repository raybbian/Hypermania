using Game;
using Game.Sim.Configs;
using UnityEngine;
using Utils.EnumArray;
using Game.Sim;

namespace Game.View.Configs
{
    // View-side counterpart of AudioStats. Holds the AudioClip the Conductor
    // streams and the per-character victory/intro themes. The sim only reads
    // AudioStats; nothing in here is deterministic.
    [CreateAssetMenu(menuName = "Hypermania/View/Audio Presentation")]
    public class AudioPresentation : ScriptableObject
    {
        public AudioStats Stats;
        public AudioClip AudioClip;
        public EnumArray<Character, AudioClip> CharacterThemes;
    }
}
