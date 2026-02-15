using System;
using UnityEngine;
using Utils.EnumArray;

namespace Game.View.Events.Vfx
{
    [Serializable]
    public enum VfxKind
    {
        Block,
    }

    [Serializable]
    public struct VfxCache
    {
        public GameObject Effect;
    }

    [CreateAssetMenu(menuName = "Hypermania/VFX Library")]
    public class VfxLibrary : ScriptableObject
    {
        public EnumArray<VfxKind, VfxCache> Library;
    }
}
