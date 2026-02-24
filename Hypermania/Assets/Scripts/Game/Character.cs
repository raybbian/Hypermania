using System;

namespace Game
{
    public enum CharacterState
    {
        Hit = 0,
        Walk = 1,
        Jump = 2,
        Idle = 3,
        Knockdown = 4,
        LightAttack = 5,
        LightAerial = 6,
        LightCrouching = 7,
        MediumAttack = 8,
        MediumAerial = 9,
        MediumCrouching = 10,
        SuperAttack = 11,
        SuperAerial = 12,
        SuperCrouching = 13,
        SpecialAttack = 14,
        SpecialAerial = 15,
        SpecialCrouching = 16,
        ForwardDash = 17,
        BackDash = 18,
        Ultimate = 19,
        Death = 20,
        Burst = 21,
    }

    [Serializable]
    public enum Character
    {
        SampleFighter = 0,
        Nythea = 1,
    }
}
