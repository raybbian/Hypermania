using System;
using UnityEngine;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Game.Sim.Configs
{
    [Serializable]
    public struct InputConfig
    {
        public int DashWindow;
        public int SuperJumpWindow;
        public int InputBufferWindow;

        // Number of frames into a heavy attack after which, if the heavy
        // attack button is still held, the move upgrades to a super.
        public int SuperDelayWindow;
    }

    // Deterministic global tuning. References AudioStats for tempo math.
    // The character roster lives in PresentationOptions on the runner side;
    // the sim only sees per-player CharacterStats picked from this roster.
    [CreateAssetMenu(menuName = "Hypermania/Sim/Global Stats")]
    public class GlobalStats : ScriptableObject
    {
        public sfloat Gravity;
        public sfloat GroundY;
        public sfloat WallsX;
        public int ClankTicks;
        public int ForwardDashCancelAfterTicks;
        public int ForwardDashTicks;
        public int ForwardAirDashTicks;
        public int BackDashCancelAfterTicks;
        public int BackDashTicks;
        public int BackAirDashTicks;
        public sfloat RunningSpeedMultiplier;
        public sfloat SuperJumpMultiplier;
        public int RoundTimeTicks;
        public int PreGameDelayTicks;
        public sfloat MaxHype;
        public sfloat HypeMovementFactor;
        public sfloat PassiveSuperGain;
        public sfloat SuperMax;
        public sfloat SuperCost;
        public int SuperTier1Beats;
        public int SuperTier2Beats;
        public sfloat MaxFighterDistance;
        public int RoundEndTicks;
        public int SuperDisplayHitstopTicks;
        public int SuperPostDisplayHitstopTicks;
        public int SuperRecoveryFrames;
        public sfloat FloatingFactor;
        public int ManiaSlowTicks;
        public int ManiaStartPaddingTicks;
        public int ManiaFailStunTicks;
        public sfloat ManiaFailKnockbackMagnitude;
        public int ManiaPostComboInputLockTicks;
        public int GrabTechWindow;
        public int GrabTechStunTicks;
        public int LightKnockdownTicks;
        public int HeavyKnockdownTicks;
        public int BurstVfxTicks;
        public sfloat GrabTechKnockbackMagnitude;
        public int StalingBufferSize;
        public sfloat RhythmComboFinisherDamageMult;
        public sfloat FreestyleDamageMultiplier;
        public sfloat FreestyleHitstunMultiplier;
        public sfloat NormalDifficultyDamageMultiplier;

        public InputConfig Input;
        public AudioStats Audio;

        public int RoundCountdownTicks => Audio.BeatsToFrame(8);
    }
}
