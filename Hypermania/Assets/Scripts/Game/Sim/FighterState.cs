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
        public CharacterState[] StalingBuffer;
        public int StalingBufferIndex;
        public InputHistory InputH;
        public int Lives;
        public sfloat Super;
        public sfloat Burst;
        public int AirDashCount;
        public VictoryKind[] Victories;
        public int NumVictories;
        public bool AttackConnected;
        public bool IsSuperAttack;
        public int SuperComboBeats;

        /// <summary>
        /// Set when this fighter is in hitstun while a mania is active. Keeps
        /// the fighter treated as non-actionable (and prevents the combo count
        /// from resetting) even after <see cref="TickStateMachine"/> returns
        /// them to <see cref="CharacterState.Idle"/>, until the mania ends.
        /// Cleared when the mania ends or the round resets.
        /// </summary>
        public bool LockedHitstun;

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
        public bool SuperMaxedThisRealFrame { get; private set; }
        public CharacterState? PostActionState { get; private set; }
        public Frame? PostActionStateStart { get; private set; }

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

        /// <summary>
        /// Whether the fighter is actionable this frame. Mirrors
        /// <see cref="CharacterStateExtensions.IsActionable"/> but additionally
        /// returns false while <see cref="LockedHitstun"/> is set, so a fighter
        /// whose hitstun rolled over into a mania stays locked down (no new
        /// inputs, no combo reset, no blocking) until the mania ends.
        /// </summary>
        public bool Actionable => !LockedHitstun && State.IsActionable();

        public SVector2 StoredJumpVelocity;

        public SVector2 ForwardVector => FacingDir == FighterFacing.Left ? SVector2.left : SVector2.right;
        public SVector2 BackwardVector => FacingDir == FighterFacing.Left ? SVector2.right : SVector2.left;
        public InputFlags ForwardInput => FacingDir == FighterFacing.Left ? InputFlags.Left : InputFlags.Right;
        public InputFlags BackwardInput => FacingDir == FighterFacing.Left ? InputFlags.Right : InputFlags.Left;

        public static FighterState Create(
            int index,
            sfloat health,
            SVector2 position,
            FighterFacing facingDirection,
            int lives,
            int stalingBufferSize
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
                StalingBuffer = new CharacterState[stalingBufferSize],
                StalingBufferIndex = 0,
                InputH = new InputHistory(),
                // TODO: character dependent?
                Health = health,
                FacingDir = facingDirection,
                Lives = lives,
                Burst = 0,
                Super = 0,
                AirDashCount = 0,
                Victories = new VictoryKind[lives],
                NumVictories = 0,
                LockedHitstun = false,
                IsSuperAttack = false,
            };
            return state;
        }

        public static FighterState CreateForDisplay(
            CharacterState animState,
            Frame stateStart,
            SVector2 position,
            FighterFacing facing
        )
        {
            return new FighterState
            {
                Position = position,
                FacingDir = facing,
                State = animState,
                StateStart = stateStart,
                StateEnd = Frame.Infinity,
            };
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
            Array.Clear(StalingBuffer, 0, StalingBuffer.Length);
            StalingBufferIndex = 0;
            LockedHitstun = false;
            InputH.Clear(); // Clear, don't want to read input from a previous round.
            // TODO: character dependent?
            IsSuperAttack = false;
            Burst = 0;
            Super = 0;
            AirDashCount = 0;
            Health = config.Health;
            FacingDir = facingDirection;
        }

        public void DoFrameStart(GameOptions options, bool maniaActive)
        {
            // Latch the mania hitstun lock: if this fighter is currently in
            // hitstun while a mania is running, they must remain treated as
            // non-actionable for the rest of the mania even after
            // TickStateMachine transitions them out of CharacterState.Hit.
            // Must run before TickStateMachine — otherwise a fighter whose
            // hitstun ends this frame would transition to Idle and be missed.
            if (maniaActive && State == CharacterState.Hit)
            {
                LockedHitstun = true;
            }

            if (OnGround(options))
            {
                AirDashCount = 0;
            }
        }

        /// <summary>
        /// Applies the actionable-gated resets (combo count, heal-on-actionable,
        /// super/burst max-on-actionable). Must run after <see cref="TickStateMachine"/>
        /// so <see cref="Actionable"/> reflects the state the fighter will act
        /// from this frame — otherwise a fighter whose stun ends this frame
        /// would be seen as non-actionable and skip the resets even though
        /// they're about to process input normally.
        /// </summary>
        public void ApplyActionableFrameResets(GameOptions options, GameMode gameMode)
        {
            if (!Actionable)
            {
                return;
            }

            ComboedCount = 0;
            if (options.Players[Index].HealOnActionable)
            {
                Health = options.Players[Index].Character.Health;
            }
            if (options.Players[Index].SuperMaxOnActionable && gameMode == GameMode.Fighting)
            {
                Super = options.Global.SuperMax;
            }
            if (options.Players[Index].BurstMaxOnActionable)
            {
                Burst = options.Players[Index].Character.BurstMax;
            }
        }

        public bool OnGround(GameOptions options) => Position.y > options.Global.GroundY ? false : true;

        public FighterLocation Location => State.IsGrounded() ? FighterLocation.Grounded : FighterLocation.Airborne;

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
            SuperMaxedThisRealFrame = false;
            PostActionState = null;
            PostActionStateStart = null;
        }

        public void CapturePostActionState()
        {
            PostActionState = State;
            PostActionStateStart = StateStart;
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
            // if animation ends, switch back to idle
            if (frame >= StateEnd)
            {
                IsSuperAttack = false;

                // TODO: is best place here?
                if (State.IsDash())
                {
                    Velocity.x = 0;
                }
                if (State == CharacterState.Hit)
                {
                    Velocity = SVector2.zero;
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

        public void ApplyMovementState(Frame frame, GameOptions options, bool isRhythmCancel)
        {
            CharacterConfig config = options.Players[Index].Character;
            sfloat runMult = State == CharacterState.Running ? options.Global.RunningSpeedMultiplier : (sfloat)1f;

            bool gatlingPreJumpAllowed =
                (AttackLocation == FighterAttackLocation.Standing || AttackLocation == FighterAttackLocation.Crouching)
                && InputH.IsHeld(InputFlags.Up)
                && IsGatlingCancelAllowed(CharacterState.PreJump, frame, config);

            if (gatlingPreJumpAllowed)
            {
                TriggerPreJump(frame, options, isRhythmCancel, runMult);
                return;
            }

            if (!Actionable && !isRhythmCancel)
            {
                return;
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

            if (State.IsGroundedActionable() || (State.IsGrounded() && isRhythmCancel))
            {
                if (InputH.IsHeld(InputFlags.Up))
                {
                    TriggerPreJump(frame, options, isRhythmCancel, runMult);
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
                    SetState(CharacterState.ForwardDash, frame, frame + options.Global.ForwardDashTicks);
                    return;
                }

                if (DashInputs(BackwardInput, ref this))
                {
                    SetState(CharacterState.BackDash, frame, frame + options.Global.BackDashTicks);
                    return;
                }
            }
            else if (
                State == CharacterState.Jump
                || State == CharacterState.Falling
                || (State.IsAerial() && isRhythmCancel)
            )
            {
                if (Velocity.y < 0)
                {
                    SetState(CharacterState.Falling, frame, Frame.Infinity);
                }

                if (DashInputs(ForwardInput, ref this) && AirDashCount < config.NumAirDashes)
                {
                    AirDashCount += 1;
                    SetState(CharacterState.ForwardAirDash, frame, frame + options.Global.ForwardAirDashTicks);
                    return;
                }

                if (DashInputs(BackwardInput, ref this) && AirDashCount < config.NumAirDashes)
                {
                    AirDashCount += 1;
                    SetState(CharacterState.BackAirDash, frame, frame + options.Global.BackAirDashTicks);
                    return;
                }
            }
        }

        private void TriggerPreJump(Frame frame, GameOptions options, bool isRhythmCancel, sfloat runMult)
        {
            CharacterConfig config = options.Players[Index].Character;

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
            AttackConnected = false;

            // Rhythm-cancel jumps skip PreJump wind-up and launch immediately.
            if (isRhythmCancel)
            {
                Velocity = StoredJumpVelocity;
                StoredJumpVelocity = SVector2.zero;
                SetState(CharacterState.Jump, frame, Frame.Infinity);
                return;
            }

            SetState(CharacterState.PreJump, frame, frame + config.GetHitboxData(CharacterState.PreJump).TotalTicks);
        }

        private static Dictionary<(FighterAttackLocation, InputFlags), CharacterState> _attackDictionary =
            new Dictionary<(FighterAttackLocation, InputFlags), CharacterState>
            {
                { (FighterAttackLocation.Standing, InputFlags.LightAttack), CharacterState.LightAttack },
                { (FighterAttackLocation.Standing, InputFlags.MediumAttack), CharacterState.MediumAttack },
                { (FighterAttackLocation.Standing, InputFlags.HeavyAttack), CharacterState.HeavyAttack },
                { (FighterAttackLocation.Standing, InputFlags.SpecialAttack), CharacterState.SpecialAttack },
                { (FighterAttackLocation.Crouching, InputFlags.LightAttack), CharacterState.LightCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.MediumAttack), CharacterState.MediumCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.HeavyAttack), CharacterState.HeavyCrouching },
                { (FighterAttackLocation.Crouching, InputFlags.SpecialAttack), CharacterState.SpecialCrouching },
                { (FighterAttackLocation.Aerial, InputFlags.LightAttack), CharacterState.LightAerial },
                { (FighterAttackLocation.Aerial, InputFlags.MediumAttack), CharacterState.MediumAerial },
                { (FighterAttackLocation.Aerial, InputFlags.HeavyAttack), CharacterState.HeavyAerial },
                { (FighterAttackLocation.Aerial, InputFlags.SpecialAttack), CharacterState.SpecialAerial },
                { (FighterAttackLocation.Standing, InputFlags.Grab), CharacterState.Grab },
                { (FighterAttackLocation.Crouching, InputFlags.Grab), CharacterState.Grab },
            };

        public void ApplyActiveState(
            Frame simFrame,
            GameOptions options,
            CharacterConfig config,
            bool isRhythmCancel,
            GameMode gameMode
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

            int bufferWindow = options.Global.Input.InputBufferWindow;

            // Followup attacks:
            HitboxData curData = config.GetHitboxData(State);
            FrameData curFrameData = curData.GetFrame(simFrame - StateStart);
            if (
                curData.Followup != CharacterState.Idle
                && curFrameData.FrameType == FrameType.Recovery
                && InputH.IsHeld(curData.FollowupInput)
            )
            {
                // TODO: fixme copied code from previous
                Frame startFrame = simFrame;
                if (isRhythmCancel)
                {
                    // Beat-snap: back-date StateStart by the attack's startup
                    // so the active frame lands on simFrame itself (= the
                    // note's dispatch frame, since ManiaState withholds the
                    // press to the last frame of the hit window).
                    startFrame -= config.GetHitboxData(curData.Followup).StartupTicks;
                }
                AttackConnected = false;
                SetState(
                    curData.Followup,
                    startFrame,
                    startFrame + config.GetHitboxData(curData.Followup).TotalTicks,
                    true
                );
                return;
            }

            // Hold-to-super: once the heavy attack has been active for
            // SuperDelayWindow ticks, promote to a super if the heavy button
            // is still held at that frame.
            int superDelayWindow = options.Global.Input.SuperDelayWindow;
            bool isHeavyAttackState =
                State == CharacterState.HeavyAttack
                || State == CharacterState.HeavyAerial
                || State == CharacterState.HeavyCrouching;
            if (
                isHeavyAttackState
                && !IsSuperAttack
                && InputH.IsHeld(InputFlags.HeavyAttack)
                && simFrame - StateStart == superDelayWindow
                && gameMode == GameMode.Fighting
            )
            {
                sfloat superCost = options.Global.SuperCost;
                if (Super >= superCost + superCost)
                {
                    IsSuperAttack = true;
                    SuperComboBeats = options.Global.SuperTier2Beats;
                }
                else if (Super >= superCost)
                {
                    IsSuperAttack = true;
                    SuperComboBeats = options.Global.SuperTier1Beats;
                }
            }

            bool dashCancelEligible =
                (
                    (simFrame + options.Global.ForwardDashCancelAfterTicks >= StateEnd)
                    && State == CharacterState.ForwardDash
                )
                || (
                    (simFrame + options.Global.BackDashCancelAfterTicks >= StateEnd) && State == CharacterState.BackDash
                );

            bool canActNormally = Actionable || dashCancelEligible || isRhythmCancel;

            int[] frames = new int[HitboxData.ATTACK_FRAME_TYPE_ORDER.Length];
            foreach (((var loc, var input), var state) in _attackDictionary)
            {
                if (!(InputH.PressedRecently(input, bufferWindow) && AttackLocation == loc))
                {
                    continue;
                }
                if (!canActNormally && !IsGatlingCancelAllowed(state, simFrame, config))
                {
                    continue;
                }

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
                    // Beat-snap: back-date StateStart by the attack's startup
                    // so the active frame lands on simFrame itself (= the
                    // note's dispatch frame, since ManiaState withholds the
                    // press to the last frame of the hit window).
                    startFrame -= frames[0];
                }

                AttackConnected = false;
                SetState(state, startFrame, startFrame + config.GetHitboxData(state).TotalTicks, true);
                return;
            }

            if (dashCancelEligible && InputH.IsHeld(ForwardInput) && State == CharacterState.ForwardDash)
            {
                SetState(CharacterState.Running, simFrame, Frame.Infinity);
            }
        }

        private bool IsGatlingCancelAllowed(CharacterState to, Frame simFrame, CharacterConfig config)
        {
            if (!AttackConnected)
                return false;
            if (!config.HasGatling(State, to))
                return false;

            HitboxData fromData = config.GetHitboxData(State);
            int total = fromData.StartupTicks + fromData.ActiveTicks + fromData.RecoveryTicks;
            if (total == 0)
                return false;

            int ticksIntoState = simFrame - StateStart;
            int recoveryStart = fromData.StartupTicks + fromData.ActiveTicks;
            int recoveryEnd = total;
            if (ticksIntoState < recoveryStart || ticksIntoState >= recoveryEnd)
                return false;

            HitboxData toData = config.GetHitboxData(to);

            int cancelWindow;
            if (toData.StartupTicks == 0)
            {
                cancelWindow = fromData.RecoveryTicks;
            }
            else
            {
                cancelWindow = Math.Max(0, toData.StartupTicks - fromData.OnHitAdvantage + 1);
            }
            return ticksIntoState >= recoveryEnd - cancelWindow;
        }

        public void UpdatePosition(Frame frame, GameOptions options, SVector2 otherFighterPos)
        {
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

            if (curData.ShouldTeleport)
            {
                SVector2 teleport = curData.TeleportLocation;
                teleport.x *= FacingDir == FighterFacing.Left ? -1 : 1;
                Position += teleport;
            }

            if (curData.GravityEnabled && Position.y > options.Global.GroundY)
            {
                Velocity.y += options.Global.Gravity * 1 / GameManager.TPS;
            }

            CharacterConfig config = options.Players[Index].Character;
            switch (State)
            {
                case CharacterState.BackAirDash:
                    Velocity.x = BackwardVector.x * (config.BackAirDashDistance / options.Global.BackAirDashTicks);
                    Velocity.y = 0;
                    break;
                case CharacterState.ForwardAirDash:
                    Velocity.x = ForwardVector.x * (config.ForwardAirDashDistance / options.Global.ForwardAirDashTicks);
                    Velocity.y = 0;
                    break;
                case CharacterState.BackDash:
                    Velocity.x = BackwardVector.x * (config.BackDashDistance / options.Global.BackDashTicks);
                    Velocity.y = 0;
                    break;
                case CharacterState.ForwardDash:
                    Velocity.x = ForwardVector.x * (config.ForwardDashDistance / options.Global.ForwardDashTicks);
                    Velocity.y = 0;
                    break;
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

            if (State.IsAerialAttack())
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

        public void ProcessHit(Frame frame, BoxProps props, CharacterConfig config)
        {
            if (props.HasTransition)
            {
                // ProcessHit runs inside DoCollisionStep, AFTER AddBoxes has
                // already added this frame's boxes for the pre-transition
                // state. If we used `frame` as StateStart, the next frame
                // would read the transitioned animation at tick 1 — skipping
                // tick 0 entirely. Back-date by using frame+1 so tick 0 is
                // what gets rendered/processed on the following frame.
                Frame nextStart = frame + 1;
                if (props.OnHitTransition == CharacterState.Throw)
                {
                    bool backThrow = InputH.IsHeld(BackwardInput);
                    if (backThrow)
                    {
                        FacingDir = FacingDir == FighterFacing.Right ? FighterFacing.Left : FighterFacing.Right;
                    }

                    SetState(
                        CharacterState.Throw,
                        nextStart,
                        nextStart + config.GetHitboxData(CharacterState.Throw).TotalTicks,
                        true
                    );
                    return;
                }
                SetState(
                    props.OnHitTransition,
                    nextStart,
                    nextStart + config.GetHitboxData(props.OnHitTransition).TotalTicks,
                    true
                );
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

                Velocity = new SVector2(props.Knockback.x * (sfloat)0.5f, sfloat.Zero);

                // TODO: check if other move is special, if so apply chip
                return new HitOutcome { Kind = HitKind.Blocked, Props = props };
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

        public void ApplyGrab(Frame frame, BoxProps props, SVector2 hitboxCenter, FighterFacing grabberFacingDir)
        {
            if (State != CharacterState.Grabbed)
            {
                ComboedCount++;
            }
            SetState(CharacterState.Grabbed, frame, Frame.Infinity);
            Velocity = SVector2.zero;

            SVector2 grabPos = props.GrabPosition;
            if (grabberFacingDir == FighterFacing.Left)
            {
                grabPos.x *= -1;
            }

            Position = hitboxCenter + grabPos;
        }

        public void ApplyClank(Frame frame, GameOptions options)
        {
            SetState(CharacterState.Hit, frame, frame + options.Global.ClankTicks);

            Velocity = SVector2.zero;
        }

        public void AddSuper(sfloat amount, GameOptions options)
        {
            sfloat max = options.Global.SuperMax;
            sfloat cost = options.Global.SuperCost;
            sfloat doubleCost = cost + cost;
            sfloat prevSuper = Super;
            Super += amount;
            Super = Mathsf.Min(Super, max);
            bool crossedTier1 = prevSuper < cost && Super >= cost;
            bool crossedTier2 = prevSuper < doubleCost && Super >= doubleCost;
            if (crossedTier1 || crossedTier2)
            {
                SuperMaxedThisRealFrame = true;
            }
        }
    }
}
