using System.Collections.Generic;
using Game;
using UnityEngine;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design
{
    [CreateAssetMenu(menuName = "Hypermania/Global Config")]
    public class GlobalConfig : ScriptableObject
    {
        public sfloat Gravity = -20;
        public sfloat GroundY = -3;
        public sfloat WallsX = 4;
        public int ClankTicks = 30;

        [SerializeField]
        private EnumArray<Character, CharacterConfig> _configs;

        public CharacterConfig CharacterConfig(Character character)
        {
            return _configs[character];
        }
    }
}
