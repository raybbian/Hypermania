using System.Collections;
using System.Collections.Generic;
using Design.Animation;
using Design.Configs;
using Game.View.Overlay;
using MemoryPack;
using UnityEngine;
using Utils;
using Utils.SoftFloat;
using static Design.Configs.AudioConfig;

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

    public enum FighterAttackLocation
    {
        Standing,
        Aerial,
        Crouching,
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
        public VictoryKind[] Victories;
        public int amountVictories;

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

        public SVector2 StoredJumpVelocity;

        public bool IsAerialAttack =>
            State == CharacterState.LightAerial
            || State == CharacterState.MediumAerial
            || State == CharacterState.SuperAerial
            || State == CharacterState.SpecialAerial;

        public bool IsDash =>
            State == CharacterState.BackAirDash
            || State == CharacterState.ForwardAirDash
            || State == CharacterState.ForwardDash
            || State == CharacterState.BackDash;

        public bool Actionable => State == CharacterState.Jump || State == CharacterState.Falling || GroundedActionable;

        public bool GroundedActionable =>
            State == CharacterState.Idle
            || State == CharacterState.ForwardWalk
            || State == CharacterState.BackWalk
            || State == CharacterState.Running
            || State == CharacterState.Crouch;

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
                Victories = new VictoryKind[lives],
                amountVictories = 0,
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

        public FighterAttackLocation AttackLocation(GlobalConfig config)
        {
            FighterLocation loc = Location(config);
            if (loc == FighterLocation.Airborne)
            {
                return FighterAttackLocation.Aerial;
            }
            return InputH.IsHeld(InputFlags.Down) ? FighterAttackLocation.Crouching : FighterAttackLocation.Standing;
        }

        public void SetState(CharacterState nextState, Frame start, Frame end, bool forceChange = false)
        {
            if (State != nextState || forceChange)
            {
                State = nextState;
                StateStart = start;
                StateEnd = end;
            }
        }

        public void FaceTowards(SVector2 location)
        {
            if (State != CharacterState.Idle && State != CharacterState.ForwardWalk && State != CharacterState.BackWalk)
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
                if (State == CharacterState.PreJump)
                {
                    Velocity = StoredJumpVelocity;
                    StoredJumpVelocity = SVector2.zero;
                    SetState(CharacterState.Jump, frame, Frame.Infinity);
                    return;
                }
                SetState(CharacterState.Idle, frame, Frame.Infinity);
            }
        }

        public void ApplyMovementState(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (!Actionable)
            {
                return;
            }
            sfloat runMult = State == CharacterState.Running ? config.RunningSpeedMultiplier : (sfloat)1f;

            if (GroundedActionable)
            {
                if (InputH.IsHeld(InputFlags.Up))
                {
                    // Jump
                    if (InputH.PressedAndReleasedRecently(InputFlags.Down, config.Input.SuperJumpWindow))
                    {
                        StoredJumpVelocity.y = characterConfig.JumpVelocity * config.SuperJumpMultiplier;
                    }
                    else
                    {
                        StoredJumpVelocity.y = characterConfig.JumpVelocity;
                    }
                    if (InputH.IsHeld(ForwardInput))
                    {
                        StoredJumpVelocity.x = ForwardVector.x * characterConfig.ForwardSpeed * runMult;
                    }
                    else if (InputH.IsHeld(BackwardInput))
                    {
                        StoredJumpVelocity.x = BackwardVector.x * characterConfig.BackSpeed;
                    }
                    else
                    {
                        StoredJumpVelocity.x = 0;
                    }
                    Velocity.x = 0;
                    SetState(
                        CharacterState.PreJump,
                        frame,
                        frame + characterConfig.GetHitboxData(CharacterState.PreJump).TotalTicks
                    );
                    return;
                }

                if (InputH.IsHeld(InputFlags.Down))
                {
                    // Crouch
                    Velocity.x = 0;
                    SetState(CharacterState.Crouch, frame, Frame.Infinity);
                    return;
                }

                if (InputH.IsHeld(ForwardInput))
                {
                    Velocity.x = ForwardVector.x * characterConfig.ForwardSpeed * runMult;

                    CharacterState nxtState =
                        State == CharacterState.Running ? CharacterState.Running : CharacterState.ForwardWalk;
                    SetState(nxtState, frame, Frame.Infinity);
                }
                else if (InputH.IsHeld(BackwardInput))
                {
                    Velocity.x = BackwardVector.x * characterConfig.BackSpeed;

                    SetState(CharacterState.BackWalk, frame, Frame.Infinity);
                }
                else
                {
                    Velocity.x = 0;

                    SetState(CharacterState.Idle, frame, Frame.Infinity);
                }

                if (
                    InputH.IsHeld(ForwardInput)
                    && InputH.PressedAndReleasedRecently(ForwardInput, config.Input.DashWindow, 1)
                )
                {
                    Velocity.x = ForwardVector.x * (characterConfig.ForwardDashDistance / config.ForwardDashTicks);

                    SetState(CharacterState.ForwardDash, frame, frame + config.ForwardDashTicks);
                    return;
                }
                if (
                    InputH.IsHeld(BackwardInput)
                    && InputH.PressedAndReleasedRecently(BackwardInput, config.Input.DashWindow, 1)
                )
                {
                    Velocity.x = BackwardVector.x * characterConfig.BackDashDistance / config.BackDashTicks;

                    SetState(CharacterState.BackDash, frame, frame + config.BackDashTicks);
                    return;
                }
            }
            else if (State == CharacterState.Jump || State == CharacterState.Falling)
            {
                if (Velocity.y < 0)
                {
                    SetState(CharacterState.Falling, frame, Frame.Infinity);
                }
                if (
                    InputH.IsHeld(ForwardInput)
                    && InputH.PressedAndReleasedRecently(ForwardInput, config.Input.DashWindow, 1)
                    && AirDashCount < characterConfig.NumAirDashes
                )
                {
                    AirDashCount += 1;
                    Velocity.x =
                        ForwardVector.x * (characterConfig.ForwardAirDashDistance / config.ForwardAirDashTicks);
                    Velocity.y = 0;

                    SetState(CharacterState.ForwardAirDash, frame, frame + config.ForwardAirDashTicks);
                    return;
                }

                if (
                    InputH.IsHeld(BackwardInput)
                    && InputH.PressedAndReleasedRecently(BackwardInput, config.Input.DashWindow, 1)
                    && AirDashCount < characterConfig.NumAirDashes
                )
                {
                    AirDashCount += 1;
                    Velocity.x = BackwardVector.x * (characterConfig.BackAirDashDistance / config.BackAirDashTicks);
                    Velocity.y = 0;

                    SetState(CharacterState.BackAirDash, frame, frame + config.BackAirDashTicks);
                    return;
                }
            }
        }

        private static Dictionary<(FighterAttackLocation, InputFlags), CharacterState> _attackDictionary =
            new Dictionary<(FighterAttackLocation, InputFlags), CharacterState>
            {
                { (FighterAttackLocation.Standing, InputFlags.LightAttack), CharacterState.LightAttack },
                { (FighterAttackLocation.Standing, InputFlags.MediumAttack), CharacterState.MediumAttack },
                { (FighterAttackLocation.Standing, InputFlags.HeavyAttack), CharacterState.SuperAttack },
                { (FighterAttackLocation.Crouching, InputFlags.LightAttack), CharacterState.LightCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.MediumAttack), CharacterState.MediumCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.HeavyAttack), CharacterState.SuperCrouching },
                { (FighterAttackLocation.Aerial, InputFlags.LightAttack), CharacterState.LightAerial },
                { (FighterAttackLocation.Aerial, InputFlags.MediumAttack), CharacterState.MediumAerial },
                { (FighterAttackLocation.Aerial, InputFlags.HeavyAttack), CharacterState.SuperAerial },
            };

        public void ApplyActiveState(Frame frame, Frame realFrame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (State == CharacterState.Hit)
            {
                if (InputH.IsHeld(InputFlags.Burst))
                {
                    Burst = 0;
                    SetState(
                        CharacterState.Burst,
                        frame,
                        frame + characterConfig.GetHitboxData(CharacterState.Burst).TotalTicks
                    );
                    // TODO: apply knockback to other player (this should be a hitbox on a burst animation with large kb)
                }
                return;
            }

            FrameData frameData = characterConfig.GetFrameData(State, frame - StateStart);
            bool isOnBeat = config.Audio.BeatWithinWindow(
                realFrame,
                BeatSubdivision.QuarterNote,
                windowFrames: config.Input.BeatCancelWindow
            );
            bool beatCancelEligible = frameData.FrameType == FrameType.Recovery && isOnBeat;

            bool dashCancelEligible =
                ((frame + config.ForwardDashCancelAfterTicks >= StateEnd) && State == CharacterState.ForwardDash)
                || ((frame + config.BackDashCancelAfterTicks >= StateEnd) && State == CharacterState.BackDash);

            if (!Actionable && !dashCancelEligible && !beatCancelEligible)
            {
                return;
            }

            Frame startFrame = frame;
            if (!Actionable && beatCancelEligible)
            {
                int frameDiff = config.Audio.ClosestBeat(frame, BeatSubdivision.QuarterNote) - realFrame;
                startFrame += frameDiff;
            }

            var attackLocation = AttackLocation(config);
            foreach (((var loc, var input), var state) in _attackDictionary)
            {
                if (InputH.PressedRecently(input, config.Input.InputBufferWindow) && attackLocation == loc)
                {
                    if (
                        attackLocation == FighterAttackLocation.Standing
                        || attackLocation == FighterAttackLocation.Crouching
                    )
                    {
                        Velocity = SVector2.zero;
                    }
                    SetState(state, startFrame, startFrame + characterConfig.GetHitboxData(state).TotalTicks, true);
                    return;
                }
            }

            if (
                dashCancelEligible
                && InputH.IsHeld(ForwardInput)
                && dashCancelEligible
                && State == CharacterState.ForwardDash
            )
            {
                SetState(CharacterState.Running, frame, Frame.Infinity);
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

        public void ApplyAerialCancel(Frame frame, GlobalConfig config, CharacterConfig characterConfig)
        {
            if (Location(config) != FighterLocation.Grounded)
            {
                return;
            }
            if (IsAerialAttack)
            {
                // TODO: apply some landing lag here
                SetState(
                    CharacterState.Landing,
                    frame,
                    frame + characterConfig.GetHitboxData(CharacterState.Landing).TotalTicks
                );
                return;
            }
            if (State == CharacterState.Falling)
            {
                SetState(
                    CharacterState.Landing,
                    frame,
                    frame + characterConfig.GetHitboxData(CharacterState.Landing).TotalTicks
                );
                return;
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
                SetState(
                    holdingDown ? CharacterState.BlockCrouch : CharacterState.BlockStand,
                    frame,
                    frame + props.BlockstunTicks + 1
                );

                ImmunityEnd = frame + 7;
                // TODO: check if other move is special, if so apply chip
                return new HitOutcome { Kind = HitKind.Blocked };
            }

            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            SetState(CharacterState.Hit, frame, frame + props.HitstunTicks + 1);

            // TODO: fixme, just to prevent multi hit
            ImmunityEnd = frame + 7;
            // TODO: if high enough, go knockdown
            Health -= props.Damage;

            Burst += props.Damage;
            Burst = Mathsf.Clamp(Burst, sfloat.Zero, config.BurstMax);

            Velocity = props.Knockback;

            ComboedCount++;
            return new HitOutcome { Kind = HitKind.Hit, Props = props };
        }

        public void ApplyClank(Frame frame, GlobalConfig config)
        {
            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            SetState(CharacterState.Hit, frame, frame + config.ClankTicks + 1);

            Velocity = SVector2.zero;
        }
    }
}
