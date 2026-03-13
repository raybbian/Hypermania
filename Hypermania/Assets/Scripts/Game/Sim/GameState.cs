using System;
using System.Buffers;
using System.Linq;
using Design.Animation;
using Design.Configs;
using Game.View.Overlay;
using MemoryPack;
using Netcode.Rollback;
using UnityEngine.InputSystem;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum GameMode
    {
        Fighting,
        ManiaStart,
        Mania,
        RoundEnd, // Used to be roundStart, indicates the end of a round
        Countdown, // Countdown to round.
        End, // after game is done
    }

    [Serializable]
    public class PlayerOptions
    {
        public bool HealOnActionable;
        public CharacterConfig Character;
        public int SkinIndex;
    }

    [Serializable]
    public class LocalPlayerOptions
    {
        public InputDevice InputDevice;
        public ControlsConfig Controls;
    }

    [Serializable]
    public class GameOptions
    {
        public GlobalConfig Global;
        public PlayerOptions[] Players;
        public LocalPlayerOptions[] LocalPlayers;
    }

    [MemoryPackable]
    public partial class GameState : IState<GameState>
    {
        /// <summary>
        /// Physics context used to resolve collisions between boxes.
        /// </summary>
        [ThreadStatic]
        private static PhysicsContext<BoxProps> _physicsCtx;

        private static PhysicsContext<BoxProps> PhysicsCtx
        {
            get
            {
                if (_physicsCtx == null)
                    _physicsCtx = new PhysicsContext<BoxProps>(MAX_COLLIDERS);
                return _physicsCtx;
            }
        }

        [MemoryPackIgnore]
        public const int MAX_COLLIDERS = 100;

        public int PartialSimFrameCount; // to accumulate frames when speedRatio is < 1
        public Frame RealFrame; // network/music frame
        public Frame SimFrame; // Game sim frame
        public Frame RoundStart; // Added to indicate when a round starts.
        public Frame RoundEnd;
        public FighterState[] Fighters;
        public ManiaState[] Manias;
        public sfloat HypeMeter;
        public GameMode GameMode;
        public int HitstopFramesRemaining;

        public sfloat SpeedRatio;
        public Frame ModeStart;

        /// <summary>
        /// Use this static builder instead of the constructor for creating new GameStates. This is because MemoryPack,
        /// which we use to serialize the GameState, places some funky restrictions on the constructor's paratmeter
        /// list.
        /// </summary>
        /// <param name="characterConfigs">Character configs to use</param>
        /// <returns>The created GameState</returns>
        public static GameState Create(GameOptions options)
        {
            GameState state = new GameState
            {
                RealFrame = Frame.FirstFrame,
                SimFrame = Frame.FirstFrame,
                RoundStart = Frame.FirstFrame,
                RoundEnd = new Frame(options.Global.RoundTimeTicks),
                Fighters = new FighterState[options.Players.Length],
                Manias = new ManiaState[options.Players.Length],
                HitstopFramesRemaining = 0,
                HypeMeter = (sfloat)0f,
                GameMode = GameMode.Countdown,
                SpeedRatio = 1,
            };
            for (int i = 0; i < options.Players.Length; i++)
            {
                sfloat xPos = (i - ((sfloat)options.Players.Length - 1) / 2) * 4;
                FighterFacing facing = xPos > 0 ? FighterFacing.Left : FighterFacing.Right;
                state.Fighters[i] = FighterState.Create(i, options, new SVector2(xPos, sfloat.Zero), facing, 3);
                state.Manias[i] = ManiaState.Create(
                    new ManiaConfig
                    {
                        NumKeys = 4,
                        HitHalfRange = options.Global.Input.BeatCancelWindow,
                        MissHalfRange = options.Global.Input.BeatCancelWindow + 3,
                    }
                );
            }

            return state;
        }

        private void DoRoundEnd(GameOptions options, Span<GameInput> outInputs)
        {
            SpeedRatio =
                1 - (sfloat)(RealFrame - ModeStart) / (options.Global.RoundEndTicks) * (sfloat)0.25f - (sfloat)0.50f;
            if (RealFrame - ModeStart < options.Global.RoundEndTicks)
            {
                return;
            }

            if (FightersDead())
            {
                ModeStart = RealFrame;
                GameMode = GameMode.End;
                return;
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].Health = options.Players[i].Character.Health;
                sfloat xPos = (i - ((sfloat)options.Players.Length - 1) / 2) * 4;
                FighterFacing facing = xPos > 0 ? FighterFacing.Left : FighterFacing.Right;
                Fighters[i].RoundReset(options.Players[i].Character, new SVector2(xPos, sfloat.Zero), facing);
                outInputs[i] = GameInput.None;
                Manias[i].ManiaEvents.Clear();
            }

            ModeStart = Frame.NullFrame;
            HypeMeter = (sfloat)0.0f;
            RoundStart = SimFrame;
            SpeedRatio = 1;
            GameMode = GameMode.Countdown;
        }

        private void DoCountdown(GameOptions options, Span<GameInput> outInputs)
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                outInputs[i] = GameInput.None;
                Fighters[i].SetState(CharacterState.Idle, SimFrame, Frame.Infinity);
            }

            if (SimFrame - RoundStart >= options.Global.RoundCountdownTicks) // Added an attribute to config for countdown.
            {
                GameMode = GameMode.Fighting;
                RoundEnd = SimFrame + options.Global.RoundTimeTicks;
            }
        }

        private void DoManiaStart(GameOptions options, Span<GameInput> outInputs)
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                outInputs[i] = GameInput.None;
            }

            if (RealFrame - ModeStart <= options.Global.ManiaSlowTicks / 2)
            {
                SpeedRatio = (sfloat)0.25f;
            }
            else if (RealFrame - ModeStart <= options.Global.ManiaSlowTicks)
            {
                SpeedRatio = (sfloat)0.5f;
            }
            else
            {
                SpeedRatio = 1;
                GameMode = GameMode.Mania;
                ModeStart = Frame.NullFrame;
            }
        }

        public void Advance(GameOptions options, (GameInput input, InputStatus status)[] inputs)
        {
            if (inputs.Length != options.Players.Length || options.Players.Length != Fighters.Length)
            {
                throw new InvalidOperationException("invalid inputs and characters to advance game state with");
            }

            RealFrame += 1;
            for (int i = 0; i < Fighters.Length; i++)
            {
                Manias[i].ManiaEvents.Clear();
                Fighters[i].ClearViewNotifiers();
            }

            if (GameMode == GameMode.End)
            {
                return;
            }

            PartialSimFrameCount++;
            if (PartialSimFrameCount < 1 / SpeedRatio)
            {
                return;
            }

            PartialSimFrameCount = 0;
            Span<GameInput> remapInputs = stackalloc GameInput[Fighters.Length];
            switch (GameMode)
            {
                case GameMode.RoundEnd:
                    DoRoundEnd(options, remapInputs);
                    break;
                case GameMode.Countdown:
                    DoCountdown(options, remapInputs);
                    break;
                case GameMode.Fighting:
                    for (int i = 0; i < Fighters.Length; i++)
                    {
                        remapInputs[i] = inputs[i].input;
                    }

                    break;
                case GameMode.Mania:
                    DoManiaStep(inputs, remapInputs);
                    break;
                case GameMode.ManiaStart:
                    DoManiaStart(options, remapInputs);
                    break;
            }

            // Push the current input into the input history, to read for buffering.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].InputH.PushInput(remapInputs[i]);
            }

            // if hitstop, only grab inputs and return
            if (HitstopFramesRemaining > 0)
            {
                HitstopFramesRemaining--;
                return;
            }

            SimFrame += 1;

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].DoFrameStart(options);
            }

            // Tick the state machine, making the character idle if an animation/stun finishes
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].TickStateMachine(SimFrame, options);
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].FaceTowards(Fighters[i ^ 1].Position);
            }

            // This function internally appies changes to the fighter's velocity based on movement inputs
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyMovementState(SimFrame, options);
            }

            // If a player applies inputs to start a state at the start of the frame, we should apply those immediately
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyActiveState(SimFrame, RealFrame, options, options.Players[i].Character);
            }

            DoCollisionStep(options);

            if (SimFrame == RoundEnd)
            {
                RoundEnd = SimFrame + options.Global.RoundTimeTicks;
                //TODO: Properly handle edge case where player health is equal. Currently player 1 wins by default.
                if (Fighters[0].Health < Fighters[1].Health)
                {
                    Fighters[0].Health = 0;
                }
                else
                {
                    Fighters[1].Health = 0;
                }
            }

            if (GameMode == GameMode.Mania || GameMode == GameMode.Fighting)
            {
                for (int i = 0; i < Fighters.Length; i++)
                {
                    if (Fighters[i].Health <= 0)
                    {
                        Fighters[i].Lives--;

                        // Decide what victory indicator to give.
                        if (Fighters[1 - i].Health == options.Players[1 - i].Character.Health)
                        {
                            Fighters[1 - i].Victories[Fighters[1 - i].NumVictories] = VictoryKind.Perfect;
                        }
                        else
                        {
                            Fighters[1 - i].Victories[Fighters[1 - i].NumVictories] = VictoryKind.Normal;
                        }

                        Fighters[1 - i].NumVictories++;

                        GameMode = GameMode.RoundEnd;
                        Fighters[i].SetState(CharacterState.Death, SimFrame, Frame.Infinity);
                        ModeStart = RealFrame;
                        // Ensure that if the player died to a mania attack it ends immediately
                        for (int j = 0; j < Manias.Length; j++)
                        {
                            Manias[j].End();
                        }

                        return;
                    }
                }
            }

            // Apply any velocities set during movement or through knockback.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].UpdatePosition(SimFrame, options, Fighters[i ^ 1].Position);
            }

            // Update hype if they are holding forward
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i].InputH.IsHeld(Fighters[i].ForwardInput))
                {
                    UpdateHype(options, i, options.Global.HypeMovementFactor);
                }
            }

            // If the fighter is now on the ground, apply aerial cancels
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyAerialCancel(SimFrame, options, options.Players[i].Character);
            }
        }

        public bool FightersDead()
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (Fighters[i].Lives <= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DoManiaStep((GameInput input, InputStatus status)[] inputs, Span<GameInput> outInputs)
        {
            for (int i = 0; i < Manias.Length; i++)
            {
                Manias[i].Tick(RealFrame, inputs[i].input);

                foreach (ManiaEvent ev in Manias[i].ManiaEvents)
                {
                    switch (ev.Kind)
                    {
                        case ManiaEventKind.End:
                            GameMode = GameMode.Fighting;
                            break;
                        case ManiaEventKind.Hit:
                            outInputs[i].Flags |= ev.Note.HitInput;
                            break;
                        case ManiaEventKind.Missed:
                            GameMode = GameMode.Fighting;
                            Manias[i].End();
                            break;
                    }
                }
            }
        }

        private void DoCollisionStep(GameOptions options)
        {
            // Each fighter then adds their hit/hurtboxes to the physics context, which will solve and find all
            // collisions. It is our job to then handle them.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].AddBoxes(SimFrame, options.Players[i].Character, PhysicsCtx.Physics, i);
            }

            // AdvanceProjectiles();

            PhysicsCtx.Physics.GetCollisions(PhysicsCtx.Collisions);

            // First, solve collisions that would result in player damage. There can only be one such collision per
            // (A, B) ordered pair, where A and B are players, projectiles, or other game objects. For now, we take the
            // first collision that happens this way. In the future, which collision we take should be given by a hitbox
            // priority (a stronger hitting move should be preferred over a projectile)
            //
            // If there are no collisions of that type, then find collisions between hitboxes. This would result in a
            // clank.
            //
            // Finally, if there are no clanks or player damage collisions, make sure that the characters are not
            // colliding. If they are, push them apart.
            Physics<BoxProps>.Collision? clank = null;
            Physics<BoxProps>.Collision? collide = null;
            foreach (var c in PhysicsCtx.Collisions)
            {
                (int, int) hitPair = (-1, -1);
                if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
                {
                    hitPair = (c.BoxA.Owner, c.BoxB.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
                {
                    hitPair = (c.BoxB.Owner, c.BoxA.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
                {
                    clank = c;
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
                {
                    collide = c;
                }

                // TODO: sort by priority or something
                if (hitPair != (-1, -1))
                {
                    PhysicsCtx.HurtHitCollisions[hitPair] = c;
                }
            }

            if (PhysicsCtx.HurtHitCollisions.Count > 0)
            {
                // sort for determinism
                var collisions = PhysicsCtx.HurtHitCollisions.ToList();
                collisions.Sort(
                    (a, b) =>
                    {
                        if (a.Key.Item1 == b.Key.Item1)
                            return a.Key.Item2 - b.Key.Item2;
                        return a.Key.Item1 - b.Key.Item1;
                    }
                );

                foreach ((var owners, var collision) in collisions)
                {
                    //owners[0] hits owners[1]
                    HitOutcome outcome = HandleCollision(options, collision);

                    HitstopFramesRemaining = Mathsf.Max(outcome.Props.HitstopTicks, HitstopFramesRemaining);

                    var attackerBox = collision.BoxA.Owner == owners.Item1 ? collision.BoxA : collision.BoxB;

                    if (outcome.Kind == HitKind.Hit)
                    {
                        sfloat damage = outcome.Props.Damage;
                        UpdateHype(options, attackerBox.Owner, damage);
                    }

                    //to start a rhythm combo, we must sure that the move was not traded
                    if (
                        attackerBox.Data.StartsRhythmCombo
                        && !PhysicsCtx.HurtHitCollisions.ContainsKey((owners.Item2, owners.Item1))
                        && GameMode == GameMode.Fighting
                        && outcome.Kind == HitKind.Hit
                    )
                    {
                        // set hitstop to the next beat
                        Frame nextBeat = RealFrame;
                        while (nextBeat - RealFrame < options.Global.ManiaSlowTicks)
                        {
                            nextBeat = options.Global.Audio.NextBeat(
                                nextBeat + 1,
                                AudioConfig.BeatSubdivision.QuarterNote
                            );
                        }

                        HitstopFramesRemaining = nextBeat - (RealFrame + options.Global.ManiaSlowTicks);
                        for (int i = 0; i < 16; i++)
                        {
                            Manias[owners.Item1]
                                .QueueNote(
                                    i % 4,
                                    new ManiaNote
                                    {
                                        Length = 0,
                                        Tick = nextBeat,
                                        HitInput = InputFlags.MediumAttack,
                                    }
                                );
                            nextBeat = options.Global.Audio.NextBeat(
                                nextBeat + 1,
                                AudioConfig.BeatSubdivision.QuarterNote
                            );
                        }

                        Manias[owners.Item1].Enable(nextBeat);
                        GameMode = GameMode.ManiaStart;
                        ModeStart = RealFrame;
                        // TODO: show mania screen only after the maximum rollback frames to ensure no visual artifacting
                    }
                }
            }
            else if (clank.HasValue)
            {
                HandleCollision(options, clank.Value);
            }
            else if (collide.HasValue)
            {
                HandleCollision(options, collide.Value);
            }

            // Clear the physics context for the next frame, which will then re-add boxes and solve for collisions again
            PhysicsCtx.Clear();
        }

        private void UpdateHype(GameOptions options, int handle, sfloat damage)
        {
            if (handle == 0)
            {
                HypeMeter += damage;
            }
            else
            {
                HypeMeter -= damage;
            }

            HypeMeter = Mathsf.Clamp(HypeMeter, -options.Global.MaxHype, options.Global.MaxHype);
        }

        private HitOutcome HandleCollision(GameOptions options, Physics<BoxProps>.Collision c)
        {
            if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                sfloat mult = 1 + (sfloat)0.2f * (HypeMeter / options.Global.MaxHype) * (c.BoxA.Owner * -2 + 1);
                return Fighters[c.BoxB.Owner]
                    .ApplyHit(
                        SimFrame,
                        Fighters[c.BoxA.Owner].StateStart,
                        options.Players[c.BoxB.Owner].Character,
                        c.BoxA.Data,
                        c.BoxB.Box.ClosestPointToCenter(c.BoxA.Box),
                        mult
                    );
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                sfloat mult = 1 + (sfloat)0.2f * (HypeMeter / options.Global.MaxHype) * (c.BoxB.Owner * -2 + 1);
                return Fighters[c.BoxA.Owner]
                    .ApplyHit(
                        SimFrame,
                        Fighters[c.BoxB.Owner].StateStart,
                        options.Players[c.BoxA.Owner].Character,
                        c.BoxB.Data,
                        c.BoxA.Box.ClosestPointToCenter(c.BoxB.Box),
                        mult
                    );
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                // TODO: check if moves are allowed to clank
                Fighters[c.BoxA.Owner].ApplyClank(SimFrame, options);
                Fighters[c.BoxB.Owner].ApplyClank(SimFrame, options);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                sfloat aPushFactor = Fighters[c.BoxA.Owner].OnGround(options) ? (sfloat)1f : (sfloat)0.1f;
                sfloat bPushFactor = Fighters[c.BoxB.Owner].OnGround(options) ? (sfloat)1f : (sfloat)0.1f;

                sfloat aPush = aPushFactor / (aPushFactor + bPushFactor);
                sfloat bPush = bPushFactor / (aPushFactor + bPushFactor);
                if (c.BoxA.Box.Pos.x < c.BoxB.Box.Pos.x)
                {
                    Fighters[c.BoxA.Owner].Position.x -= c.OverlapX * aPush;
                    Fighters[c.BoxB.Owner].Position.x += c.OverlapX * bPush;
                }
                else
                {
                    Fighters[c.BoxA.Owner].Position.x += c.OverlapX * aPush;
                    Fighters[c.BoxB.Owner].Position.x -= c.OverlapX * bPush;
                }
            }

            return new HitOutcome { Kind = HitKind.None };
        }

        [ThreadStatic]
        private static ArrayBufferWriter<byte> _writer;

        private static ArrayBufferWriter<byte> Writer
        {
            get
            {
                if (_writer == null)
                    _writer = new ArrayBufferWriter<byte>(256);
                return _writer;
            }
        }

        public ulong Checksum()
        {
            Writer.Clear();
            MemoryPackSerializer.Serialize(Writer, this);
            ReadOnlySpan<byte> bytes = Writer.WrittenSpan;

            // 64-bit FNV-1a over the serialized bytes
            const ulong OFFSET = 14695981039346656037UL;
            const ulong PRIME = 1099511628211UL;

            ulong hash = OFFSET;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= PRIME;
            }

            return hash;
        }
    }
}
