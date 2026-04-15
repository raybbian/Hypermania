using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Design.Configs
{
    [Serializable]
    public struct ComboMove
    {
        public InputFlags Input;
        public AudioConfig.BeatSubdivision DelayAfter;
    }

    [CreateAssetMenu(menuName = "Hypermania/Combo Config")]
    public class ComboConfig : ScriptableObject
    {
        public List<ComboMove> Moves;
    }
}
