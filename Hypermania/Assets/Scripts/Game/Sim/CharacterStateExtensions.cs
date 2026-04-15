namespace Game.Sim
{
    public static class CharacterStateExtensions
    {
        public static bool IsAerialAttack(this CharacterState state) =>
            state == CharacterState.LightAerial
            || state == CharacterState.MediumAerial
            || state == CharacterState.HeavyAerial
            || state == CharacterState.SpecialAerial;

        public static bool IsAerial(this CharacterState state) =>
            state.IsAerialAttack()
            || state == CharacterState.Jump
            || state == CharacterState.PreJump
            || state == CharacterState.Falling;

        public static bool IsGrounded(this CharacterState state) => !state.IsAerial();

        public static bool IsDash(this CharacterState state) =>
            state == CharacterState.BackAirDash
            || state == CharacterState.ForwardAirDash
            || state == CharacterState.ForwardDash
            || state == CharacterState.BackDash;

        public static bool IsGroundedActionable(this CharacterState state) =>
            state == CharacterState.Idle
            || state == CharacterState.ForwardWalk
            || state == CharacterState.BackWalk
            || state == CharacterState.Running
            || state == CharacterState.Crouch;

        public static bool IsActionable(this CharacterState state) =>
            state == CharacterState.Jump || state == CharacterState.Falling || state.IsGroundedActionable();
    }
}
