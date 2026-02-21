using System;
using Design;
using Design.Animation;
using MemoryPack;
using UnityEngine;
using UnityEngine.UIElements;
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

        public CharacterState State { get; private set; }
        public Frame StateStart { get; private set; }

        /// <summary>
        /// Set to a value that marks the first frame in which the character should return to neutral.
        /// </summary>
        public Frame StateEnd { get; private set; }
        public Frame ImmunityEnd { get; private set; }

        public FighterFacing FacingDir;

        public FighterLocation LastLocation;
        public Frame LocationSt { get; private set; }

        public bool IsAerial =>
            State == CharacterState.LightAerial
            || State == CharacterState.MediumAerial
            || State == CharacterState.SuperAerial
            || State == CharacterState.SpecialAerial;

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
            Health = config.Health;
            FacingDir = facingDirection;
        }

        public void DoFrameStart()
        {
            if (State == CharacterState.Idle || State == CharacterState.Jump || State == CharacterState.Walk)
            {
                ComboedCount = 0;
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
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void ApplyMovementIntent(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (State != CharacterState.Idle && State != CharacterState.Walk && State != CharacterState.Jump)
            {
                return;
            }
            if (Location(config) == FighterLocation.Grounded)
            {
                // this prevents jumping after dashing preserving momentum
                Velocity.x = 0;

                if (InputH.IsHeld(InputFlags.Left) && InputH.PressedAndReleasedRecently(InputFlags.Left, 12, 1))
                {
                    Velocity.x += 2 * -characterConfig.Speed;
                    State = FacingDir == FighterFacing.Left ? CharacterState.ForwardDash : CharacterState.BackDash;
                    StateEnd = frame + 12;
                    StateStart = frame;
                    return;
                }

                if (InputH.IsHeld(InputFlags.Right) && InputH.PressedAndReleasedRecently(InputFlags.Right, 12, 1))
                {
                    Velocity.x += 2 * characterConfig.Speed;
                    State = FacingDir == FighterFacing.Right ? CharacterState.ForwardDash : CharacterState.BackDash;
                    StateEnd = frame + 12;
                    StateStart = frame;
                    return;
                }

                if (InputH.IsHeld(InputFlags.Left))
                {
                    Velocity.x += -characterConfig.Speed;
                }
                if (InputH.IsHeld(InputFlags.Right))
                {
                    Velocity.x += characterConfig.Speed;
                }

                if (InputH.IsHeld(InputFlags.Up))
                {
                    if (InputH.PressedRecently(InputFlags.Down, 8))
                    {
                        Velocity.y = (sfloat)1.25 * characterConfig.JumpVelocity;
                    }
                    else
                    {
                        Velocity.y = characterConfig.JumpVelocity;
                    }
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
            if (State != CharacterState.Idle && State != CharacterState.Walk && State != CharacterState.Jump)
            {
                return;
            }
            if (InputH.PressedRecently(InputFlags.LightAttack, 8))
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
            else if (InputH.PressedRecently(InputFlags.MediumAttack, 8))
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
            else if (InputH.PressedRecently(InputFlags.HeavyAttack, 8))
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
        }

        public void UpdatePosition(GlobalConfig config)
        {
            // Apply gravity if not grounded
            if (Position.y > config.GroundY || Velocity.y > 0)
            {
                Velocity.y += config.Gravity * 1 / 64;
            }

            // Update Position
            Position += Velocity * 1 / 64;

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

        public HitOutcome ApplyHit(Frame frame, BoxProps props, CharacterConfig config)
        {
            if (ImmunityEnd > frame)
            {
                return new HitOutcome { Kind = HitKind.None };
            }

            bool holdingBack =
                FacingDir == FighterFacing.Left ? InputH.IsHeld(InputFlags.Right) : InputH.IsHeld(InputFlags.Left);
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
                return new HitOutcome { Kind = HitKind.Blocked };
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
            return new HitOutcome { Kind = HitKind.Hit, Props = props };
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
                (State == CharacterState.Idle || State == CharacterState.Walk)
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
                (State == CharacterState.Walk || State == CharacterState.Jump)
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
