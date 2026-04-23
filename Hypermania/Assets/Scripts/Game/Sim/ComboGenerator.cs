using System;
using System.Buffers;
using System.Collections.Generic;
using Design.Animation;
using Design.Configs;
using MemoryPack;
using Netcode.Rollback;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    /// <summary>
    /// Which slot a <see cref="GeneratedComboMove"/> fills. The three kinds are
    /// mutually exclusive.
    /// </summary>
    public enum ComboMoveKind
    {
        /// <summary>Landed attack. <see cref="GeneratedComboMove.Input"/>
        /// carries the tier bit (optionally OR'd with <c>Down</c>).</summary>
        Attack,

        /// <summary>Dash or jump note emitted when no attack qualified.
        /// Resets the progression constraint so the next attack starts
        /// fresh.</summary>
        Movement,

        /// <summary>Empty beat (<see cref="GeneratedComboMove.Input"/> =
        /// <see cref="InputFlags.None"/>) the player still has to press.
        /// Emitted as the second half of an on-beat attack+no-op pair when
        /// an otherwise-valid attack would have bled hitstop into this
        /// beat's window; grants a decaying damage bonus to the next
        /// attack.</summary>
        NoOp,
    }

    public struct GeneratedComboMove
    {
        public InputFlags Input;
        public Frame BeatFrame;
        public ComboMoveKind Kind;
    }

    /// <summary>
    /// A snapshot of the generator's <c>_working</c> state captured at the end
    /// of a beat's hit window (<c>noteTick + HitHalfRange</c>, clamped to
    /// <c>nextBeat - 1</c>). Used by <see cref="ComboVerifyDebug"/> to verify
    /// that the real simulation reaches an equivalent fighter state at the
    /// same frame regardless of where inside the hit window the player
    /// actually pressed.
    /// </summary>
    public struct ComboBeatSnapshot
    {
        public Frame CompareFrame;
        public GameState Predicted;
    }

    public struct GeneratedCombo
    {
        public List<GeneratedComboMove> Moves;
        public Frame EndFrame;

        /// <summary>
        /// Per-beat snapshots of the generator's <c>_working</c> state.
        /// Populated only when <see cref="InfoOptions.VerifyComboPrediction"/>
        /// is enabled, otherwise null.
        /// </summary>
        public List<ComboBeatSnapshot> BeatSnapshots;
    }

    /// <summary>
    /// Generates a dynamic combo for a rhythm pattern by simulating candidate
    /// moves against a working copy of the game state. The generator owns the
    /// working state and a beat snapshot, so each beat is advanced exactly
    /// once: candidates are tried by reverting to the snapshot, not by cloning
    /// from scratch on every try.
    ///
    /// <para>Invariants the per-beat code depends on:</para>
    /// <list type="bullet">
    ///   <item><b>Dispatch-frame alignment.</b> The real game's
    ///     <see cref="GameState.DoManiaStep"/> withholds each note's Input
    ///     event until <c>RealFrame = noteTick + HitHalfRange</c>. The
    ///     generator matches this by stopping one frame short of the
    ///     dispatch frame, then applying the attacker input with a single
    ///     <see cref="AdvanceOnce"/> call — see <see cref="ApplyInputAtBeat"/>.
    ///     Advancing TO the dispatch frame before applying would consume it
    ///     with empty input and desync by one.</item>
    ///   <item><b>AlwaysRhythmCancel is one-frame only.</b> It must be true
    ///     ONLY on the frame the attacker input is applied. Leaving it on
    ///     across inter-beat empty-input advances lets a buffered press
    ///     retrigger mid-recovery and overwrite crouching variants with
    ///     standing ones, desyncing the sim from the real Mania game.</item>
    ///   <item><b>_working advances monotonically.</b> Candidate trials
    ///     revert to <see cref="_beatSnapshot"/>; the movement lookahead
    ///     uses a secondary <see cref="_lookaheadSnapshot"/> so its nested
    ///     trials don't clobber <see cref="_beatSnapshot"/>.</item>
    /// </list>
    /// </summary>
    public class ComboGenerator
    {
        /// <summary>
        /// Maximum frames to simulate when testing if a move hits.
        /// Should cover the longest attack animation (startup + active).
        /// </summary>
        private const int MAX_TEST_FRAMES = 40;

        /// <summary>
        /// Default trailing pad past the last authored note before the mania
        /// deactivates. Half a second at 60 TPS — used when the pattern is
        /// too short to derive a pad from the gap between the last two
        /// beats.
        /// </summary>
        private const int DEFAULT_TRAILING_PAD = 30;

        /// <summary>
        /// Extra buffer past the last beat so the finisher's hit still
        /// registers while GameMode == Mania. Otherwise it lands in
        /// Fighting mode and spuriously grants super meter from the combo
        /// itself.
        /// </summary>
        private const int POST_FINISHER_BUFFER = 10;

        /// <summary>
        /// Bitmask of every attack tier. Used to extract the single tier
        /// bit carried by a candidate input.
        /// </summary>
        private const InputFlags AttackTierMask =
            InputFlags.LightAttack | InputFlags.MediumAttack | InputFlags.HeavyAttack | InputFlags.SpecialAttack;

        /// <summary>
        /// All 8 attack candidates (4 tiers × {standing, crouching}) as a
        /// flat list so the per-beat trial loops don't need the tier ×
        /// Down-modifier nest.
        /// </summary>
        private static readonly InputFlags[] AllAttackVariants =
        {
            InputFlags.LightAttack,
            InputFlags.LightAttack | InputFlags.Down,
            InputFlags.MediumAttack,
            InputFlags.MediumAttack | InputFlags.Down,
            InputFlags.HeavyAttack,
            InputFlags.HeavyAttack | InputFlags.Down,
            InputFlags.SpecialAttack,
            InputFlags.SpecialAttack | InputFlags.Down,
        };

        /// <summary>
        /// Distinct <see cref="DeterministicHash"/> contexts so the strict
        /// and relaxed passes within one beat produce independent pick
        /// sequences (and so future passes can be added without bit-patching
        /// the hash at the call site).
        /// </summary>
        private enum HashSalt
        {
            Strict = 0,
            Relaxed = 1,
        }

        /// <summary>
        /// Serialization scratch for <see cref="CloneInto"/>. Per-instance —
        /// the generator is a single-use object driven synchronously by
        /// <see cref="Generate"/>, never shared across threads.
        /// </summary>
        private readonly ArrayBufferWriter<byte> _cloneWriter = new ArrayBufferWriter<byte>(4096);

        /// <summary>
        /// Canonical simulation state that advances monotonically through the
        /// pattern. Every candidate trial reverts this back to
        /// <see cref="_beatSnapshot"/>.
        /// </summary>
        private GameState _working;

        /// <summary>
        /// Snapshot of <see cref="_working"/> at the start of the current
        /// beat, used to revert between candidate tests within a single beat.
        /// </summary>
        private GameState _beatSnapshot;

        /// <summary>
        /// Secondary snapshot used by the movement lookahead path. Holds the
        /// post-movement state at the next beat so each trial attack can be
        /// tested in isolation without clobbering <see cref="_beatSnapshot"/>.
        /// </summary>
        private GameState _lookaheadSnapshot;

        private GameOptions _options;
        private int _attackerIndex;
        private CharacterConfig _attackerConfig;

        /// <summary>
        /// Half-window (in frames) of the rhythm note hit window, matching
        /// <see cref="ManiaConfig.HitHalfRange"/>. A note at BeatFrame can be
        /// hit during [BeatFrame − _noteHitHalfRange, BeatFrame +
        /// _noteHitHalfRange].
        /// </summary>
        private int _noteHitHalfRange;

        /// <summary>Cached <see cref="AudioConfig"/> for on-beat tests.</summary>
        private AudioConfig _audio;

        /// <summary>
        /// Reusable input buffer for <see cref="AdvanceOnce"/>. Sized to
        /// <c>Fighters.Length</c> at the start of <see cref="Run"/>; only the
        /// attacker's slot is mutated per call.
        /// </summary>
        private (GameInput input, InputStatus status)[] _inputScratch;

        /// <summary>
        /// Cache of move reach (max horizontal hitbox/grabbox extent from
        /// attacker origin) per <see cref="CharacterState"/>. Reach is
        /// config-static so it only needs to be computed once per run.
        /// </summary>
        private readonly Dictionary<CharacterState, sfloat> _reachCache = new Dictionary<CharacterState, sfloat>();

        private struct MoveTestResult
        {
            public InputFlags Input;
            public sfloat KnockbackSqr;
            public sfloat Reach;
        }

        /// <summary>
        /// Static shim so existing callers (RhythmComboManager) continue to
        /// work without constructing a generator themselves.
        /// </summary>
        public static GeneratedCombo Generate(
            in GameState state,
            GameOptions options,
            int attackerIndex,
            Frame[] noteFrames,
            int gameHitstop
        )
        {
            ComboGenerator gen = new ComboGenerator();
            return gen.Run(state, options, attackerIndex, noteFrames, gameHitstop);
        }

        public GeneratedCombo Run(
            in GameState state,
            GameOptions options,
            int attackerIndex,
            Frame[] noteFrames,
            int gameHitstop
        )
        {
            if (noteFrames == null || noteFrames.Length == 0)
            {
                return new GeneratedCombo { Moves = new List<GeneratedComboMove>(), EndFrame = state.RealFrame };
            }

            InitializeFromCaller(options, attackerIndex);
            SeedWorkingState(state, gameHitstop, noteFrames[0]);

            List<GeneratedComboMove> moves = new List<GeneratedComboMove>();
            List<MoveTestResult> candidates = new List<MoveTestResult>();
            List<ComboBeatSnapshot> beatSnapshots =
                options.InfoOptions != null && options.InfoOptions.VerifyComboPrediction
                    ? new List<ComboBeatSnapshot>()
                    : null;

            // Progression constraint: any move after the first must strictly
            // exceed the previous move on knockback OR reach. Movement (dash
            // fallback) resets the constraint.
            bool hasPrev = false;
            sfloat prevKb = sfloat.Zero;
            sfloat prevReach = sfloat.Zero;

            for (int i = 0; i < noteFrames.Length; i++)
            {
                Frame currentBeat = noteFrames[i];
                Frame nextBeat = (i + 1 < noteFrames.Length) ? noteFrames[i + 1] : Frame.Infinity;
                bool isLastBeat = i == noteFrames.Length - 1;

                AdvanceToDispatchOf(currentBeat);
                SnapshotWorking();

                if (
                    TryStrictAttack(
                        state,
                        candidates,
                        i,
                        isLastBeat,
                        hasPrev,
                        prevKb,
                        prevReach,
                        currentBeat,
                        nextBeat,
                        moves,
                        beatSnapshots,
                        out MoveTestResult strict
                    )
                )
                {
                    hasPrev = true;
                    prevKb = strict.KnockbackSqr;
                    prevReach = strict.Reach;
                    continue;
                }

                // On-beat no-op pair: requires the current note to sit on
                // a quarter-note grid position, and a beat i+2 to exist so
                // the relaxed hitstop check has a target window.
                bool canTryNoOp = i + 2 < noteFrames.Length && _audio.IsOnBeat(currentBeat);
                if (
                    canTryNoOp
                    && TryOnBeatNoOpPair(
                        state,
                        candidates,
                        i,
                        hasPrev,
                        prevKb,
                        prevReach,
                        currentBeat,
                        nextBeat,
                        noteFrames[i + 2],
                        moves,
                        beatSnapshots,
                        out MoveTestResult relaxed
                    )
                )
                {
                    hasPrev = true;
                    prevKb = relaxed.KnockbackSqr;
                    prevReach = relaxed.Reach;
                    i++; // skip the beat consumed as the no-op
                    continue;
                }

                Frame beatAfterNext = (i + 2 < noteFrames.Length) ? noteFrames[i + 2] : Frame.Infinity;
                CommitMovementFallback(currentBeat, nextBeat, beatAfterNext, moves, beatSnapshots);
                hasPrev = false;
                prevKb = sfloat.Zero;
                prevReach = sfloat.Zero;
            }

            return new GeneratedCombo
            {
                Moves = moves,
                EndFrame = ComputeEndFrame(noteFrames),
                BeatSnapshots = beatSnapshots,
            };
        }

        // ------------------------------------------------------------------
        // Run setup
        // ------------------------------------------------------------------

        private void InitializeFromCaller(GameOptions options, int attackerIndex)
        {
            _attackerIndex = attackerIndex;
            _attackerConfig = options.Players[attackerIndex].Character;
            _noteHitHalfRange = (int)options.Players[attackerIndex].BeatCancelWindow;
            _audio = options.Global.Audio;

            // Clone Players so we can suppress the attacker's ComboMode on
            // the generator's copy (forcing Freestyle) without leaking the
            // change back to the real game's shared PlayerOptions. Prevents
            // the generator's inner simulation from recursively triggering
            // mania when its own super-hit connects.
            PlayerOptions[] clonedPlayers = new PlayerOptions[options.Players.Length];
            for (int p = 0; p < options.Players.Length; p++)
            {
                clonedPlayers[p] = options.Players[p];
            }
            PlayerOptions atk = options.Players[attackerIndex];
            clonedPlayers[attackerIndex] = new PlayerOptions
            {
                HealOnActionable = atk.HealOnActionable,
                SuperMaxOnActionable = atk.SuperMaxOnActionable,
                BurstMaxOnActionable = atk.BurstMaxOnActionable,
                Immortal = atk.Immortal,
                Character = atk.Character,
                SkinIndex = atk.SkinIndex,
                ComboMode = ComboMode.Freestyle,
                ManiaDifficulty = atk.ManiaDifficulty,
                BeatCancelWindow = atk.BeatCancelWindow,
            };

            _options = new GameOptions
            {
                Global = options.Global,
                Players = clonedPlayers,
                LocalPlayers = options.LocalPlayers,
                InfoOptions = options.InfoOptions,
                AlwaysRhythmCancel = false,
            };

            // One reusable input buffer; only the attacker's slot mutates
            // across AdvanceOnce calls.
            _inputScratch = new (GameInput input, InputStatus status)[options.Players.Length];
            for (int i = 0; i < _inputScratch.Length; i++)
            {
                _inputScratch[i] = (GameInput.None, InputStatus.Confirmed);
            }
        }

        private void SeedWorkingState(in GameState state, int gameHitstop, Frame firstBeatFrame)
        {
            _working = null;
            CloneInto(ref _working, state);
            _working.RoundEnd = Frame.Infinity;
            for (int i = 0; i < _working.Fighters.Length; i++)
            {
                _working.Fighters[i].Health = sfloat.PositiveInfinity;
            }

            // Mania alignment preamble: use the game's hitstop (which aligns
            // to the next quarter-note beat boundary) so the simulation's
            // hitstop/slow-mo split matches the real game exactly.
            _working.HitstopFramesRemaining = gameHitstop;
            _working.GameMode = GameMode.ManiaStart;
            _working.ModeStart = _working.RealFrame;

            AdvanceToDispatchOf(firstBeatFrame);

            // Override back to Fighting so candidate trials run at full
            // speed instead of through the ManiaStart slow-mo curve.
            _working.GameMode = GameMode.Fighting;
            _working.SpeedRatio = (sfloat)1f;
        }

        private static Frame ComputeEndFrame(Frame[] noteFrames)
        {
            // Trail the last note by the gap of the authored slice, so the
            // window closes at a musically sensible distance past the
            // finisher. Fall back to a fixed pad for single-note slices.
            int trailingPad = DEFAULT_TRAILING_PAD;
            if (noteFrames.Length >= 2)
            {
                int lastGap = noteFrames[noteFrames.Length - 1] - noteFrames[noteFrames.Length - 2];
                if (lastGap > 0)
                    trailingPad = lastGap;
            }
            trailingPad += POST_FINISHER_BUFFER;
            return noteFrames[noteFrames.Length - 1] + trailingPad;
        }

        // ------------------------------------------------------------------
        // Per-beat passes
        // ------------------------------------------------------------------

        /// <summary>
        /// Try every attack candidate under the strict rule (no hitstop
        /// overlap with <paramref name="nextBeat"/>'s window), pick the best
        /// via <see cref="PickFinisher"/> or <see cref="PickChainLink"/>,
        /// and commit it if found.
        /// </summary>
        private bool TryStrictAttack(
            in GameState state,
            List<MoveTestResult> candidates,
            int beatIndex,
            bool isLastBeat,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            Frame currentBeat,
            Frame nextBeat,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots,
            out MoveTestResult chosen
        )
        {
            candidates.Clear();
            for (int k = 0; k < AllAttackVariants.Length; k++)
            {
                TryHitFromBeatSnapshot(candidates, AllAttackVariants[k], nextBeat);
            }

            int hash = DeterministicHash(state.RealFrame.No, beatIndex, HashSalt.Strict);
            int idx = isLastBeat
                ? PickFinisher(candidates, hash)
                : PickChainLink(candidates, hasPrev, prevKb, prevReach, hash);
            if (idx < 0)
            {
                chosen = default;
                return false;
            }

            chosen = candidates[idx];
            CommitFromBeatSnapshot(chosen.Input, currentBeat, nextBeat, ComboMoveKind.Attack, moves, beatSnapshots);
            return true;
        }

        /// <summary>
        /// Relaxed pass used when <paramref name="currentBeat"/> sits on a
        /// quarter-note grid position. Allows an attack whose hitstop bleeds
        /// into <paramref name="nextBeat"/>'s window, provided the hitstop
        /// clears before <paramref name="beatAfterNoop"/>'s window. On
        /// success, commits the attack on <paramref name="currentBeat"/> and
        /// a no-op on <paramref name="nextBeat"/>.
        /// </summary>
        private bool TryOnBeatNoOpPair(
            in GameState state,
            List<MoveTestResult> candidates,
            int beatIndex,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            Frame currentBeat,
            Frame nextBeat,
            Frame beatAfterNoop,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots,
            out MoveTestResult chosen
        )
        {
            candidates.Clear();
            for (int k = 0; k < AllAttackVariants.Length; k++)
            {
                TryHitFromBeatSnapshot(candidates, AllAttackVariants[k], beatAfterNoop);
            }

            int hash = DeterministicHash(state.RealFrame.No, beatIndex, HashSalt.Relaxed);
            int idx = PickChainLink(candidates, hasPrev, prevKb, prevReach, hash);
            if (idx < 0)
            {
                chosen = default;
                return false;
            }

            chosen = candidates[idx];
            CommitFromBeatSnapshot(chosen.Input, currentBeat, nextBeat, ComboMoveKind.Attack, moves, beatSnapshots);
            CommitNoOpBeat(nextBeat, beatAfterNoop, moves, beatSnapshots);
            return true;
        }

        /// <summary>
        /// Emit a movement (dash/jump) on <paramref name="currentBeat"/> as
        /// a last resort. Prefers a lookahead-validated choice that sets up
        /// a direct attack on <paramref name="nextBeat"/>; falls back to the
        /// preferred movement (jump if defender airborne, else dash) if no
        /// setup qualifies. Resets the progression constraint (the caller
        /// clears <c>hasPrev</c>/<c>prevKb</c>/<c>prevReach</c>).
        /// </summary>
        private void CommitMovementFallback(
            Frame currentBeat,
            Frame nextBeat,
            Frame beatAfterNext,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots
        )
        {
            // Candidate trials left _working advanced up to MAX_TEST_FRAMES
            // past the beat, during which FaceTowards (and, for grab
            // candidates, back-throw) can have flipped the attacker's
            // FacingDir. Restore to the pristine beat snapshot so the
            // forward-direction and defender-airborne reads reflect the
            // beat itself, not the tail of the last probed attack —
            // otherwise the emitted Dash/Up note can point backwards on a
            // cross-up.
            RestoreWorking();
            InputFlags forwardInput = _working.Fighters[_attackerIndex].ForwardInput;
            InputFlags dashMove = InputFlags.Dash | forwardInput;
            InputFlags jumpMove = InputFlags.Up | forwardInput;

            bool defenderAirborne = _working.Fighters[1 - _attackerIndex].Location == FighterLocation.Airborne;
            InputFlags firstTry = defenderAirborne ? jumpMove : dashMove;
            InputFlags secondTry = defenderAirborne ? dashMove : jumpMove;

            InputFlags chosenMovement;
            if (nextBeat < Frame.Infinity && TryMovementLookahead(firstTry, nextBeat, beatAfterNext))
            {
                chosenMovement = firstTry;
            }
            else if (nextBeat < Frame.Infinity && TryMovementLookahead(secondTry, nextBeat, beatAfterNext))
            {
                chosenMovement = secondTry;
            }
            else
            {
                chosenMovement = firstTry;
            }

            CommitFromBeatSnapshot(chosenMovement, currentBeat, nextBeat, ComboMoveKind.Movement, moves, beatSnapshots);
        }

        // ------------------------------------------------------------------
        // Commit helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Revert to <see cref="_beatSnapshot"/>, apply
        /// <paramref name="input"/> on the current beat's dispatch frame,
        /// append the move, and take a verify-snapshot.
        /// </summary>
        private void CommitFromBeatSnapshot(
            InputFlags input,
            Frame currentBeat,
            Frame nextBeat,
            ComboMoveKind kind,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots
        )
        {
            RestoreWorking();
            ApplyInputToWorking(input);
            moves.Add(
                new GeneratedComboMove
                {
                    Input = input,
                    BeatFrame = currentBeat,
                    Kind = kind,
                }
            );
            CaptureBeatSnapshot(beatSnapshots, currentBeat, nextBeat);
        }

        /// <summary>
        /// Advance <see cref="_working"/> forward to
        /// <paramref name="noOpBeat"/>'s dispatch frame, consume it with an
        /// empty input, register the no-op bonus (mirroring
        /// <see cref="GameState.DoManiaStep"/>'s Input-event side effect so
        /// the verify snapshot stays byte-equivalent), and append the note.
        /// </summary>
        private void CommitNoOpBeat(
            Frame noOpBeat,
            Frame beatAfterNoop,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots
        )
        {
            ApplyInputAtBeat(noOpBeat, InputFlags.None);
            _working.Fighters[_attackerIndex].RegisterManiaNoOp();
            moves.Add(
                new GeneratedComboMove
                {
                    Input = InputFlags.None,
                    BeatFrame = noOpBeat,
                    Kind = ComboMoveKind.NoOp,
                }
            );
            CaptureBeatSnapshot(beatSnapshots, noOpBeat, beatAfterNoop);
        }

        // ------------------------------------------------------------------
        // Hit trial (strict / lookahead share this)
        // ------------------------------------------------------------------

        /// <summary>
        /// Probe whether <paramref name="input"/>, applied from
        /// <paramref name="snapshotSource"/>, connects within
        /// <see cref="MAX_TEST_FRAMES"/> without bleeding hitstop into
        /// <paramref name="windowBeat"/>'s hit window. On success returns
        /// true and populates <paramref name="hitProps"/> with the BoxProps
        /// of the defender's <c>HitProps</c>.
        /// </summary>
        private bool TryHit(ref GameState snapshotSource, InputFlags input, Frame windowBeat, out BoxProps hitProps)
        {
            hitProps = default;

            // Respect per-move opt-out: moves whose HitboxData.ComboEligible
            // is false must never appear in a generated combo.
            CharacterState cs = MapInputToState(input);
            HitboxData data = _attackerConfig.GetHitboxData(cs);
            if (data == null || !data.ComboEligible)
                return false;

            CloneInto(ref _working, snapshotSource);

            int defenderIndex = 1 - _attackerIndex;
            bool checkWindow = windowBeat < Frame.Infinity;
            Frame windowStart = checkWindow ? windowBeat - _noteHitHalfRange : Frame.Infinity;
            Frame windowEnd = checkWindow ? windowBeat + _noteHitHalfRange : Frame.Infinity;

            bool hit = false;
            bool hitstopInWindow = false;

            // Phase 1: advance up to MAX_TEST_FRAMES looking for the hit.
            // While advancing, also watch for the game being in hitstop
            // inside the window — residual hitstop from a prior move can
            // land in the window even before this candidate connects.
            for (int frame = 0; frame < MAX_TEST_FRAMES; frame++)
            {
                InputFlags flags = frame == 0 ? input : InputFlags.None;
                _options.AlwaysRhythmCancel = frame == 0;
                AdvanceOnce(flags);

                if (checkWindow && IsHitstopInWindow(windowStart, windowEnd))
                    hitstopInWindow = true;

                if (_working.Fighters[defenderIndex].HitProps.HasValue)
                {
                    hit = true;
                    hitProps = _working.Fighters[defenderIndex].HitProps.Value;
                    break;
                }
            }
            _options.AlwaysRhythmCancel = false;

            if (!hit)
                return false;

            // Phase 2: the current move connected. Continue advancing
            // through the end of the window, checking whether the fresh
            // hitstop overlaps. Early-exit on first overlap.
            if (checkWindow && !hitstopInWindow)
            {
                while (_working.RealFrame < windowEnd)
                {
                    AdvanceOnce(InputFlags.None);
                    if (IsHitstopInWindow(windowStart, windowEnd))
                    {
                        hitstopInWindow = true;
                        break;
                    }
                }
            }

            return !hitstopInWindow;
        }

        /// <summary>Run <see cref="TryHit"/> from
        /// <see cref="_beatSnapshot"/>; on success append a
        /// <see cref="MoveTestResult"/> with cached knockback/reach.</summary>
        private void TryHitFromBeatSnapshot(List<MoveTestResult> candidates, InputFlags input, Frame windowBeat)
        {
            if (!TryHit(ref _beatSnapshot, input, windowBeat, out BoxProps hitProps))
                return;
            candidates.Add(
                new MoveTestResult
                {
                    Input = input,
                    KnockbackSqr = hitProps.Knockback.sqrMagnitude,
                    Reach = GetReach(input),
                }
            );
        }

        /// <summary>Run <see cref="TryHit"/> from
        /// <see cref="_lookaheadSnapshot"/>; caller only needs pass/fail.</summary>
        private bool TryHitFromLookaheadSnapshot(InputFlags input, Frame windowBeat)
        {
            return TryHit(ref _lookaheadSnapshot, input, windowBeat, out _);
        }

        /// <summary>
        /// Lookahead: restore to the current beat snapshot, apply
        /// <paramref name="movement"/> on the beat frame, advance to
        /// <paramref name="nextBeat"/>'s dispatch frame, and test whether
        /// any grounded attack (standing or crouching, each tier) would
        /// land a direct hit from that post-movement position without
        /// causing hitstop overlap with <paramref name="beatAfterNext"/>'s
        /// window. Returns true on the first attack that passes both
        /// checks. Mutates <see cref="_working"/>; the caller is expected
        /// to <see cref="RestoreWorking"/> before committing.
        /// </summary>
        private bool TryMovementLookahead(InputFlags movement, Frame nextBeat, Frame beatAfterNext)
        {
            RestoreWorking();
            ApplyInputToWorking(movement);
            AdvanceToDispatchOf(nextBeat);
            CloneInto(ref _lookaheadSnapshot, _working);

            for (int k = 0; k < AllAttackVariants.Length; k++)
            {
                if (TryHitFromLookaheadSnapshot(AllAttackVariants[k], beatAfterNext))
                    return true;
            }
            return false;
        }

        private bool IsHitstopInWindow(Frame windowStart, Frame windowEnd)
        {
            return _working.RealFrame >= windowStart
                && _working.RealFrame <= windowEnd
                && _working.HitstopFramesRemaining > 0;
        }

        // ------------------------------------------------------------------
        // Candidate selection
        // ------------------------------------------------------------------

        /// <summary>
        /// Finisher rule (last beat of the pattern, no chain to build):
        /// prefer the LARGEST knockback, random-pick among every candidate
        /// sharing the winner's tier bit. No progression filter.
        /// </summary>
        private static int PickFinisher(List<MoveTestResult> pool, int hashValue)
        {
            if (pool.Count == 0)
                return -1;

            sfloat bestKb = sfloat.NegativeInfinity;
            InputFlags bestInput = InputFlags.None;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].KnockbackSqr > bestKb)
                {
                    bestKb = pool[i].KnockbackSqr;
                    bestInput = pool[i].Input;
                }
            }

            InputFlags tier = GetAttackTierBit(bestInput);
            Span<int> eligible = stackalloc int[pool.Count];
            int eCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if ((pool[i].Input & tier) != 0)
                    eligible[eCount++] = i;
            }
            return eCount == 0 ? -1 : eligible[hashValue % eCount];
        }

        /// <summary>
        /// Chain-link rule (non-last beat): candidates must strictly exceed
        /// the previous move on knockback OR reach (if a previous move
        /// exists). Among qualifying candidates, prefer the smallest
        /// knockback; if the winner is a heavy attack, random-pick among
        /// every qualifying heavy (standing or crouching), otherwise pick
        /// among candidates tied on knockback.
        /// </summary>
        private static int PickChainLink(
            List<MoveTestResult> pool,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            int hashValue
        )
        {
            sfloat bestKb = sfloat.PositiveInfinity;
            InputFlags bestInput = InputFlags.None;
            bool any = false;
            for (int i = 0; i < pool.Count; i++)
            {
                MoveTestResult c = pool[i];
                if (hasPrev && !(c.KnockbackSqr > prevKb || c.Reach > prevReach))
                    continue;
                if (!any || c.KnockbackSqr < bestKb)
                {
                    bestKb = c.KnockbackSqr;
                    bestInput = c.Input;
                    any = true;
                }
            }
            if (!any)
                return -1;

            bool bestIsHeavy = (bestInput & InputFlags.HeavyAttack) != 0;

            Span<int> eligible = stackalloc int[pool.Count];
            int eCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                MoveTestResult c = pool[i];
                if (hasPrev && !(c.KnockbackSqr > prevKb || c.Reach > prevReach))
                    continue;
                bool matches = bestIsHeavy ? (c.Input & InputFlags.HeavyAttack) != 0 : c.KnockbackSqr == bestKb;
                if (matches)
                    eligible[eCount++] = i;
            }
            return eCount == 0 ? -1 : eligible[hashValue % eCount];
        }

        // ------------------------------------------------------------------
        // Input classification (shared by TryHit and PickFinisher)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the tier bit (Light/Medium/Heavy/Special) carried by an
        /// attack input, or <see cref="InputFlags.None"/> if none is present.
        /// Candidates are always generated from a single tier bit OR'd with
        /// an optional <see cref="InputFlags.Down"/> modifier, so exactly
        /// one tier bit is set in practice.
        /// </summary>
        private static InputFlags GetAttackTierBit(InputFlags input) => input & AttackTierMask;

        /// <summary>
        /// Map an attack InputFlags (with optional Down modifier) to the
        /// corresponding grounded CharacterState. Mirrors the
        /// Standing/Crouching rows of <c>FighterState._attackDictionary</c>.
        /// </summary>
        private static CharacterState MapInputToState(InputFlags input)
        {
            bool crouching = (input & InputFlags.Down) != 0;
            switch (GetAttackTierBit(input))
            {
                case InputFlags.LightAttack:
                    return crouching ? CharacterState.LightCrouching : CharacterState.LightAttack;
                case InputFlags.MediumAttack:
                    return crouching ? CharacterState.MediumCrouching : CharacterState.MediumAttack;
                case InputFlags.HeavyAttack:
                    return crouching ? CharacterState.HeavyCrouching : CharacterState.HeavyAttack;
                case InputFlags.SpecialAttack:
                    return crouching ? CharacterState.SpecialCrouching : CharacterState.SpecialAttack;
                default:
                    return CharacterState.Idle;
            }
        }

        /// <summary>
        /// Maximum horizontal hitbox/grabbox extent from attacker origin for
        /// the given attack input, across every frame of the move. Uses
        /// attacker-local coordinates (not mirrored by facing) since
        /// <c>BoxData.CenterLocal</c> is stored facing-agnostic — see
        /// <c>FighterState.AddBoxes</c> where the X mirror is applied at
        /// read time.
        /// </summary>
        private sfloat GetReach(InputFlags input)
        {
            CharacterState state = MapInputToState(input);
            if (_reachCache.TryGetValue(state, out sfloat cached))
                return cached;

            HitboxData data = _attackerConfig.GetHitboxData(state);
            sfloat maxExtent = sfloat.Zero;
            if (data != null)
            {
                for (int f = 0; f < data.Frames.Count; f++)
                {
                    FrameData frame = data.Frames[f];
                    for (int b = 0; b < frame.Boxes.Count; b++)
                    {
                        BoxData box = frame.Boxes[b];
                        if (box.Props.Kind != HitboxKind.Hitbox && box.Props.Kind != HitboxKind.Grabbox)
                            continue;
                        sfloat right = box.CenterLocal.x + box.SizeLocal.x * (sfloat)0.5f;
                        if (right > maxExtent)
                            maxExtent = right;
                    }
                }
            }

            _reachCache[state] = maxExtent;
            return maxExtent;
        }

        /// <summary>
        /// Deterministic hash for tie-breaking, derived from game state so
        /// rollback produces identical selections.
        /// <paramref name="salt"/> keeps independent pass decisions (strict
        /// vs. relaxed) from picking the same index out of lookalike pools.
        /// </summary>
        private static int DeterministicHash(int realFrame, int beatIndex, HashSalt salt)
        {
            unchecked
            {
                int h = realFrame * 31 + beatIndex;
                h = h * 31 + (int)salt;
                h ^= h >> 16;
                h *= unchecked((int)0x45d9f3b);
                h ^= h >> 16;
                return h & 0x7FFFFFFF;
            }
        }

        // ------------------------------------------------------------------
        // State management: snapshot / restore / advance
        // ------------------------------------------------------------------

        private void SnapshotWorking()
        {
            CloneInto(ref _beatSnapshot, _working);
        }

        private void RestoreWorking()
        {
            CloneInto(ref _working, _beatSnapshot);
        }

        /// <summary>
        /// Capture a clone of <see cref="_working"/> at
        /// <paramref name="currentBeat"/>'s dispatch frame (or
        /// <paramref name="nextBeat"/> − 1 if that would land earlier).
        /// No-op when the caller passed a null list (verify flag off).
        /// </summary>
        private void CaptureBeatSnapshot(List<ComboBeatSnapshot> snapshots, Frame currentBeat, Frame nextBeat)
        {
            if (snapshots == null)
                return;

            Frame snapFrame = currentBeat + _noteHitHalfRange;
            if (nextBeat < Frame.Infinity)
            {
                Frame cap = nextBeat - 1;
                if (cap < snapFrame)
                    snapFrame = cap;
            }

            AdvanceWorkingTo(snapFrame);

            GameState cloned = null;
            CloneInto(ref cloned, _working);
            snapshots.Add(new ComboBeatSnapshot { CompareFrame = _working.RealFrame, Predicted = cloned });
        }

        /// <summary>
        /// Serialize <paramref name="src"/> and deserialize into
        /// <paramref name="dst"/> via a per-instance ArrayBufferWriter;
        /// passes the writer's span straight to <c>Deserialize</c> so no
        /// intermediate byte[] is allocated.
        /// </summary>
        private void CloneInto(ref GameState dst, GameState src)
        {
            _cloneWriter.Clear();
            MemoryPackSerializer.Serialize(_cloneWriter, src);
            dst = MemoryPackSerializer.Deserialize<GameState>(_cloneWriter.WrittenSpan);
        }

        /// <summary>
        /// Advance <see cref="_working"/> with empty inputs until its
        /// RealFrame reaches <paramref name="targetRealFrame"/>. The last
        /// <see cref="AdvanceOnce"/> processes
        /// <paramref name="targetRealFrame"/> itself with empty input.
        /// </summary>
        private void AdvanceWorkingTo(Frame targetRealFrame)
        {
            while (_working.RealFrame < targetRealFrame)
            {
                AdvanceOnce(InputFlags.None);
            }
        }

        /// <summary>
        /// Advance <see cref="_working"/> to one frame before
        /// <paramref name="beat"/>'s dispatch frame, so a subsequent
        /// <see cref="AdvanceOnce"/>(input) lands the input on the dispatch
        /// frame itself (see class-level invariants).
        /// </summary>
        private void AdvanceToDispatchOf(Frame beat)
        {
            AdvanceWorkingTo(beat + _noteHitHalfRange - 1);
        }

        /// <summary>
        /// Apply a single attacker input for one frame on
        /// <see cref="_working"/>, with rhythm cancel enabled for exactly
        /// that frame. Assumes <see cref="_working"/> is already at the
        /// dispatch frame − 1 (see <see cref="AdvanceToDispatchOf"/>).
        /// </summary>
        private void ApplyInputToWorking(InputFlags input)
        {
            _options.AlwaysRhythmCancel = true;
            AdvanceOnce(input);
            _options.AlwaysRhythmCancel = false;
        }

        /// <summary>
        /// Advance to the dispatch frame of <paramref name="beat"/> and
        /// apply <paramref name="input"/> there. The common "commit one
        /// chosen input at a beat" path.
        /// </summary>
        private void ApplyInputAtBeat(Frame beat, InputFlags input)
        {
            AdvanceToDispatchOf(beat);
            ApplyInputToWorking(input);
        }

        /// <summary>
        /// Advance <see cref="_working"/> by exactly one frame with
        /// <paramref name="attackerInput"/> in the attacker's slot. Uses the
        /// per-instance <see cref="_inputScratch"/> so no per-call
        /// allocation happens in this hot loop.
        /// </summary>
        private void AdvanceOnce(InputFlags attackerInput)
        {
            for (int i = 0; i < _inputScratch.Length; i++)
            {
                _inputScratch[i].input = i == _attackerIndex ? new GameInput(attackerInput) : GameInput.None;
            }
            _working.Advance(_options, _inputScratch);
        }
    }
}
