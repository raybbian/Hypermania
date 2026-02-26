using System.Collections;
using Design.Animation;
using Design.Configs;
using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum FighterFacing
    {
        Left,
        Right,
    }

    public enum FighterLocation
    {
        Grounded,
        Airborne,
    }

    [MemoryPackable]
    public partial struct FighterState
    {
        public SVector2 Position;
        public SVector2 Velocity;
        public sfloat Health;
        public int ComboedCount;
        public InputHistory InputH;
        public int Lives;
        public sfloat Burst;
        public int AirDashCount;

        public CharacterState State { get; private set; }
        public Frame StateStart { get; private set; }

        /// <summary>
        /// Set to a value that marks the first frame in which the character should return to neutral.
        /// </summary>
        public Frame StateEnd { get; private set; }
        public Frame ImmunityEnd { get; private set; }

        public FighterFacing FacingDir;

        public Frame LocationSt { get; private set; }

        public BoxProps HitProps { get; private set; }
        public SVector2 HitLocation { get; private set; }

        public bool IsAerial =>
            State == CharacterState.LightAerial
            || State == CharacterState.MediumAerial
            || State == CharacterState.SuperAerial
            || State == CharacterState.SpecialAerial;

        public bool IsDash =>
            State == CharacterState.BackAirDash
            || State == CharacterState.ForwardAirDash
            || State == CharacterState.ForwardDash
            || State == CharacterState.BackDash;

        public bool Actionable =>
            State == CharacterState.Idle
            || State == CharacterState.Walk
            || State == CharacterState.Jump
            || State == CharacterState.Running;

        public SVector2 ForwardVector => FacingDir == FighterFacing.Left ? SVector2.left : SVector2.right;
        public SVector2 BackwardVector => FacingDir == FighterFacing.Left ? SVector2.right : SVector2.left;
        public InputFlags ForwardInput => FacingDir == FighterFacing.Left ? InputFlags.Left : InputFlags.Right;
        public InputFlags BackwardInput => FacingDir == FighterFacing.Left ? InputFlags.Right : InputFlags.Left;

        public static FighterState Create(
            SVector2 position,
            FighterFacing facingDirection,
            CharacterConfig config,
            int lives
        )
        {
            FighterState state = new FighterState
            {
                Position = position,
                Velocity = SVector2.zero,
                State = CharacterState.Idle,
                StateStart = Frame.FirstFrame,
                StateEnd = Frame.Infinity,
                ImmunityEnd = Frame.FirstFrame,
                ComboedCount = 0,
                InputH = new InputHistory(),
                // TODO: character dependent?
                Health = config.Health,
                FacingDir = facingDirection,
                Lives = lives,
                Burst = 0,
                AirDashCount = 0,
            };
            return state;
        }

        public void RoundReset(SVector2 position, FighterFacing facingDirection, CharacterConfig config)
        {
            Position = position;
            Velocity = SVector2.zero;
            State = CharacterState.Idle;
            StateStart = Frame.FirstFrame;
            StateEnd = Frame.Infinity;
            ImmunityEnd = Frame.FirstFrame;
            ComboedCount = 0;
            InputH.Clear(); // Clear, don't want to read input from a previous round.
            // TODO: character dependent?
            Burst = 0;
            AirDashCount = 0;
            Health = config.Health;
            FacingDir = facingDirection;
        }

        public void DoFrameStart(GlobalConfig config)
        {
            if (Actionable)
            {
                ComboedCount = 0;
            }
            HitLocation = SVector2.zero;
            HitProps = new BoxProps();
            if (Location(config) == FighterLocation.Grounded)
            {
                AirDashCount = 0;
            }
        }

        public FighterLocation Location(GlobalConfig config)
        {
            if (Position.y > config.GroundY)
            {
                return FighterLocation.Airborne;
            }
            return FighterLocation.Grounded;
        }

        public void FaceTowards(SVector2 location)
        {
            // can only switch locations if in idle/walking
            if (State != CharacterState.Idle && State != CharacterState.Walk)
            {
                return;
            }
            if (location.x < Position.x)
            {
                FacingDir = FighterFacing.Left;
            }
            else
            {
                FacingDir = FighterFacing.Right;
            }
        }

        public void TickStateMachine(Frame frame)
        {
            // if animation ends, switch back to idle
            if (frame >= StateEnd)
            {
                // TODO: is best place here?
                if (IsDash)
                {
                    Velocity.x = 0;
                }
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void ApplyMovementIntent(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (!Actionable)
            {
                return;
            }
            if (Location(config) == FighterLocation.Grounded)
            {
                Velocity.x = 0;

                if (
                    InputH.IsHeld(ForwardInput)
                    && InputH.PressedAndReleasedRecently(ForwardInput, config.Input.DashWindow, 1)
                )
                {
                    State = CharacterState.ForwardDash;
                    StateEnd = frame + config.ForwardDashTicks;
                    StateStart = frame;
                    Velocity.x = ForwardVector.x * (characterConfig.ForwardDashDistance / config.ForwardDashTicks);
                    return;
                }

                if (
                    InputH.IsHeld(BackwardInput)
                    && InputH.PressedAndReleasedRecently(BackwardInput, config.Input.DashWindow, 1)
                )
                {
                    State = CharacterState.BackDash;
                    StateEnd = frame + config.BackDashTicks;
                    StateStart = frame;
                    Velocity.x = BackwardVector.x * characterConfig.BackDashDistance / config.BackDashTicks;
                    return;
                }

                // prevent jump from taking run multiplier
                sfloat runMult =
                    State == CharacterState.Running && !InputH.IsHeld(InputFlags.Up)
                        ? config.RunningSpeedMultiplier
                        : sfloat.One;

                if (InputH.IsHeld(ForwardInput))
                {
                    Velocity.x += ForwardVector.x * characterConfig.ForwardSpeed * runMult;
                }
                if (InputH.IsHeld(BackwardInput))
                {
                    Velocity.x += BackwardVector.x * characterConfig.BackSpeed;
                }

                if (InputH.IsHeld(InputFlags.Up))
                {
                    if (InputH.PressedRecently(InputFlags.Down, config.Input.SuperJumpWindow))
                    {
                        Velocity.y = (sfloat)1.25 * characterConfig.JumpVelocity;
                    }
                    else
                    {
                        Velocity.y = characterConfig.JumpVelocity;
                    }
                }
            }
            else if (Location(config) == FighterLocation.Airborne)
            {
                if (
                    InputH.IsHeld(ForwardInput)
                    && InputH.PressedAndReleasedRecently(ForwardInput, config.Input.DashWindow, 1)
                    && AirDashCount < characterConfig.NumAirDashes
                )
                {
                    AirDashCount += 1;
                    State = CharacterState.ForwardAirDash;
                    StateEnd = frame + config.ForwardAirDashTicks;
                    StateStart = frame;
                    Velocity.x =
                        ForwardVector.x * (characterConfig.ForwardAirDashDistance / config.ForwardAirDashTicks);
                    Velocity.y = 0;
                    return;
                }

                if (
                    InputH.IsHeld(BackwardInput)
                    && InputH.PressedAndReleasedRecently(BackwardInput, config.Input.DashWindow, 1)
                    && AirDashCount < characterConfig.NumAirDashes
                )
                {
                    AirDashCount += 1;
                    State = CharacterState.BackAirDash;
                    StateEnd = frame + config.BackAirDashTicks;
                    StateStart = frame;
                    Velocity.x = BackwardVector.x * (characterConfig.BackAirDashDistance / config.BackAirDashTicks);
                    Velocity.y = 0;
                    return;
                }
            }
        }

        public void ApplyActiveState(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (State == CharacterState.Hit)
            {
                if (InputH.IsHeld(InputFlags.Burst))
                {
                    Burst = 0;
                    State = CharacterState.Burst;
                    StateStart = frame;
                    StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                    // TODO: apply knockback to other player (this should be a hitbox on a burst animation with large kb)
                }
            }

            bool dashCancelEligible =
                ((frame + config.ForwardDashCancelAfterTicks >= StateEnd) && State == CharacterState.ForwardDash)
                || ((frame + config.BackDashCancelAfterTicks >= StateEnd) && State == CharacterState.BackDash);

            if (!Actionable && !dashCancelEligible)
            {
                return;
            }

            if (InputH.PressedRecently(InputFlags.LightAttack, config.Input.InputBufferWindow))
            {
                switch (Location(config))
                {
                    case FighterLocation.Grounded:
                        {
                            Velocity = SVector2.zero;
                            State = CharacterState.LightAttack;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                    case FighterLocation.Airborne:
                        {
                            State = CharacterState.LightAerial;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                }
            }
            else if (InputH.PressedRecently(InputFlags.MediumAttack, config.Input.InputBufferWindow))
            {
                switch (Location(config))
                {
                    case FighterLocation.Grounded:
                        {
                            Velocity = SVector2.zero;
                            State = CharacterState.MediumAttack;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                }
            }
            else if (InputH.PressedRecently(InputFlags.HeavyAttack, config.Input.InputBufferWindow))
            {
                switch (Location(config))
                {
                    case FighterLocation.Grounded:
                        {
                            Velocity = SVector2.zero;
                            State = CharacterState.SuperAttack;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                }
            }
            else if (InputH.IsHeld(ForwardInput) && dashCancelEligible && State == CharacterState.ForwardDash)
            {
                State = CharacterState.Running;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void UpdatePosition(GlobalConfig config)
        {
            // Apply gravity if not grounded and not in airdash
            if (
                State != CharacterState.BackAirDash
                && State != CharacterState.ForwardAirDash
                && Position.y > config.GroundY
            )
            {
                Velocity.y += config.Gravity * 1 / GameManager.TPS;
            }

            // Update Position
            Position += Velocity * 1 / GameManager.TPS;

            // Floor collision
            if (Position.y <= config.GroundY)
            {
                Position.y = config.GroundY;

                if (Velocity.y < 0)
                    Velocity.y = 0;
            }
            if (Position.x >= config.WallsX)
            {
                Position.x = config.WallsX;
                if (Velocity.x > 0)
                    Velocity.x = 0;
            }
            if (Position.x <= -config.WallsX)
            {
                Position.x = -config.WallsX;
                if (Velocity.x < 0)
                    Velocity.x = 0;
            }
        }

        public void ApplyAerialCancel(Frame frame, GlobalConfig config)
        {
            if (!IsAerial)
            {
                return;
            }
            // TODO: apply some landing lag here
            if (Location(config) == FighterLocation.Grounded)
            {
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void AddBoxes(Frame frame, CharacterConfig config, Physics<BoxProps> physics, int handle)
        {
            int tick = frame - StateStart;
            FrameData frameData = config.GetFrameData(State, tick);

            foreach (var box in frameData.Boxes)
            {
                SVector2 centerLocal = box.CenterLocal;
                if (FacingDir == FighterFacing.Left)
                {
                    centerLocal.x *= -1;
                }
                SVector2 sizeLocal = box.SizeLocal;
                SVector2 centerWorld = Position + centerLocal;
                BoxProps newProps = box.Props;
                if (FacingDir == FighterFacing.Left)
                {
                    newProps.Knockback.x *= -1;
                }
                physics.AddBox(handle, centerWorld, sizeLocal, newProps);
            }
        }

        public HitOutcome ApplyHit(Frame frame, BoxProps props, CharacterConfig config, SVector2 location)
        {
            if (ImmunityEnd > frame)
            {
                return new HitOutcome { Kind = HitKind.None };
            }

            HitProps = props;
            HitLocation = location;

            bool holdingBack = InputH.IsHeld(BackwardInput);
            bool holdingDown = InputH.IsHeld(InputFlags.Down);

            bool standBlock = props.AttackKind != AttackKind.Low;
            bool crouchBlock = props.AttackKind != AttackKind.Overhead;
            bool blockSuccess = holdingBack && ((holdingDown && crouchBlock) || (!holdingDown && standBlock));

            if (blockSuccess)
            {
                // True: Crouch blocking, False: Stand blocking
                State = holdingDown ? CharacterState.BlockCrouch : CharacterState.BlockStand;
                StateStart = frame;
                StateEnd = frame + props.BlockstunTicks + 1;
                ImmunityEnd = frame + 7;
                // TODO: check if other move is special, if so apply chip
                return new HitOutcome { Kind = HitKind.Blocked, HitstopFrames = 6 };
            }

            State = CharacterState.Hit;
            StateStart = frame;
            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            StateEnd = frame + props.HitstunTicks + 1;
            // TODO: fixme, just to prevent multi hit
            ImmunityEnd = frame + 7;
            // TODO: if high enough, go knockdown
            Health -= props.Damage;

            Burst += props.Damage;
            Burst = Mathsf.Clamp(Burst, sfloat.Zero, config.BurstMax);

            Velocity = props.Knockback;

            ComboedCount++;
            
            switch (props.AttackKind)
            {
                case AttackKind.Low:
                    return new HitOutcome { Kind = HitKind.Hit, Props = props, HitstopFrames = 6 };
                case AttackKind.Medium:
                    return new HitOutcome { Kind = HitKind.Hit, Props = props, HitstopFrames = 8 };
                case AttackKind.Overhead:
                    return new HitOutcome { Kind = HitKind.Hit, Props = props, HitstopFrames = 10 };
                default :
                    return new HitOutcome { Kind = HitKind.Hit, Props = props };
            }
        }

        public void ApplyClank(Frame frame, GlobalConfig config)
        {
            State = CharacterState.Hit;
            StateStart = frame;
            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            StateEnd = frame + config.ClankTicks + 1;
            Velocity = SVector2.zero;
        }

        public void ApplyMovementState(Frame frame, GlobalConfig config)
        {
            if (
                (State == CharacterState.Idle || State == CharacterState.Walk || State == CharacterState.Running)
                && Location(config) == FighterLocation.Airborne
            )
            {
                State = CharacterState.Jump;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
            else if (
                (State == CharacterState.Idle || State == CharacterState.Jump)
                && Velocity.magnitude > (sfloat)0.01f
                && Location(config) == FighterLocation.Grounded
            )
            {
                State = CharacterState.Walk;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
            else if (
                (State == CharacterState.Walk || State == CharacterState.Jump || State == CharacterState.Running)
                && Velocity.magnitude < (sfloat)0.01f
                && Location(config) == FighterLocation.Grounded
            )
            {
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }
    }
}
