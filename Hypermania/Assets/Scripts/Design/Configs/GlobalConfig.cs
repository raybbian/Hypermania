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

        /// <summary>
        /// Number of frames into a heavy attack after which, if the heavy
        /// attack button is still held, the move upgrades to a super.
        /// </summary>
        public int SuperDelayWindow;
    }

    [Serializable]
    public struct InfoConfig
    {
        public bool ShowFrameMeter;
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
        public int RoundCountdownTicks => Audio.BeatsToFrame(8);
        public int PreGameDelayTicks = 0;
        public sfloat MaxHype = 100;
        public sfloat HypeMovementFactor = (sfloat)0.3f;
        public sfloat PassiveSuperGain = (sfloat)5f;
        public sfloat SuperMax = (sfloat)400f;
        public sfloat SuperCost = (sfloat)100f;
        public int SuperTier1Beats = 8;
        public int SuperTier2Beats = 16;
        public sfloat CameraHalfHeight = (sfloat)1.5f;
        public sfloat CameraPadding = (sfloat)0.3f;
        public int RoundEndTicks = 120;
        public int SuperDisplayHitstopTicks = 60;
        public int SuperPostDisplayHitstopTicks = 0;
        public int SuperRecoveryFrames = 0;
        public sfloat FloatingFactor = (sfloat)1.3f;
        public int ManiaSlowTicks = 60;
        public int ManiaStartPaddingTicks = 30;
        public int ManiaFailStunTicks = 30;
        public sfloat ManiaFailKnockbackMagnitude = (sfloat)1.5f;
        public int GrabTechWindow = 10;
        public int GrabTechStunTicks = 20;
        public int LightKnockdownTicks = 30;
        public int HeavyKnockdownTicks = 60;
        public sfloat GrabTechKnockbackMagnitude = (sfloat)2f;
        public int StalingBufferSize = 8;
        public sfloat RhythmComboFinisherDamageMult = (sfloat)2f;
        public sfloat FreestyleDamageMultiplier = (sfloat)1.5f;
        public sfloat FreestyleHitstunMultiplier = (sfloat)1.25f;

        /// <summary>
        /// Damage multiplier applied to hits dealt by an attacker whose
        /// <see cref="Game.Sim.PlayerOptions.ManiaDifficulty"/> is
        /// <see cref="Game.Sim.ManiaDifficulty.Normal"/> (the easier setting).
        /// Hard attackers deal full damage (implicit 1.0x).
        /// </summary>
        public sfloat NormalDifficultyDamageMultiplier = (sfloat)0.6f;
        public sfloat CameraHalfWidth => CameraHalfHeight * (sfloat)1.7777777f;

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
