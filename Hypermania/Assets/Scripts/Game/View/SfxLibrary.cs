using System;
using UnityEngine;
using Utils.EnumArray;

namespace Game.View
{
    [Serializable]
    public enum SfxKind
    {
        MediumPunch,
        HeavyPunch,
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
