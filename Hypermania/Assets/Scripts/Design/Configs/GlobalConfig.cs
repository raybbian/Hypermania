using System;
using Game;
using UnityEngine;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Configs
{
    [Serializable]
    public struct InputConfig
    {
        public int DashWindow;
        public int SuperJumpWindow;
        public int InputBufferWindow;
        public int BeatCancelWindow;
    }

    [CreateAssetMenu(menuName = "Hypermania/Global Config")]
    public class GlobalConfig : ScriptableObject
    {
        public sfloat Gravity = -20;
        public sfloat GroundY = -3;
        public sfloat WallsX = 4;
        public int ClankTicks = 30;
        public int ForwardDashCancelAfterTicks = 2;
        public int ForwardDashTicks = 5;
        public int ForwardAirDashTicks = 5;
        public int BackDashCancelAfterTicks = 6;
        public int BackDashTicks = 15;
        public int BackAirDashTicks = 15;
        public sfloat RunningSpeedMultiplier = 2;
        public sfloat SuperJumpMultiplier = (sfloat)1.25f;
        public int RoundTimeTicks = 10800;
        public int RoundCountdownTicks = 180;

        [SerializeField]
        private InputConfig _inputConfig;
        public InputConfig Input => _inputConfig;

        [SerializeField]
        private AudioConfig _audioConfig;
        public AudioConfig Audio => _audioConfig;

        [SerializeField]
        private EnumArray<Character, CharacterConfig> _configs;

        public CharacterConfig CharacterConfig(Character character)
        {
            return _configs[character];
        }
    }
}
