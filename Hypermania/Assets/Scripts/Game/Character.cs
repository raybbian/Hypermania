using System;

namespace Game
{
    [Serializable]
    public enum CharacterState
    {
        Hit = 0,
        ForwardWalk = 1,
        BackWalk = 28,
        Jump = 2,
        PreJump = 29,
        Idle = 3,
        Knockdown = 4,
        LightAttack = 5,
        LightAerial = 6,
        LightCrouching = 7,
        MediumAttack = 8,
        MediumAerial = 9,
        MediumCrouching = 10,
        HeavyAttack = 11,
        HeavyAerial = 12,
        HeavyCrouching = 13,
        SpecialAttack = 14,
        SpecialAerial = 15,
        SpecialCrouching = 16,
        ForwardDash = 17,
        BackDash = 18,
        Ultimate = 19,
        Death = 20,
        Burst = 21,
        BlockStand = 22,
        BlockCrouch = 23,
        Running = 24,
        ForwardAirDash = 25,
        BackAirDash = 26,
        Crouch = 27,
        Falling = 30,
        Landing = 31,
        Grabbed = 32,
        Grab = 33,
        Throw = 34,
        HeavyAerialFollowUp = 35,
        MediumAttackFollowUp = 36,
        SpecialAerialFollowUp = 37,
    }

    [Serializable]
    public enum Character
    {
        // SampleFighter = 0,
        Nythea = 1,
        Via = 2,
    }
}
