using System;
using System.Collections.Generic;
using Design.Animation;
using Design.Configs;
using Game.View.Overlay;
using MemoryPack;
using UnityEngine;
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
        public int NumVictories;
        public bool GrabConnected;

        public int Index { get; private set; }
        public CharacterState State { get; private set; }
        public Frame StateStart { get; private set; }

        /// <summary>
        /// Set to a value that marks the first frame in which the character should return to neutral.
        /// </summary>
        public Frame StateEnd { get; private set; }

        public int ImmunityHash { get; private set; }

        public FighterFacing FacingDir;

        public Frame LocationSt { get; private set; }

        public BoxProps? HitProps { get; private set; }
        public SVector2? HitLocation { get; private set; }
        public bool StateChangedThisRealFrame { get; private set; }

        public bool HitLastRealFrame =>
            HitProps.HasValue
            && HitLocation.HasValue
            && (
                State == CharacterState.Death
                || State == CharacterState.Knockdown
                || State == CharacterState.Hit
                || State == CharacterState.Grabbed
            );

        public bool BlockedLastRealFrame =>
            HitProps.HasValue
            && HitLocation.HasValue
            && (State == CharacterState.BlockCrouch || State == CharacterState.BlockStand);

        public bool DashedLastRealFrame =>
            StateChangedThisRealFrame && (State == CharacterState.BackDash || State == CharacterState.ForwardDash);

        public SVector2 StoredJumpVelocity;

        public bool IsAerialAttack =>
            State == CharacterState.LightAerial
            || State == CharacterState.MediumAerial
            || State == CharacterState.SuperAerial
            || State == CharacterState.SpecialAerial;

        public bool IsAerial =>
            IsAerialAttack
            || State == CharacterState.Jump
            || State == CharacterState.PreJump
            || State == CharacterState.Falling;

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
            int index,
            sfloat health,
            SVector2 position,
            FighterFacing facingDirection,
            int lives
        )
        {
            FighterState state = new FighterState
            {
                Index = index,
                Position = position,
                Velocity = SVector2.zero,
                State = CharacterState.Idle,
                StateStart = Frame.FirstFrame,
                StateEnd = Frame.Infinity,
                ImmunityHash = 0,
                ComboedCount = 0,
                InputH = new InputHistory(),
                // TODO: character dependent?
                Health = health,
                FacingDir = facingDirection,
                Lives = lives,
                Burst = 0,
                AirDashCount = 0,
                Victories = new VictoryKind[lives],
                NumVictories = 0,
            };
            return state;
        }

        public void RoundReset(CharacterConfig config, SVector2 position, FighterFacing facingDirection)
        {
            Position = position;
            Velocity = SVector2.zero;
            State = CharacterState.Idle;
            StateStart = Frame.FirstFrame;
            StateEnd = Frame.Infinity;
            ImmunityHash = 0;
            ComboedCount = 0;
            InputH.Clear(); // Clear, don't want to read input from a previous round.
            // TODO: character dependent?
            Burst = 0;
            AirDashCount = 0;
            Health = config.Health;
            FacingDir = facingDirection;
        }

        public void DoFrameStart(GameOptions options)
        {
            if (Actionable)
            {
                ComboedCount = 0;
                if (options.Players[Index].HealOnActionable)
                {
                    Health = options.Players[Index].Character.Health;
                }
            }

            if (OnGround(options))
            {
                AirDashCount = 0;
            }
        }

        public bool OnGround(GameOptions options) => Position.y > options.Global.GroundY ? false : true;

        public FighterLocation Location => IsAerial ? FighterLocation.Airborne : FighterLocation.Grounded;

        public FighterAttackLocation AttackLocation
        {
            get
            {
                FighterLocation loc = Location;
                if (loc == FighterLocation.Airborne)
                {
                    return FighterAttackLocation.Aerial;
                }

                return InputH.IsHeld(InputFlags.Down)
                    ? FighterAttackLocation.Crouching
                    : FighterAttackLocation.Standing;
            }
        }

        public void ClearViewNotifiers()
        {
            HitProps = null;
            HitLocation = null;
            StateChangedThisRealFrame = false;
        }

        public void SetState(CharacterState nextState, Frame start, Frame end, bool forceChange = false)
        {
            if (State != nextState || forceChange)
            {
                State = nextState;
                StateStart = start;
                StateEnd = end;
                StateChangedThisRealFrame = true;
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

        public void TickStateMachine(Frame frame, GameOptions options)
        {
            if (State == CharacterState.Grab && GrabConnected)
            {
                bool backThrow = InputH.IsHeld(BackwardInput);
                if (backThrow)
                {
                    FacingDir = FacingDir == FighterFacing.Right ? FighterFacing.Left : FighterFacing.Right;
                }

                CharacterConfig config = options.Players[Index].Character;
                SetState(
                    CharacterState.Throw,
                    frame,
                    frame + config.GetHitboxData(CharacterState.Throw).TotalTicks,
                    true
                );
                GrabConnected = false;
                return;
            }

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

                if (OnGround(options))
                {
                    SetState(CharacterState.Idle, frame, Frame.Infinity);
                }
                else
                {
                    SetState(CharacterState.Falling, frame, Frame.Infinity);
                }
            }
        }

        public void ApplyMovementState(Frame frame, GameOptions options, bool isRhythmCancel, int beatOffset)
        {
            if (!Actionable && !isRhythmCancel)
            {
                return;
            }

            sfloat runMult = State == CharacterState.Running ? options.Global.RunningSpeedMultiplier : (sfloat)1f;
            CharacterConfig config = options.Players[Index].Character;

            Frame startFrame = frame;
            if (isRhythmCancel)
            {
                startFrame += -beatOffset + options.Global.Input.BeatCancelWindow;
            }

            bool DashInputs(InputFlags dirInput, ref FighterState self) =>
                (
                    self.InputH.IsHeld(dirInput)
                    && self.InputH.PressedAndReleasedRecently(dirInput, options.Global.Input.DashWindow, 1)
                )
                || (
                    self.InputH.IsHeld(dirInput)
                    && self.InputH.PressedRecently(InputFlags.Dash, options.Global.Input.InputBufferWindow)
                );

            if (GroundedActionable || isRhythmCancel)
            {
                if (InputH.IsHeld(InputFlags.Up))
                {
                    // Jump
                    if (InputH.PressedAndReleasedRecently(InputFlags.Down, options.Global.Input.SuperJumpWindow))
                    {
                        StoredJumpVelocity.y = config.JumpVelocity * options.Global.SuperJumpMultiplier;
                    }
                    else
                    {
                        StoredJumpVelocity.y = config.JumpVelocity;
                    }

                    if (InputH.IsHeld(ForwardInput))
                    {
                        StoredJumpVelocity.x = ForwardVector.x * config.ForwardSpeed * runMult;
                    }
                    else if (InputH.IsHeld(BackwardInput))
                    {
                        StoredJumpVelocity.x = BackwardVector.x * config.BackSpeed;
                    }
                    else
                    {
                        StoredJumpVelocity.x = 0;
                    }

                    Velocity = SVector2.zero;
                    Frame endFrame = frame + config.GetHitboxData(CharacterState.PreJump).TotalTicks;
                    if (isRhythmCancel)
                    {
                        endFrame = frame - beatOffset + options.Global.Input.BeatCancelWindow;
                    }

                    // handle the case when jump is pressed on the last frame
                    if (endFrame == frame)
                    {
                        Velocity = StoredJumpVelocity;
                        StoredJumpVelocity = SVector2.zero;
                        SetState(CharacterState.Jump, frame, Frame.Infinity);
                    }
                    else
                    {
                        SetState(CharacterState.PreJump, frame, endFrame);
                    }

                    return;
                }

                if (InputH.IsHeld(InputFlags.Down) && !isRhythmCancel)
                {
                    // Crouch
                    Velocity.x = 0;
                    SetState(CharacterState.Crouch, frame, Frame.Infinity);
                    return;
                }

                if (InputH.IsHeld(ForwardInput) && !isRhythmCancel)
                {
                    Velocity.x = ForwardVector.x * config.ForwardSpeed * runMult;

                    CharacterState nxtState =
                        State == CharacterState.Running ? CharacterState.Running : CharacterState.ForwardWalk;
                    SetState(nxtState, frame, Frame.Infinity);
                }
                else if (InputH.IsHeld(BackwardInput) && !isRhythmCancel)
                {
                    Velocity.x = BackwardVector.x * config.BackSpeed;

                    SetState(CharacterState.BackWalk, frame, Frame.Infinity);
                }
                else if (!isRhythmCancel)
                {
                    Velocity.x = 0;

                    SetState(CharacterState.Idle, frame, Frame.Infinity);
                }

                if (DashInputs(ForwardInput, ref this))
                {
                    Velocity.x = ForwardVector.x * (config.ForwardDashDistance / options.Global.ForwardDashTicks);

                    SetState(CharacterState.ForwardDash, startFrame, startFrame + options.Global.ForwardDashTicks);
                    return;
                }

                if (DashInputs(BackwardInput, ref this))
                {
                    Velocity.x = BackwardVector.x * config.BackDashDistance / options.Global.BackDashTicks;

                    SetState(CharacterState.BackDash, startFrame, startFrame + options.Global.BackDashTicks);
                    return;
                }
            }
            else if (State == CharacterState.Jump || State == CharacterState.Falling)
            {
                if (Velocity.y < 0)
                {
                    SetState(CharacterState.Falling, frame, Frame.Infinity);
                }

                if (DashInputs(ForwardInput, ref this) && AirDashCount < config.NumAirDashes)
                {
                    AirDashCount += 1;
                    Velocity.x = ForwardVector.x * (config.ForwardAirDashDistance / options.Global.ForwardAirDashTicks);
                    Velocity.y = 0;

                    SetState(
                        CharacterState.ForwardAirDash,
                        startFrame,
                        startFrame + options.Global.ForwardAirDashTicks
                    );
                    return;
                }

                if (DashInputs(BackwardInput, ref this) && AirDashCount < config.NumAirDashes)
                {
                    AirDashCount += 1;
                    Velocity.x = BackwardVector.x * (config.BackAirDashDistance / options.Global.BackAirDashTicks);
                    Velocity.y = 0;

                    SetState(CharacterState.BackAirDash, startFrame, startFrame + options.Global.BackAirDashTicks);
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
                { (FighterAttackLocation.Standing, InputFlags.SpecialAttack), CharacterState.SpecialAttack },
                { (FighterAttackLocation.Crouching, InputFlags.LightAttack), CharacterState.LightCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.MediumAttack), CharacterState.MediumCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.HeavyAttack), CharacterState.SuperCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.SpecialAttack), CharacterState.SpecialCrouching },
                { (FighterAttackLocation.Aerial, InputFlags.LightAttack), CharacterState.LightAerial },
                { (FighterAttackLocation.Aerial, InputFlags.MediumAttack), CharacterState.MediumAerial },
                { (FighterAttackLocation.Aerial, InputFlags.HeavyAttack), CharacterState.SuperAerial },
                { (FighterAttackLocation.Aerial, InputFlags.SpecialAttack), CharacterState.SpecialAerial },
                { (FighterAttackLocation.Standing, InputFlags.Grab), CharacterState.Grab },
                { (FighterAttackLocation.Crouching, InputFlags.Grab), CharacterState.Grab },
            };

        public void ApplyActiveState(
            Frame simFrame,
            GameOptions options,
            CharacterConfig config,
            bool isRhythmCancel,
            int beatOffset
        )
        {
            if (State == CharacterState.Hit)
            {
                if (InputH.IsHeld(InputFlags.Burst))
                {
                    Burst = 0;
                    SetState(
                        CharacterState.Burst,
                        simFrame,
                        simFrame + config.GetHitboxData(CharacterState.Burst).TotalTicks
                    );
                    // TODO: apply knockback to other player (this should be a hitbox on a burst animation with large kb)
                }

                return;
            }

            bool dashCancelEligible =
                (
                    (simFrame + options.Global.ForwardDashCancelAfterTicks >= StateEnd)
                    && State == CharacterState.ForwardDash
                )
                || (
                    (simFrame + options.Global.BackDashCancelAfterTicks >= StateEnd) && State == CharacterState.BackDash
                );

            if (!Actionable && !dashCancelEligible && !isRhythmCancel)
            {
                return;
            }

            int bufferWindow = options.Global.Input.InputBufferWindow;

            int[] frames = new int[HitboxData.ATTACK_FRAME_TYPE_ORDER.Length];
            foreach (((var loc, var input), var state) in _attackDictionary)
            {
                if (InputH.PressedRecently(input, bufferWindow) && AttackLocation == loc)
                {
                    if (
                        AttackLocation == FighterAttackLocation.Standing
                        || AttackLocation == FighterAttackLocation.Crouching
                    )
                    {
                        Velocity = SVector2.zero;
                    }

                    Frame startFrame = simFrame;
                    if (isRhythmCancel && config.GetHitboxData(state).IsValidAttack(frames))
                    {
                        // a negative beat offset means the input was early, which means we should start it later, so we negate beatoffset
                        startFrame += -beatOffset - frames[0] + options.Global.Input.BeatCancelWindow;
                    }

                    SetState(state, startFrame, startFrame + config.GetHitboxData(state).TotalTicks, true);
                    if (state == CharacterState.Grab)
                    {
                        GrabConnected = false;
                    }
                    return;
                }
            }

            if (dashCancelEligible && InputH.IsHeld(ForwardInput) && State == CharacterState.ForwardDash)
            {
                SetState(CharacterState.Running, simFrame, Frame.Infinity);
            }
        }

        public void UpdatePosition(Frame frame, GameOptions options, SVector2 otherFighterPos)
        {
            // Dash beat-snap: under rhythm cancel, ApplyMovementState pushes
            // a dash's StateStart to (noteTick + BeatCancelWindow), while the
            // velocity is set on the actual input frame. Integrating that
            // velocity before StateStart would give an extra (BeatCancelWindow
            // - beatOffset) frames of motion whose length varies with how
            // early/late the player hit the note. Gate the entire position
            // update to the dash's own [StateStart, StateEnd) window so the
            // dash only ever moves for exactly dashTicks frames, matching the
            // combo generator's beatOffset == 0 simulation.
            if (IsDash && (frame < StateStart || frame >= StateEnd))
            {
                return;
            }

            // Apply gravity if not grounded and not in airdash
            FrameData curData = options.Players[Index].Character.GetFrameData(State, frame - StateStart);
            if (curData.Floating)
            {
                Velocity /= options.Global.FloatingFactor;
            }

            if (curData.ShouldApplyVel)
            {
                Velocity = curData.ApplyVelocity;
                Velocity.x *= FacingDir == FighterFacing.Left ? -1 : 1;
            }

            if (curData.GravityEnabled && Position.y > options.Global.GroundY)
            {
                Velocity.y += options.Global.Gravity * 1 / GameManager.TPS;
            }

            // Update Position
            Position += Velocity * 1 / GameManager.TPS;

            // Floor collision
            if (Position.y <= options.Global.GroundY)
            {
                Position.y = options.Global.GroundY;

                if (Velocity.y < 0)
                    Velocity.y = 0;
            }

            sfloat cameraMaxBounds =
                otherFighterPos.x + 2 * (options.Global.CameraHalfWidth - options.Global.CameraPadding);
            sfloat cameraMinBounds =
                otherFighterPos.x - 2 * (options.Global.CameraHalfWidth - options.Global.CameraPadding);
            sfloat maxBounds = Mathsf.Min(options.Global.WallsX, cameraMaxBounds);
            sfloat minBounds = Mathsf.Max(-options.Global.WallsX, cameraMinBounds);
            if (Position.x >= maxBounds)
            {
                Position.x = maxBounds;
                if (Velocity.x > 0)
                    Velocity.x = 0;
            }

            if (Position.x <= minBounds)
            {
                Position.x = minBounds;
                if (Velocity.x < 0)
                    Velocity.x = 0;
            }
        }

        public void ApplyAerialCancel(Frame frame, GameOptions options, CharacterConfig config)
        {
            if (!OnGround(options))
            {
                return;
            }

            if (IsAerialAttack)
            {
                // TODO: apply some landing lag here
                SetState(
                    CharacterState.Landing,
                    frame,
                    frame + config.GetHitboxData(CharacterState.Landing).TotalTicks
                );
                return;
            }

            if (State == CharacterState.Knockdown)
            {
                // TODO: getup options
                SetState(CharacterState.Idle, frame, Frame.Infinity);
                return;
            }

            if (State == CharacterState.Falling)
            {
                SetState(
                    CharacterState.Landing,
                    frame,
                    frame + config.GetHitboxData(CharacterState.Landing).TotalTicks
                );
                return;
            }
        }

        public void AddBoxes(Frame frame, CharacterConfig config, Physics<BoxProps> physics, int handle)
        {
            // If the current state's StateStart is in the future (dash under
            // rhythm cancel waiting for its beat snap, etc.), don't add any
            // boxes at all. The negative tick would wraparound via modulo to
            // the tail of the animation and expose hurtboxes/pushboxes that
            // the fighter shouldn't have during the pre-state window. In
            // particular, a pushbox present here lets the other fighter
            // nudge them out of position before the dash even fires, which
            // is enough to throw off combo positioning.
            if (frame < StateStart)
            {
                return;
            }

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

        public HitOutcome ApplyHit(
            Frame frame,
            Frame attackSt,
            CharacterConfig characterConfig,
            BoxProps props,
            SVector2 location,
            sfloat damageMult
        )
        {
            int immunityVal = HashCode.Combine(props, attackSt);
            if (ImmunityHash == immunityVal)
            {
                return new HitOutcome { Kind = HitKind.None };
            }

            HitProps = props;
            HitLocation = location;
            ImmunityHash = immunityVal;

            bool holdingBack = InputH.IsHeld(BackwardInput);
            bool holdingDown = InputH.IsHeld(InputFlags.Down);

            bool standBlock = props.AttackKind != AttackKind.Low;
            bool crouchBlock = props.AttackKind != AttackKind.Overhead;
            bool blockSuccess = holdingBack && ((holdingDown && crouchBlock) || (!holdingDown && standBlock));

            if (
                blockSuccess
                && (Actionable || State == CharacterState.BlockCrouch || State == CharacterState.BlockStand)
            )
            {
                // True: Crouch blocking, False: Stand blocking
                SetState(
                    holdingDown ? CharacterState.BlockCrouch : CharacterState.BlockStand,
                    frame,
                    frame + props.BlockstunTicks,
                    true
                );

                // TODO: check if other move is special, if so apply chip
                return new HitOutcome { Kind = HitKind.Blocked };
            }

            switch (props.KnockdownKind)
            {
                case KnockdownKind.None:
                    SetState(CharacterState.Hit, frame, frame + props.HitstunTicks, true);
                    break;
                case KnockdownKind.Light:
                    SetState(CharacterState.Knockdown, frame, Frame.Infinity, true);
                    break;
            }

            // TODO: fixme, just to prevent multi hit
            // TODO: if high enough, go knockdown
            Health -= props.Damage * damageMult;

            Burst += props.Damage * damageMult;
            Burst = Mathsf.Clamp(Burst, sfloat.Zero, characterConfig.BurstMax);

            Velocity = props.Knockback;

            ComboedCount++;
            return new HitOutcome { Kind = HitKind.Hit, Props = props };
        }

        public void ApplyGrab(Frame frame, BoxProps props, SVector2 hitboxCenter, ref FighterState attacker)
        {
            if (State != CharacterState.Grabbed)
            {
                ComboedCount++;
            }
            SetState(CharacterState.Grabbed, frame, Frame.Infinity);
            Velocity = SVector2.zero;

            SVector2 grabPos = props.GrabPosition;
            if (attacker.FacingDir == FighterFacing.Left)
            {
                grabPos.x *= -1;
            }

            Position = hitboxCenter + grabPos;
            attacker.GrabConnected = true;
        }

        public void ApplyClank(Frame frame, GameOptions options)
        {
            SetState(CharacterState.Hit, frame, frame + options.Global.ClankTicks);

            Velocity = SVector2.zero;
        }
    }
}
