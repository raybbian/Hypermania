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

        // True when the fighter is in hitstun while a mania is running. Keeps
        // them non-actionable and stops the combo counter from resetting,
        // even after TickStateMachine returns them to Idle. Cleared when the
        // mania ends or the round resets.
        public bool LockedHitstun;

        // Inclusive last frame inputs are forced to None. NullFrame = no lock.
        public Frame InputLockUntil;

        public bool RhythmComboFinisherActive;
        public bool RhythmComboTier2;

        public bool FreestyleActive;

        // Damage multiplier the next attack consumes. Builds up from rhythm
        // no-op beats during a mania: each no-op takes half of
        // NoOpBonusRemaining and adds it here, so after n no-ops the value is
        // 1 + 0.25 * (1 - 0.5^n), which approaches 1.25 but never reaches it.
        // Resets to 1 on hit consumption, mania end, mania miss, and round
        // reset.
        public sfloat NoOpBonus;

        // Budget left to feed into NoOpBonus. Starts at 0.25, halves on each
        // no-op, so the total of all contributions stays under 0.25 (the
        // geometric series 1/2 + 1/4 + ... = 1, scaled by 0.25).
        public sfloat NoOpBonusRemaining;

        public int Index { get; private set; }
        public CharacterState State { get; private set; }
        public Frame StateStart { get; private set; }

        // First frame on which the character should return to neutral.
        public Frame StateEnd { get; private set; }

        public int ImmunityHash { get; private set; }

        public FighterFacing FacingDir;

        public Frame LocationSt { get; private set; }

        public BoxProps? HitProps { get; private set; }
        public SVector2? HitLocation { get; private set; }
        public SVector2? ClankLocation { get; private set; }
        public bool CurrentGrabTechable { get; private set; }
        public bool GrabTechedThisRealFrame { get; private set; }
        public bool StateChangedThisRealFrame { get; private set; }
        public bool SuperTier1MaxedThisRealFrame { get; private set; }
        public bool SuperTier2MaxedThisRealFrame { get; private set; }
        public CharacterState? PostActionState { get; private set; }
        public Frame? PostActionStateStart { get; private set; }

        // Attacker-side OnHitTransition that ProcessHit queues on frame F.
        // ApplyPendingHitTransition runs the real SetState at the start of
        // F+1, so the rest of frame F still sees the old state and the new
        // one (Throw, etc.) first appears on F+1.
        public CharacterState? PendingHitState { get; private set; }
        public Frame? PendingHitStateStart { get; private set; }
        public Frame? PendingHitStateEnd { get; private set; }
        public bool PendingHitStateForce { get; private set; }
        public KnockdownKind PendingKnockdown { get; private set; }

        public bool HitLastRealFrame =>
            HitProps.HasValue
            && HitLocation.HasValue
            && (
                State == CharacterState.Death
                || State == CharacterState.SoftKnockdown
                || State == CharacterState.HeavyKnockdown
                || State == CharacterState.Hit
                || State == CharacterState.Grabbed
            );

        public bool BlockedLastRealFrame =>
            HitProps.HasValue
            && HitLocation.HasValue
            && (State == CharacterState.BlockCrouch || State == CharacterState.BlockStand);

        public bool DashedLastRealFrame =>
            StateChangedThisRealFrame && (State == CharacterState.BackDash || State == CharacterState.ForwardDash);

        // Same as CharacterStateExtensions.IsActionable, but also returns
        // false while LockedHitstun is set. A fighter whose hitstun rolled
        // into a mania stays locked down (no inputs, no combo reset, no
        // blocking) until the mania ends.
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
            int stalingBufferSize,
            sfloat startingBurst
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
                Burst = startingBurst,
                Super = 0,
                AirDashCount = 0,
                Victories = new VictoryKind[lives],
                NumVictories = 0,
                LockedHitstun = false,
                InputLockUntil = Frame.NullFrame,
                IsSuperAttack = false,
                RhythmComboFinisherActive = false,
                RhythmComboTier2 = false,
                FreestyleActive = false,
                NoOpBonus = sfloat.One,
                NoOpBonusRemaining = (sfloat)0.25f,
                PendingKnockdown = KnockdownKind.None,
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
            InputLockUntil = Frame.NullFrame;
            RhythmComboFinisherActive = false;
            RhythmComboTier2 = false;
            FreestyleActive = false;
            NoOpBonus = sfloat.One;
            NoOpBonusRemaining = (sfloat)0.25f;
            PendingHitState = null;
            PendingHitStateStart = null;
            PendingHitStateEnd = null;
            PendingHitStateForce = false;
            InputH.Clear(); // Clear, don't want to read input from a previous round.
            // TODO: character dependent?
            IsSuperAttack = false;
            Super = 0;
            AirDashCount = 0;
            Health = config.Health;
            FacingDir = facingDirection;
            PendingKnockdown = KnockdownKind.None;
        }

        public void DoFrameStart(GameOptions options, bool maniaActive)
        {
            // Latch the mania hitstun lock: if this fighter is in hitstun
            // while a mania is running, they have to keep being treated as
            // non-actionable for the rest of the mania, even after
            // TickStateMachine transitions them out of CharacterState.Hit.
            // Has to run before TickStateMachine, otherwise a fighter whose
            // hitstun ends this frame would already be Idle by the time we
            // checked.
            if (maniaActive && State == CharacterState.Hit)
            {
                LockedHitstun = true;
            }

            if (OnGround(options))
            {
                AirDashCount = 0;
            }
        }

        // Applies the actionable-gated resets (combo count, heal-on-actionable,
        // super/burst max-on-actionable). Has to run after TickStateMachine so
        // Actionable reflects the state the fighter will act from this frame.
        // Otherwise a fighter whose stun ends this frame would still look
        // non-actionable and skip the resets, even though they're about to
        // process input normally.
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
            if (options.Players[Index].SuperMaxOnActionable && gameMode == GameMode.Fighting && !FreestyleActive)
            {
                Super = options.Global.SuperMax;
            }
            if (options.Players[Index].BurstMaxOnActionable)
            {
                Burst = options.Players[Index].Character.BurstMax;
            }
        }

        public bool OnGround(GameOptions options) => Position.y > options.Global.GroundY ? false : true;

        // Records a rhythm no-op press. Moves half of the remaining 0.25
        // budget into NoOpBonus, so the bonus approaches 1.25 but never
        // gets there no matter how many no-ops chain.
        public void RegisterManiaNoOp()
        {
            sfloat share = NoOpBonusRemaining * (sfloat)0.5f;
            NoOpBonus += share;
            NoOpBonusRemaining -= share;
        }

        // Read the current no-op bonus and reset. The damage pipeline calls
        // this so the bonus applies exactly once, to the attack right after
        // one or more no-ops.
        public sfloat ConsumeNoOpBonus()
        {
            sfloat bonus = NoOpBonus;
            NoOpBonus = sfloat.One;
            NoOpBonusRemaining = (sfloat)0.25f;
            return bonus;
        }

        // Reset no-op bonus to defaults (1.0x, full 0.25 budget). Called on
        // mania end / fail so a stale bonus doesn't carry into a fresh combo.
        public void ResetNoOpBonus()
        {
            NoOpBonus = sfloat.One;
            NoOpBonusRemaining = (sfloat)0.25f;
        }

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
            ClankLocation = null;
            GrabTechedThisRealFrame = false;
            StateChangedThisRealFrame = false;
            SuperTier1MaxedThisRealFrame = false;
            SuperTier2MaxedThisRealFrame = false;
            PostActionState = null;
            PostActionStateStart = null;
        }

        // Queues a collision-driven state transition to apply at the start of
        // the next sim frame. Pass `start` as the frame the state should
        // first be visible on (usually hit-frame + 1), so tick 0 lines up
        // with the first frame of the new state.
        public void EnqueueHitTransition(CharacterState state, Frame start, Frame end, bool force = false)
        {
            PendingHitState = state;
            PendingHitStateStart = start;
            PendingHitStateEnd = end;
            PendingHitStateForce = force;
        }

        // Applies any transition that EnqueueHitTransition queued during the
        // previous frame's collision step. Call at the start of a sim frame
        // (after SimFrame increments, before DoFrameStart) so every
        // subsequent step sees the new state.
        public void ApplyPendingHitTransition()
        {
            if (!PendingHitState.HasValue)
                return;
            SetState(PendingHitState.Value, PendingHitStateStart.Value, PendingHitStateEnd.Value, PendingHitStateForce);
            PendingHitState = null;
            PendingHitStateStart = null;
            PendingHitStateEnd = null;
            PendingHitStateForce = false;
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
                if (State.IsStunned() && !nextState.IsStunned())
                {
                    ImmunityHash = 0;
                }
                State = nextState;
                StateStart = start;
                StateEnd = end;
                StateChangedThisRealFrame = true;
                IsSuperAttack = false;
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

                if (State == CharacterState.HeavyKnockdown)
                {
                    SetState(
                        CharacterState.GetUp,
                        frame,
                        frame + options.Players[Index].Character.GetHitboxData(CharacterState.GetUp).TotalTicks
                    );
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
                    SetState(
                        CharacterState.BackDash,
                        frame,
                        frame + options.Global.BackDashTicks + config.BackDashRecoveryTicks
                    );
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
            GameMode gameMode,
            bool isManiaAttacker
        )
        {
            if (InputH.IsHeld(InputFlags.Burst) && State != CharacterState.Burst && Burst >= config.BurstMax && Health > sfloat.Zero && !isManiaAttacker)
            {
                Burst = 0;
                SetState(
                    CharacterState.Burst,
                    simFrame,
                    simFrame + config.GetHitboxData(CharacterState.Burst).TotalTicks
                );
                return;
            }

            if (
                State == CharacterState.Hit
                || State == CharacterState.SoftKnockdown
                || State == CharacterState.HeavyKnockdown
                || State == CharacterState.GetUp
            )
            {
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

            // Heavy-attack → super promotion. Two trigger shapes, chosen per
            // player via SuperInputMode:
            //   Hold:      on frame SuperDelayWindow, heavy must still be held
            //              and must not have been released at any point inside
            //              the window.
            //   DoubleTap: any time inside the window, a release-then-re-press
            //              of heavy promotes immediately on the re-press frame.
            int superDelayWindow = options.Global.Input.SuperDelayWindow;
            bool isHeavyAttackState =
                State == CharacterState.HeavyAttack
                || State == CharacterState.HeavyAerial
                || State == CharacterState.HeavyCrouching;
            int framesInto = simFrame - StateStart;
            SuperInputMode superInputMode = options.Players[Index].SuperInputMode;

            bool triggerHold =
                superInputMode == SuperInputMode.Hold
                && framesInto == superDelayWindow
                && InputH.IsHeld(InputFlags.HeavyAttack)
                && !InputH.ReleasedRecently(InputFlags.HeavyAttack, withinFrames: superDelayWindow + 1);

            bool triggerDoubleTap =
                superInputMode == SuperInputMode.DoubleTap
                && framesInto > 0
                && framesInto <= superDelayWindow
                && InputH.PressedRecently(InputFlags.HeavyAttack, withinFrames: framesInto + 1);

            if (
                isHeavyAttackState
                && !IsSuperAttack
                && gameMode == GameMode.Fighting
                && (triggerHold || triggerDoubleTap)
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
                // Pre-charge half of SuperCost on commit; refunded in
                // GameState when the super lands. Whiffs keep the charge,
                // so throwing out a super without connecting costs 50%.
                if (IsSuperAttack)
                {
                    Super = Mathsf.Max(Super - superCost / (sfloat)2, (sfloat)0);
                    StateEnd += options.Global.SuperRecoveryFrames;
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

            if (simFrame + 1 >= StateEnd && InputH.IsHeld(ForwardInput) && State == CharacterState.ForwardDash)
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

        public void UpdatePosition(Frame frame, GameOptions options, ref SVector2 otherFighterPos)
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

            HitboxData moveData = options.Players[Index].Character.GetHitboxData(State);
            if (moveData != null && moveData.ApplyRootMotion)
            {
                int rmTick = frame - StateStart;
                SVector2 prevOffset =
                    rmTick > 0
                        ? options.Players[Index].Character.GetFrameData(State, rmTick - 1).RootMotionOffset
                        : SVector2.zero;
                SVector2 rmDelta = curData.RootMotionOffset - prevOffset;
                rmDelta.x *= FacingDir == FighterFacing.Left ? -1 : 1;
                Position += rmDelta;
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
                    if (frame >= StateEnd - config.BackDashRecoveryTicks)
                    {
                        Velocity.x = 0;
                    }
                    else
                    {
                        Velocity.x = BackwardVector.x * (config.BackDashDistance / options.Global.BackDashTicks);
                    }
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

            if (State == CharacterState.Death && OnGround(options))
            {
                Velocity = SVector2.zero;
            }

            sfloat cameraMaxBounds =
                otherFighterPos.x + 2 * (options.Global.CameraHalfWidth - options.Global.CameraPadding);
            sfloat cameraMinBounds =
                otherFighterPos.x - 2 * (options.Global.CameraHalfWidth - options.Global.CameraPadding);
            sfloat maxBounds = Mathsf.Min(options.Global.WallsX, cameraMaxBounds);
            sfloat minBounds = Mathsf.Max(-options.Global.WallsX, cameraMinBounds);
            bool stunned = State.IsStunned();

            if (Position.x >= maxBounds)
            {
                if (stunned && Velocity.x > sfloat.Zero && Position.x > options.Global.WallsX)
                {
                    sfloat excess = Position.x - options.Global.WallsX;
                    otherFighterPos.x -= excess;
                }
                else if (Velocity.x > sfloat.Zero)
                {
                    Velocity.x = sfloat.Zero;
                }
                Position.x = maxBounds;
            }

            if (Position.x <= minBounds)
            {
                if (stunned && Velocity.x < sfloat.Zero && Position.x < -options.Global.WallsX)
                {
                    sfloat excess = -options.Global.WallsX - Position.x;
                    otherFighterPos.x += excess;
                }
                else if (Velocity.x < sfloat.Zero)
                {
                    Velocity.x = sfloat.Zero;
                }
                Position.x = minBounds;
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

            if (State == CharacterState.HeavyKnockdown && PendingKnockdown == KnockdownKind.Light)
            {
                PendingKnockdown = KnockdownKind.None;
                Velocity = SVector2.zero;
                SetState(
                    CharacterState.SoftKnockdown,
                    frame,
                    frame + config.GetHitboxData(CharacterState.SoftKnockdown).TotalTicks,
                    true
                );
                return;
            }

            if (State == CharacterState.HeavyKnockdown && PendingKnockdown == KnockdownKind.Heavy)
            {
                PendingKnockdown = KnockdownKind.None;
                Velocity = SVector2.zero;
                // Preserve StateStart so the HeavyKnockdown animation keeps
                // playing from where it was when the fighter lands. Only the
                // downed-timer end frame gets latched here.
                StateEnd = frame + options.Global.HeavyKnockdownTicks;
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
            HitboxData hitboxData = config.GetHitboxData(State);
            FrameData frameData = config.GetFrameData(State, tick);

            foreach (var box in frameData.Boxes)
            {
                SVector2 centerLocal = box.CenterLocal;
                if (hitboxData != null && hitboxData.ApplyRootMotion)
                {
                    centerLocal -= frameData.RootMotionOffset;
                }
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

                bool ignoreOwner = hitboxData != null && hitboxData.IgnoreOwner;
                physics.AddBox(handle, centerWorld, sizeLocal, newProps, -1, ignoreOwner);
            }
        }

        public void ProcessHit(Frame frame, BoxProps props, CharacterConfig config)
        {
            if (props.HasTransition)
            {
                // The transition is deferred to the start of the next sim
                // frame by ApplyPendingHitTransition, so StateStart = frame+1
                // lines up tick 0 with the first frame the new state is
                // visible.
                Frame nextStart = frame + 1;
                if (props.OnHitTransition == CharacterState.Throw)
                {
                    bool backThrow = InputH.IsHeld(BackwardInput);
                    if (backThrow)
                    {
                        FacingDir = FacingDir == FighterFacing.Right ? FighterFacing.Left : FighterFacing.Right;
                    }

                    EnqueueHitTransition(
                        CharacterState.Throw,
                        nextStart,
                        nextStart + config.GetHitboxData(CharacterState.Throw).TotalTicks,
                        true
                    );
                    return;
                }
                EnqueueHitTransition(
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
            if (State.IsKnockdown())
            {
                return new HitOutcome { Kind = HitKind.None };
            }
            int immunityVal = ComputeImmunityHash(props);
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
                    PendingKnockdown = KnockdownKind.Light;
                    SetState(CharacterState.HeavyKnockdown, frame, Frame.Infinity, true);
                    break;
                case KnockdownKind.Heavy:
                    PendingKnockdown = KnockdownKind.Heavy;
                    SetState(CharacterState.HeavyKnockdown, frame, Frame.Infinity, true);
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
            // only consider the first grab tech property
            if (State != CharacterState.Grabbed)
            {
                ComboedCount++;
                CurrentGrabTechable = props.Techable;
            }

            SetState(CharacterState.Grabbed, frame, frame + 2, true);

            Velocity = SVector2.zero;

            SVector2 grabPos = props.GrabPosition;
            if (grabberFacingDir == FighterFacing.Left)
            {
                grabPos.x *= -1;
            }

            Position = hitboxCenter + grabPos;
        }

        public void ApplyGrabTech(Frame frame, GameOptions options, SVector2 pushDirection)
        {
            SetState(CharacterState.Hit, frame, frame + options.Global.GrabTechStunTicks, true);
            Velocity = pushDirection * options.Global.GrabTechKnockbackMagnitude;
            CurrentGrabTechable = false;
            GrabTechedThisRealFrame = true;
            CancelPendingHitTransition();
        }

        public void CancelPendingHitTransition()
        {
            PendingHitState = null;
            PendingHitStateStart = null;
            PendingHitStateEnd = null;
            PendingHitStateForce = false;
        }

        public void ApplyClank(Frame frame, GameOptions options, SVector2 location)
        {
            SetState(CharacterState.Hit, frame, frame + options.Global.ClankTicks);
            ClankLocation = location;

            Velocity = SVector2.zero;
        }

        // Deterministic immunity hash over BoxProps. System.HashCode uses a
        // per-process seed, so its output diverges across peers and desyncs
        // the serialized ImmunityHash. Every BoxProps field has to be mixed
        // in here. When you add a field to BoxProps, update both BoxProps.Equals
        // and this helper.
        private static int ComputeImmunityHash(BoxProps props)
        {
            unchecked
            {
                int h = (int)props.Kind;
                h = h * 31 + (int)props.AttackKind;
                h = h * 31 + props.Damage;
                h = h * 31 + props.HitstunTicks;
                h = h * 31 + props.BlockstunTicks;
                h = h * 31 + props.HitstopTicks;
                h = h * 31 + props.BlockstopTicks;
                h = h * 31 + (int)props.KnockdownKind;
                h = h * 31 + (int)props.Knockback.x.RawValue;
                h = h * 31 + (int)props.Knockback.y.RawValue;
                h = h * 31 + (int)props.GrabPosition.x.RawValue;
                h = h * 31 + (int)props.GrabPosition.y.RawValue;
                h = h * 31 + (props.GrabsGrounded ? 1 : 0);
                h = h * 31 + (props.GrabsAirborne ? 1 : 0);
                h = h * 31 + (props.Techable ? 1 : 0);
                h = h * 31 + (props.HasTransition ? 1 : 0);
                h = h * 31 + (int)props.OnHitTransition;
                h ^= h >> 16;
                h *= (int)0x45d9f3b;
                h ^= h >> 16;
                return h;
            }
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
            if (crossedTier1)
            {
                SuperTier1MaxedThisRealFrame = true;
            }
            if (crossedTier2)
            {
                SuperTier2MaxedThisRealFrame = true;
            }
        }
    }
}
