using System;
using System.Buffers;
using System.Linq;
using Design.Animation;
using Design.Configs;
using Game.View.Overlay;
using MemoryPack;
using Netcode.Rollback;
using UnityEngine;
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

    public enum ComboMode
    {
        Assisted,
        Freestyle,
    }

    public enum ManiaDifficulty
    {
        Normal,
        Hard,
    }

    public enum BeatCancelWindow
    {
        Medium = 5,
        Hard = 3,
    }

    [Serializable]
    public class PlayerOptions
    {
        public bool HealOnActionable;
        public bool SuperMaxOnActionable;
        public bool BurstMaxOnActionable;
        public bool Immortal;
        public CharacterConfig Character;
        public int SkinIndex;
        public ComboMode ComboMode;
        public ManiaDifficulty ManiaDifficulty;
        public BeatCancelWindow BeatCancelWindow = BeatCancelWindow.Medium;
    }

    [Serializable]
    public class LocalPlayerOptions
    {
        public InputDevice InputDevice;
        public ControlsConfig Controls;
    }

    [Serializable]
    public class InfoOptions
    {
        public bool ShowFrameData;
        public bool ShowBoxes;

        /// <summary>
        /// Developer-only. When true, each generated rhythm combo is simulated
        /// a second time with perfect on-beat mania presses, and the real
        /// <see cref="GameState"/> at the mania's <c>EndFrame</c> is diffed
        /// field-by-field against the prediction via
        /// <see cref="ComboVerifyDebug"/>. Any differing field means pressing
        /// within the hit window (instead of exactly on the beat) produced a
        /// different downstream state — i.e. the beat-snap / rhythm-cancel
        /// invariant is broken. Log-only; no gameplay effect.
        /// </summary>
        public bool VerifyComboPrediction;
    }

    [Serializable]
    public class GameOptions
    {
        public GlobalConfig Global;
        public PlayerOptions[] Players;
        public LocalPlayerOptions[] LocalPlayers;
        public InfoOptions InfoOptions;
        public bool AlwaysRhythmCancel;
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

        [MemoryPackIgnore]
        public const int MAX_PROJECTILES = 8;

        public int PartialSimFrameCount; // to accumulate frames when speedRatio is < 1
        public Frame RealFrame; // network/music frame
        public Frame SimFrame; // Game sim frame
        public Frame RoundStart; // Added to indicate when a round starts.
        public Frame RoundEnd;
        public FighterState[] Fighters;
        public ManiaState[] Manias;
        public ProjectileState[] Projectiles;
        public sfloat HypeMeter;
        public GameMode GameMode;
        public int HitstopFramesRemaining;
        public RhythmComboManager ComboManager;

        public sfloat SpeedRatio;
        public Frame ModeStart;

        /// <summary>
        /// Attacker index whose rhythm combo should be started at the end of
        /// the current <see cref="Advance"/> call. -1 when nothing is pending.
        /// DoCollisionStep just records the attacker here; the actual combo
        /// generation and note queueing runs after <see cref="FighterState.UpdatePosition"/>
        /// so that the combo generator's cloned snapshot already has frame
        /// F's velocity integration applied — otherwise the generator's sim
        /// would be one <c>UpdatePosition</c> tick behind the real game and
        /// predicted hits at the edge of a move's range could whiff in play.
        /// </summary>
        public int PendingRhythmComboAttacker;

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
                Projectiles = new ProjectileState[MAX_PROJECTILES],
                HitstopFramesRemaining = 0,
                HypeMeter = (sfloat)0f,
                GameMode = GameMode.Countdown,
                SpeedRatio = 1,
                PendingRhythmComboAttacker = -1,
            };
            for (int i = 0; i < options.Players.Length; i++)
            {
                sfloat xPos = (i - ((sfloat)options.Players.Length - 1) / 2) * 4;
                FighterFacing facing = xPos > 0 ? FighterFacing.Left : FighterFacing.Right;
                state.Fighters[i] = FighterState.Create(
                    i,
                    options.Players[i].Character.Health,
                    new SVector2(xPos, sfloat.Zero),
                    facing,
                    3,
                    options.Global.StalingBufferSize
                );
                int beatWindow = (int)options.Players[i].BeatCancelWindow;
                state.Manias[i] = ManiaState.Create(
                    new ManiaConfig
                    {
                        NumKeys = 4,
                        HitHalfRange = beatWindow,
                        MissHalfRange = beatWindow + 3,
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

            for (int i = 0; i < Projectiles.Length; i++)
            {
                Projectiles[i].Active = false;
            }

            ModeStart = Frame.NullFrame;
            HypeMeter = (sfloat)0.0f;
            // Delay countdown start until the next whole-note (measure downbeat) so the
            // 1-2-1-2-3-4-Go sequence always begins on beat 1 of a 4/4 bar. SimFrame and
            // RealFrame advance in lockstep during Countdown (SpeedRatio=1, no hitstop),
            // so aligning RealFrame here keeps every subsequent beat transition on-beat.
            var audio = options.Global.Audio;
            int framesPerWholeNote = audio.FramesPerBeat * 4;
            int phase =
                ((RealFrame.No - audio.FirstMusicalBeat.No) % framesPerWholeNote + framesPerWholeNote)
                % framesPerWholeNote;
            int delay = (framesPerWholeNote - phase) % framesPerWholeNote;
            RoundStart = SimFrame + delay;
            SpeedRatio = 1;
            GameMode = GameMode.Countdown;
            // Defensive: a round ending mid-combo-startup shouldn't leak the
            // pending attacker into the next round.
            PendingRhythmComboAttacker = -1;
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

        // While an opponent is in the startup+active window of a super, mask the
        // defender's input to only bits that were held last frame: releases pass
        // through, new presses are dropped.
        private void ApplySuperInputLock(GameOptions options, Span<GameInput> remapInputs)
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                FighterState opp = Fighters[i ^ 1];
                if (!opp.IsSuperAttack)
                    continue;

                CharacterState s = opp.State;
                if (
                    s != CharacterState.HeavyAttack
                    && s != CharacterState.HeavyAerial
                    && s != CharacterState.HeavyCrouching
                )
                    continue;

                HitboxData hd = options.Players[i ^ 1].Character.GetHitboxData(s);
                if (SimFrame - opp.StateStart >= hd.StartupTicks + hd.ActiveTicks)
                    continue;

                InputFlags prev = Fighters[i].InputH.GetInput(0).Flags;
                remapInputs[i] = new GameInput(remapInputs[i].Flags & prev);
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
            bool rhythmCancel = false;
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
                    rhythmCancel = DoManiaStep(options, inputs, remapInputs);
                    break;
                case GameMode.ManiaStart:
                    DoManiaStart(options, remapInputs);
                    break;
            }

            if (options.AlwaysRhythmCancel)
            {
                rhythmCancel = true;
            }

            if (GameMode == GameMode.Fighting)
            {
                ApplySuperInputLock(options, remapInputs);
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

            bool maniaActive = GameMode == GameMode.Mania || GameMode == GameMode.ManiaStart;
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].DoFrameStart(options, maniaActive);
            }

            // Tick the state machine, making the character idle if an animation/stun finishes
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].TickStateMachine(SimFrame, options);
            }

            // Actionable-gated resets run after TickStateMachine so a fighter
            // whose stun/animation ends this frame is seen as actionable when
            // their combo count, heal, super, and burst are decided.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyActionableFrameResets(options, GameMode);
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].FaceTowards(Fighters[i ^ 1].Position);
            }

            // This function internally appies changes to the fighter's velocity based on movement inputs
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyMovementState(SimFrame, options, rhythmCancel);
            }

            bool wasSuper0 = Fighters[0].IsSuperAttack;
            bool wasSuper1 = Fighters[1].IsSuperAttack;

            // If a player applies inputs to start a state at the start of the frame, we should apply those immediately
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyActiveState(SimFrame, options, options.Players[i].Character, rhythmCancel, GameMode);
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].CapturePostActionState();
            }

            bool anySuperStarted =
                (!wasSuper0 && Fighters[0].IsSuperAttack) || (!wasSuper1 && Fighters[1].IsSuperAttack);
            if (anySuperStarted)
            {
                HitstopFramesRemaining = Mathsf.Max(
                    HitstopFramesRemaining,
                    options.Global.SuperDisplayHitstopTicks + options.Global.SuperPostDisplayHitstopTicks
                );
            }

            // Check if any fighter should spawn a projectile this frame
            for (int i = 0; i < Fighters.Length; i++)
            {
                var projConfigs = options.Players[i].Character.Projectiles;
                if (projConfigs == null)
                    continue;

                for (int p = 0; p < projConfigs.Count; p++)
                {
                    var projConfig = projConfigs[p];
                    if (Fighters[i].State != projConfig.TriggerState)
                        continue;

                    int tick = SimFrame - Fighters[i].StateStart;
                    if (tick != projConfig.SpawnTick)
                        continue;

                    SVector2 spawnOffset = projConfig.SpawnOffset;
                    SVector2 velocity = projConfig.Velocity;
                    if (Fighters[i].FacingDir == FighterFacing.Left)
                    {
                        spawnOffset.x *= -1;
                        velocity.x *= -1;
                    }

                    SpawnProjectile(
                        i,
                        Fighters[i].Position + spawnOffset,
                        velocity,
                        Fighters[i].FacingDir,
                        SimFrame,
                        projConfig.LifetimeTicks,
                        p
                    );
                }
            }

            AdvanceProjectiles(options);
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
                    if (Fighters[i].Health <= 0 && !options.Players[i].Immortal)
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
                        ClearLockedHitstun();

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

            if (GameMode == GameMode.Fighting)
            {
                for (int i = 0; i < Fighters.Length; i++)
                {
                    Fighters[i].AddSuper(options.Global.PassiveSuperGain, options);
                }
            }
            // Execute a pending rhythm combo that was queued by
            // DoCollisionStep earlier this frame. Running the generator
            // here (rather than inline during DoCollisionStep) means the
            // clone it receives already has frame F's UpdatePosition,
            // hype, and aerial-cancel steps applied, so its internal
            // simulation starts from the exact state the real game will
            // resume from on frame F+1.
            if (PendingRhythmComboAttacker >= 0)
            {
                int attackerIndex = PendingRhythmComboAttacker;
                PendingRhythmComboAttacker = -1;
                int comboBeats = Fighters[attackerIndex].SuperComboBeats;
                Fighters[attackerIndex].IsSuperAttack = false;
                Fighters[attackerIndex].SuperComboBeats = 0;
                HitstopFramesRemaining = ComboManager.StartRhythmCombo(
                    RealFrame,
                    ref Manias[attackerIndex],
                    options,
                    this,
                    attackerIndex,
                    comboBeats
                );
            }

            if (options.InfoOptions != null && options.InfoOptions.VerifyComboPrediction)
            {
                ComboVerifyDebug.CheckAtFrame(RealFrame, this);

                // If a mania has terminated (natural end, missed note, fighter
                // death, etc. — all paths clear EndFrame to NullFrame), drop
                // any remaining snapshots for that attacker whose CompareFrame
                // is still in the future. Without this, an early-failed combo
                // would log spurious MISMATCHes for beats that never happened.
                for (int i = 0; i < Manias.Length; i++)
                {
                    if (Manias[i].EndFrame == Frame.NullFrame)
                    {
                        ComboVerifyDebug.DiscardFutureSnapshots(i, RealFrame);
                    }
                }
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

        private void ClearLockedHitstun()
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].LockedHitstun = false;
            }
        }

        private bool DoManiaStep(
            GameOptions options,
            (GameInput input, InputStatus status)[] inputs,
            Span<GameInput> outInputs
        )
        {
            bool rhythmCancel = false;

            // Dissipate SuperCost super per 8 beats from the combo attacker.
            sfloat dissipationPerFrame = options.Global.SuperCost / (sfloat)options.Global.Audio.BeatsToFrame(8);

            for (int i = 0; i < Manias.Length; i++)
            {
                if (Manias[i].Enabled(RealFrame))
                {
                    Fighters[i].Super = Mathsf.Max(Fighters[i].Super - dissipationPerFrame, (sfloat)0);
                }

                Manias[i].Tick(RealFrame, inputs[i].input);

                foreach (ManiaEvent ev in Manias[i].ManiaEvents)
                {
                    switch (ev.Kind)
                    {
                        case ManiaEventKind.End:
                            GameMode = GameMode.Fighting;
                            ClearLockedHitstun();
                            break;
                        case ManiaEventKind.Input:
                            outInputs[i].Flags |= ev.Note.HitInput;
                            rhythmCancel = true;
                            break;
                        case ManiaEventKind.Hit:
                            // View-only feedback event; no sim effect.
                            break;
                        case ManiaEventKind.Missed:
                            // Punish the missing fighter: stun for
                            // ManiaFailStunTicks frames and apply a small
                            // knockback away from the opponent.
                            Fighters[i]
                                .SetState(
                                    CharacterState.Hit,
                                    SimFrame,
                                    SimFrame + options.Global.ManiaFailStunTicks,
                                    true
                                );
                            Fighters[i].Velocity =
                                Fighters[i].BackwardVector * options.Global.ManiaFailKnockbackMagnitude;
                            // Early-end penalty: deduct half of SuperCost
                            // from the remaining super bar.
                            Fighters[i].Super = Mathsf.Max(
                                Fighters[i].Super - options.Global.SuperCost / (sfloat)2,
                                (sfloat)0
                            );
                            GameMode = GameMode.Fighting;
                            Manias[i].End();
                            ClearLockedHitstun();
                            break;
                    }
                }
            }

            return rhythmCancel;
        }

        private bool SpawnProjectile(
            int owner,
            SVector2 position,
            SVector2 velocity,
            FighterFacing facing,
            Frame simFrame,
            int lifetimeTicks,
            int configIndex
        )
        {
            for (int i = 0; i < Projectiles.Length; i++)
            {
                if (!Projectiles[i].Active)
                {
                    Projectiles[i] = new ProjectileState
                    {
                        Active = true,
                        Owner = owner,
                        Position = position,
                        Velocity = velocity,
                        CreationFrame = simFrame,
                        LifetimeTicks = lifetimeTicks,
                        FacingDir = facing,
                        MarkedForDestroy = false,
                        ConfigIndex = configIndex,
                    };
                    return true;
                }
            }
            return false;
        }

        private void AdvanceProjectiles(GameOptions options)
        {
            for (int i = 0; i < Projectiles.Length; i++)
            {
                Projectiles[i].Advance(SimFrame, options.Global.WallsX);
            }
        }

        private void AddProjectileBoxes(GameOptions options)
        {
            for (int i = 0; i < Projectiles.Length; i++)
            {
                if (!Projectiles[i].Active)
                    continue;

                var projConfigs = options.Players[Projectiles[i].Owner].Character.Projectiles;
                if (projConfigs == null || Projectiles[i].ConfigIndex >= projConfigs.Count)
                    continue;

                Projectiles[i].AddBoxes(SimFrame, projConfigs[Projectiles[i].ConfigIndex], PhysicsCtx.Physics, i);
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

            AddProjectileBoxes(options);

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
                else if (c.BoxA.Data.Kind == HitboxKind.Grabbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
                {
                    hitPair = (c.BoxA.Owner, c.BoxB.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Grabbox)
                {
                    hitPair = (c.BoxB.Owner, c.BoxA.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
                {
                    bool aIsProjectile = c.BoxA.ProjectileIndex >= 0;
                    bool bIsProjectile = c.BoxB.ProjectileIndex >= 0;

                    if (aIsProjectile || bIsProjectile)
                    {
                        // Destroy any projectile(s) involved — no clank
                        if (aIsProjectile)
                            Projectiles[c.BoxA.ProjectileIndex].MarkedForDestroy = true;
                        if (bIsProjectile)
                            Projectiles[c.BoxB.ProjectileIndex].MarkedForDestroy = true;
                    }
                    else
                    {
                        // Both are fighter hitboxes — normal clank
                        clank = c;
                    }
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

                    int stopTicks =
                        outcome.Kind == HitKind.Blocked ? outcome.Props.BlockstopTicks : outcome.Props.HitstopTicks;
                    HitstopFramesRemaining = Mathsf.Min(Mathsf.Max(stopTicks, HitstopFramesRemaining), 12);

                    var attackerBox = collision.BoxA.Owner == owners.Item1 ? collision.BoxA : collision.BoxB;

                    if (outcome.Kind == HitKind.Hit)
                    {
                        sfloat damage = outcome.Props.Damage;
                        UpdateHype(options, attackerBox.Owner, damage);

                        // Refund the half-SuperCost pre-charge that was
                        // taken when IsSuperAttack was committed. A whiff
                        // never reaches this branch, so the pre-charge
                        // stands and the super costs 50%.
                        if (Fighters[attackerBox.Owner].IsSuperAttack)
                        {
                            Fighters[attackerBox.Owner].Super = Mathsf.Min(
                                Fighters[attackerBox.Owner].Super + options.Global.SuperCost / (sfloat)2,
                                options.Global.SuperMax
                            );
                        }
                    }

                    if (outcome.Kind == HitKind.Hit || outcome.Kind == HitKind.Blocked)
                    {
                        if (attackerBox.ProjectileIndex >= 0)
                        {
                            Projectiles[attackerBox.ProjectileIndex].MarkedForDestroy = true;
                        }
                    }

                    //to start a rhythm combo, we must sure that the move was not traded
                    if (
                        options.Players[owners.Item1].ComboMode == ComboMode.Assisted
                        && !PhysicsCtx.HurtHitCollisions.ContainsKey((owners.Item2, owners.Item1))
                        && GameMode == GameMode.Fighting
                        && outcome.Kind == HitKind.Hit
                        && PendingRhythmComboAttacker < 0
                        && Fighters[owners.Item1].IsSuperAttack
                    )
                    {
                        // Defer the actual combo generation until after the
                        // rest of this Advance finishes (crucially, after
                        // UpdatePosition). That way the clone the combo
                        // generator receives already has frame F's velocity
                        // integration applied and its simulation stays in
                        // lockstep with the real game, instead of running
                        // one UpdatePosition tick behind. We still transition
                        // to ManiaStart immediately so the death check below
                        // (which gates on GameMode == Fighting/Mania) is
                        // suppressed and behaves exactly as before.
                        GameMode = GameMode.ManiaStart;
                        ModeStart = RealFrame;
                        PendingRhythmComboAttacker = owners.Item1;
                        // TODO: show mania screen only after the maximum rollback frames to ensure no visual artifacting
                    }

                    // Blocked super: the attack is spent without ever
                    // entering the mania (where super would normally
                    // dissipate). The 50% penalty was already taken as
                    // the pre-charge on commit; we simply skip the
                    // refund and clear the super flags so a later frame
                    // of the same move can't still trigger a combo.
                    if (outcome.Kind == HitKind.Blocked && Fighters[owners.Item1].IsSuperAttack)
                    {
                        Fighters[owners.Item1].IsSuperAttack = false;
                        Fighters[owners.Item1].SuperComboBeats = 0;
                    }

                    // Add super checking to start a combo so that the combo only starts if the meter is alr at max
                    if (outcome.Kind == HitKind.Hit && GameMode == GameMode.Fighting)
                    {
                        sfloat damage = outcome.Props.Damage;
                        Fighters[attackerBox.Owner].AddSuper(damage, options);
                    }
                }
            }
            else if (clank.HasValue)
            {
                HandleCollision(options, clank.Value);
            }

            // handle hurt hurt always
            if (collide.HasValue)
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

        private void HandleClank(GameOptions options, Physics<BoxProps>.Collision c)
        {
            if (c.BoxA.Data.Kind != HitboxKind.Hitbox || c.BoxB.Data.Kind != HitboxKind.Hitbox)
            {
                throw new InvalidOperationException("Not clank");
            }

            SVector2 midpoint = (c.BoxA.Box.Pos + c.BoxB.Box.Pos) * (sfloat)0.5f;

            // TODO: check if moves are allowed to clank
            Fighters[c.BoxA.Owner].ApplyClank(SimFrame, options, midpoint);
            Fighters[c.BoxB.Owner].ApplyClank(SimFrame, options, midpoint);
        }

        private void HandlePush(GameOptions options, Physics<BoxProps>.Collision c)
        {
            if (c.BoxA.Data.Kind != HitboxKind.Hurtbox || c.BoxB.Data.Kind != HitboxKind.Hurtbox)
            {
                throw new InvalidOperationException("Not push");
            }

            if (
                Fighters[c.BoxA.Owner].State == CharacterState.Grabbed
                || Fighters[c.BoxB.Owner].State == CharacterState.Grabbed
            )
            {
                return;
            }

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

        private HitOutcome HandleHit(
            GameOptions options,
            Physics<BoxProps>.BoxEntry attacker,
            Physics<BoxProps>.BoxEntry defender
        )
        {
            if (
                (attacker.Data.Kind != HitboxKind.Hitbox && attacker.Data.Kind != HitboxKind.Grabbox)
                || defender.Data.Kind != HitboxKind.Hurtbox
            )
            {
                throw new InvalidOperationException("Not hit");
            }

            if (attacker.Data.Kind == HitboxKind.Grabbox)
            {
                Fighters[defender.Owner]
                    .ApplyGrab(SimFrame, attacker.Data, attacker.Box.Pos, Fighters[attacker.Owner].FacingDir);
                Fighters[attacker.Owner].ProcessHit(SimFrame, attacker.Data, options.Players[attacker.Owner].Character);
                return new HitOutcome { Kind = HitKind.Grabbed, Props = attacker.Data };
            }

            sfloat hypeMult = 1 + (sfloat)0.2f * (HypeMeter / options.Global.MaxHype) * (attacker.Owner * -2 + 1);

            CharacterState move = Fighters[attacker.Owner].State;
            CharacterState[] staleBuf = Fighters[attacker.Owner].StalingBuffer;
            int occurrences = 0;
            for (int i = 0; i < staleBuf.Length; i++)
            {
                if (staleBuf[i] == move)
                    occurrences++;
            }

            sfloat comboMult = DamageScale(Fighters[defender.Owner].ComboedCount);
            sfloat staleMult = DamageScale(occurrences);
            sfloat mult = hypeMult * comboMult * staleMult;

            HitOutcome outcome = Fighters[defender.Owner]
                .ApplyHit(
                    SimFrame,
                    Fighters[attacker.Owner].StateStart,
                    options.Players[defender.Owner].Character,
                    attacker.Data,
                    defender.Box.ClosestPointToCenter(attacker.Box),
                    mult
                );
            if (outcome.Kind == HitKind.Hit)
            {
                // Apply on-hit state transitions, which are not allowed on block
                // TODO: what happens if trade?
                Fighters[attacker.Owner]
                    .ProcessHit(SimFrame, attacker.Data, options.Players[attacker.Owner].Character);
            }

            if (outcome.Kind == HitKind.Hit || outcome.Kind == HitKind.Blocked)
            {
                Fighters[attacker.Owner].AttackConnected = true;
                ref FighterState atk = ref Fighters[attacker.Owner];
                atk.StalingBuffer[atk.StalingBufferIndex] = move;
                atk.StalingBufferIndex = (atk.StalingBufferIndex + 1) % atk.StalingBuffer.Length;
            }
            return outcome;
        }

        // Geometric decay: 0.9^n, floored at 0.25. Shared by combo and staling scaling.
        private static sfloat DamageScale(int n)
        {
            sfloat m = sfloat.One;
            int cap = Mathsf.Min(n, 14); // 0.9^14 < 0.25
            for (int i = 0; i < cap; i++)
            {
                m *= (sfloat)0.9f;
            }
            return Mathsf.Max((sfloat)0.25f, m);
        }

        private HitOutcome HandleCollision(GameOptions options, Physics<BoxProps>.Collision c)
        {
            if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                return HandleHit(options, c.BoxA, c.BoxB);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                return HandleHit(options, c.BoxB, c.BoxA);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Grabbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                return HandleHit(options, c.BoxA, c.BoxB);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Grabbox)
            {
                return HandleHit(options, c.BoxB, c.BoxA);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                HandleClank(options, c);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                HandlePush(options, c);
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
