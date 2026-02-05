using System;

namespace Game
{
    public enum CharacterState
    {
        Hit,
        Walk,
        Jump,
        Idle,
        Knockdown,
        LightAttack,
        LightAerial,
        LightCrouching,
        MediumAttack,
        MediumAerial,
        MediumCrouching,
        SuperAttack,
        SuperAerial,
        SuperCrouching,
        SpecialAttack,
        SpecialAerial,
        SpecialCrouching,
        ForwardDash,
        BackDash,
        Ultimate,
        Death,
    }

    [Serializable]
    public enum Character
    {
        SampleFighter,
        Nythea,
    }
}
