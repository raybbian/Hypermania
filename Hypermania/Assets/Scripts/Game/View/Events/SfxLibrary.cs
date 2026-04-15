using System;
using UnityEngine;
using Utils.EnumArray;

namespace Game.View.Events
{
    [Serializable]
    public enum SfxKind
    {
        MediumPunch,
        HeavyPunch,
        ComboGood,
        ComboMiss,
        ComboOk,
        Block,
        Woosh,
        ScytheSwing,
        Sweep,
        SuperReady,
        SuperStart,
    }

    [Serializable]
    public struct SfxCache
    {
        public AudioClip[] Clips;
    }

    [CreateAssetMenu(menuName = "Hypermania/SFX Library")]
    public class SfxLibrary : ScriptableObject
    {
        public EnumArray<SfxKind, SfxCache> Library;
    }
}
