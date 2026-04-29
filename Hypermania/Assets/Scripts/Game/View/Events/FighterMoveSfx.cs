using System;
using System.Collections.Generic;
using UnityEngine;
using Utils.EnumArray;
using Game.Sim;

namespace Game.View.Events
{
    [Serializable]
    public struct SfxKindList
    {
        public List<SfxKind> Kinds;
    }

    // TODO: will fix this later, should be able to be spawned on a specific frame
    [CreateAssetMenu(menuName = "Hypermania/Fighter Move Sfx")]
    public class FighterMoveSfx : ScriptableObject
    {
        public EnumArray<CharacterState, SfxKindList> Sfx = new();
    }
}
