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
        public sfloat Super;
        public sfloat Burst;
        public int AirDashCount;
        public VictoryKind[] Victories;
        public int NumVictories;
        public bool GrabConnected;
        public bool IsSuperAttack;

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

        /// <summary>
        /// Last frame of the note input window for the note that triggered
        /// the current state's rhythm cancel (i.e.
        /// `noteTick + BeatCancelWindow`). Used to decouple combo mechanics
        /// from where inside the input window the player actually hit the
        /// note: regardless of how early or late the press lands, the
        /// state's frame-data effects and hit/hurt/pushboxes don't come
        /// online until this frame, so two different presses on the same
        /// note produce identical combo behaviour.
        /// <see cref="Frame.NullFrame"/> when the current state was not
        /// rhythm canceled.
        /// </summary>
        public Frame RhythmCancelInputEnd { get; private set; }

        public int ImmunityHash { get; private set; }

        public FighterFacing FacingDir;

        public Frame LocationSt { get; private set; }

        public BoxProps? HitProps { get; private set; }
        public SVector2? HitLocation { get; private set; }
        public bool StateChangedThisRealFrame { get; private set; }
        public bool SuperMaxedThisRealFrame { get; private set; }

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
                RhythmCancelInputEnd = Frame.NullFrame,
                ImmunityHash = 0,
                ComboedCount = 0,
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
                RhythmCancelInputEnd = Frame.NullFrame,
            };
        }

        public void RoundReset(CharacterConfig config, SVector2 position, FighterFacing facingDirection)
        {
            Position = position;
            Velocity = SVector2.zero;
            State = CharacterState.Idle;
            StateStart = Frame.FirstFrame;
            StateEnd = Frame.Infinity;
            RhythmCancelInputEnd = Frame.NullFrame;
            ImmunityHash = 0;
            ComboedCount = 0;
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
            if (maniaActive && State == CharacterState.Hit)
            {
                LockedHitstun = true;
            }

            if (Actionable)
            {
                ComboedCount = 0;
                if (options.Players[Index].HealOnActionable)
                {
                    Health = options.Players[Index].Character.Health;
                }
                if (options.Players[Index].SuperMaxOnActionable)
                {
                    Super = options.Players[Index].Character.SuperMax;
                }
                if (options.Players[Index].BurstMaxOnActionable)
                {
                    Burst = options.Players[Index].Character.BurstMax;
                }
            }

            if (OnGround(options))
            {
                AirDashCount = 0;
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
        }

        public void SetState(CharacterState nextState, Frame start, Frame end, bool forceChange = false)
        {
            if (State != nextState || forceChange)
            {
                State = nextState;
                StateStart = start;
                StateEnd = end;
                // Clear any prior rhythm-cancel input window. Callers that
                // entered this state under rhythm cancel reassign
                // RhythmCancelInputEnd immediately after SetState.
                RhythmCancelInputEnd = Frame.NullFrame;
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

        public void ApplyMovementState(Frame frame, GameOptions options, bool isRhythmCancel, int beatOffset)
        {
            if (!Actionable && !isRhythmCancel)
            {
                return;
            }

            sfloat runMult = State == CharacterState.Running ? options.Global.RunningSpeedMultiplier : (sfloat)1f;
            CharacterConfig config = options.Players[Index].Character;

            Frame startFrame = frame;
            // Absolute frame of the end of the note's input window
            // (`noteTick + BeatCancelWindow`). A rhythm-canceled state
            // records this so its mechanics all unfold from this single
            // point, making the resulting combo independent of where in the
            // input window the player actually pressed.
            Frame rhythmCancelInputEnd = frame + (-beatOffset + (int)options.Players[Index].BeatCancelWindow);
            if (isRhythmCancel)
            {
                startFrame = rhythmCancelInputEnd;
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
                        endFrame = frame - beatOffset + (int)options.Players[Index].BeatCancelWindow;
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
                    if (isRhythmCancel)
                    {
                        RhythmCancelInputEnd = rhythmCancelInputEnd;
                    }
                    return;
                }

                if (DashInputs(BackwardInput, ref this))
                {
                    Velocity.x = BackwardVector.x * config.BackDashDistance / options.Global.BackDashTicks;

                    SetState(CharacterState.BackDash, startFrame, startFrame + options.Global.BackDashTicks);
                    if (isRhythmCancel)
                    {
                        RhythmCancelInputEnd = rhythmCancelInputEnd;
                    }
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
                    Velocity.x = ForwardVector.x * (config.ForwardAirDashDistance / options.Global.ForwardAirDashTicks);
                    Velocity.y = 0;

                    SetState(
                        CharacterState.ForwardAirDash,
                        startFrame,
                        startFrame + options.Global.ForwardAirDashTicks
                    );
                    if (isRhythmCancel)
                    {
                        RhythmCancelInputEnd = rhythmCancelInputEnd;
                    }
                    return;
                }

                if (DashInputs(BackwardInput, ref this) && AirDashCount < config.NumAirDashes)
                {
                    AirDashCount += 1;
                    Velocity.x = BackwardVector.x * (config.BackAirDashDistance / options.Global.BackAirDashTicks);
                    Velocity.y = 0;

                    SetState(CharacterState.BackAirDash, startFrame, startFrame + options.Global.BackAirDashTicks);
                    if (isRhythmCancel)
                    {
                        RhythmCancelInputEnd = rhythmCancelInputEnd;
                    }
                    return;
                }
            }
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
            int beatOffset,
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

            // Double-tap heavy: if in a heavy attack and heavy pressed again within the super window, mark as super
            int superWindow = options.Global.Input.SuperAttackWindow;
            bool isHeavyAttackState =
                State == CharacterState.HeavyAttack
                || State == CharacterState.HeavyAerial
                || State == CharacterState.HeavyCrouching;
            if (
                isHeavyAttackState
                && !IsSuperAttack
                && InputH.PressedRecently(InputFlags.HeavyAttack, bufferWindow)
                && simFrame - StateStart > bufferWindow
                && simFrame - StateStart <= superWindow
                && Super >= options.Players[Index].Character.SuperMax
                && gameMode == GameMode.Fighting
            )
            {
                IsSuperAttack = true;
                Super = 0;
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
                    bool rhythmCancelAdjusted = false;
                    if (isRhythmCancel && config.GetHitboxData(state).IsValidAttack(frames))
                    {
                        // a negative beat offset means the input was early, which means we should start it later, so we negate beatoffset
                        startFrame += -beatOffset - frames[0] + (int)options.Players[Index].BeatCancelWindow;
                        rhythmCancelAdjusted = true;
                    }

                    SetState(state, startFrame, startFrame + config.GetHitboxData(state).TotalTicks, true);
                    if (rhythmCancelAdjusted)
                    {
                        // Record the end of the note's input window
                        // (`simFrame + (-beatOffset + BeatCancelWindow)`) so
                        // the attack's frame-data effects and hit/hurtboxes
                        // all come online from the same absolute frame no
                        // matter where inside the window the player pressed.
                        // Two different-timed presses on the same note then
                        // produce identical combo behaviour.
                        RhythmCancelInputEnd = simFrame + (-beatOffset + (int)options.Players[Index].BeatCancelWindow);
                    }
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
            if (State.IsDash() && (frame < StateStart || frame >= StateEnd))
            {
                return;
            }

            // Frame-data-driven velocity / gravity / floating effects are
            // suppressed while a rhythm-canceled state is still inside the
            // note's input window (i.e. `frame < RhythmCancelInputEnd`).
            // The point is to keep combo mechanics independent of when
            // inside the window the player actually hit the note: early and
            // late presses on the same note should produce exactly the same
            // frame-data progression afterwards, so the combo simulator's
            // predictions stay correct regardless of player timing.
            bool rhythmCancelLockout = RhythmCancelInputEnd != Frame.NullFrame && frame < RhythmCancelInputEnd;
            if (!rhythmCancelLockout)
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

                if (curData.GravityEnabled && Position.y > options.Global.GroundY)
                {
                    Velocity.y += options.Global.Gravity * 1 / GameManager.TPS;
                }
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
            // Emit no boxes while a rhythm-canceled state is still inside
            // the note's input window. This keeps combo mechanics
            // independent of player timing within the window: early and
            // late presses on the same note both produce the same
            // hit/hurt/pushbox timeline relative to the note, so the combo
            // simulator's predictions hold regardless of where in the
            // window the player actually pressed. (Without this, a pushbox
            // appearing mid-window can let the opponent nudge the fighter
            // out of position before the move fires, and the shift depends
            // on press timing.)
            if (RhythmCancelInputEnd != Frame.NullFrame && frame < RhythmCancelInputEnd)
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

        public void AddSuper(sfloat amount, GameOptions options)
        {
            sfloat max = options.Players[Index].Character.SuperMax;
            bool wasBelowMax = Super < max;
            Super += amount;
            Super = Mathsf.Min(Super, max);
            if (wasBelowMax && Super >= max)
            {
                SuperMaxedThisRealFrame = true;
            }
        }
    }
}
