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
    public struct GeneratedComboMove
    {
        public InputFlags Input;
        public Frame BeatFrame;
        public bool IsMovement;
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
        /// Per-beat snapshots of the generator's <c>_working</c> state. Populated
        /// only when <see cref="InfoOptions.VerifyComboPrediction"/> is enabled,
        /// otherwise null.
        /// </summary>
        public List<ComboBeatSnapshot> BeatSnapshots;
    }

    /// <summary>
    /// Generates a dynamic combo for a rhythm pattern by simulating candidate
    /// moves against a working copy of the game state. The generator owns the
    /// working state and a beat snapshot, so each beat is advanced exactly once:
    /// candidates are tried by reverting to the snapshot, not by cloning from
    /// scratch on every try.
    /// </summary>
    public class ComboGenerator
    {
        /// <summary>
        /// Maximum frames to simulate when testing if a move hits.
        /// Should cover the longest attack animation (startup + active).
        /// </summary>
        private const int MAX_TEST_FRAMES = 40;

        private static readonly InputFlags[] AttackInputs =
        {
            InputFlags.LightAttack,
            InputFlags.MediumAttack,
            InputFlags.HeavyAttack,
            InputFlags.SpecialAttack,
        };

        [System.ThreadStatic]
        private static ArrayBufferWriter<byte> _cloneWriter;

        private static ArrayBufferWriter<byte> CloneWriter
        {
            get
            {
                if (_cloneWriter == null)
                    _cloneWriter = new ArrayBufferWriter<byte>(4096);
                return _cloneWriter;
            }
        }

        /// <summary>
        /// Single canonical simulation state that advances monotonically through
        /// the pattern. Every candidate trial reverts this back to _beatSnapshot.
        /// </summary>
        private GameState _working;

        /// <summary>
        /// Snapshot of _working at the start of the current beat, used to revert
        /// between candidate tests within a single beat.
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
        /// ManiaConfig.HitHalfRange as initialized in GameState.Create. A note
        /// at BeatFrame can be hit during [BeatFrame - _noteHitHalfRange,
        /// BeatFrame + _noteHitHalfRange].
        /// </summary>
        private int _noteHitHalfRange;

        /// <summary>
        /// Cache of move reach (max horizontal hitbox extent from attacker origin)
        /// per CharacterState. Reach is config-static so it only needs to be
        /// computed once per run.
        /// </summary>
        private readonly Dictionary<CharacterState, sfloat> _reachCache = new Dictionary<CharacterState, sfloat>();

        /// <summary>
        /// Result of a single candidate trial.
        /// </summary>
        private struct MoveTestResult
        {
            public bool Hit;
            public InputFlags Input;
            public sfloat KnockbackSqr;
            public sfloat Reach;
        }

        /// <summary>
        /// Static shim so existing callers (RhythmComboManager) continue to
        /// work without changes.
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
            _attackerIndex = attackerIndex;
            _attackerConfig = options.Players[attackerIndex].Character;
            _noteHitHalfRange = (int)options.Players[attackerIndex].BeatCancelWindow;

            // Clone Players so we can suppress the attacker's ComboMode on the
            // generator's copy (forcing Freestyle) without leaking the change
            // back to the real game's shared PlayerOptions. Prevents the
            // generator's inner simulation from recursively triggering mania
            // when its own super-hit connects.
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
                // Default off. Toggled to true only on the exact single frame
                // an attacker input is applied (either in TryCandidate's frame
                // 0 or when applying a chosen move in ApplyInputToWorking).
                // Leaving this on across inter-beat empty-input advances lets
                // buffered attack presses retrigger mid-recovery and overwrite
                // crouching variants with standing ones, desyncing the sim
                // from the real Mania-mode game.
                AlwaysRhythmCancel = false,
            };

            // Seed the working state from a clone of the caller's state so we
            // never mutate the real game state.
            _working = null;
            CloneInto(ref _working, state);
            _working.RoundEnd = Frame.Infinity;

            // Mania alignment preamble: use the game's hitstop (which aligns
            // to the next quarter-note beat boundary) so the simulation's
            // hitstop/slow-mo split matches the real game exactly. This
            // ensures the SimFrame count at firstBeatFrame is identical,
            // keeping fighter positions and animation frames in sync.
            Frame firstBeatFrame = noteFrames[0];
            _working.HitstopFramesRemaining = gameHitstop;
            _working.GameMode = GameMode.ManiaStart;
            _working.ModeStart = _working.RealFrame;

            // Stop one frame short of the beat's dispatch frame
            // (noteTick + HitHalfRange) so that the subsequent
            // AdvanceOnce(input) in TryCandidate / ApplyInputToWorking
            // lands the input on the dispatch frame itself — matching
            // the real game's DoManiaStep, which (post-withhold refactor)
            // only emits the HitEvent at the last frame of the hit
            // window. Advancing TO the dispatch frame would consume it
            // with an empty input and push the sim's effective input
            // frame one step ahead of the real game each beat.
            AdvanceWorkingTo(firstBeatFrame + _noteHitHalfRange - 1);

            // Override back to Fighting so candidate trials run at full speed
            // instead of through the ManiaStart slow-mo curve.
            _working.GameMode = GameMode.Fighting;
            _working.SpeedRatio = (sfloat)1f;

            List<GeneratedComboMove> moves = new List<GeneratedComboMove>();
            List<MoveTestResult> candidates = new List<MoveTestResult>();

            // Per-beat GameState snapshots for ComboVerifyDebug — only built
            // when the debug flag is on, so production runs pay no extra cost.
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

            Frame currentBeat = firstBeatFrame;

            for (int i = 0; i < noteFrames.Length; i++)
            {
                currentBeat = noteFrames[i];
                // Stop one frame short of the beat's dispatch frame
                // (currentBeat + HitHalfRange) so candidate and
                // chosen-move AdvanceOnce(input) calls land the input
                // on the dispatch frame — matching the real game's
                // withheld HitEvent fire at RealFrame = currentBeat +
                // HitHalfRange.
                AdvanceWorkingTo(currentBeat + _noteHitHalfRange - 1);

                // Next authored note (if any) so we can reject candidates
                // whose hitstun bleeds into its input window.
                Frame nextBeat = (i + 1 < noteFrames.Length) ? noteFrames[i + 1] : Frame.Infinity;

                // Snapshot state at the frame before the beat so each
                // candidate trial can revert and then apply its input
                // on the beat frame itself.
                SnapshotWorking();

                candidates.Clear();
                foreach (InputFlags attack in AttackInputs)
                {
                    TryCandidate(candidates, attack, nextBeat);
                    TryCandidate(candidates, attack | InputFlags.Down, nextBeat);
                }

                int hashValue = DeterministicHash(state.RealFrame.No, i);
                bool isLastBeat = i == noteFrames.Length - 1;
                int chosenIdx = PickBestCandidate(candidates, hasPrev, prevKb, prevReach, hashValue, isLastBeat);

                if (chosenIdx >= 0)
                {
                    MoveTestResult chosen = candidates[chosenIdx];

                    // Apply the chosen input to _working for real. This is the
                    // single advance past this beat — no duplicate advancing.
                    RestoreWorking();
                    ApplyInputToWorking(chosen.Input);

                    moves.Add(
                        new GeneratedComboMove
                        {
                            Input = chosen.Input,
                            BeatFrame = currentBeat,
                            IsMovement = false,
                        }
                    );

                    CaptureBeatSnapshot(beatSnapshots, currentBeat, nextBeat);

                    hasPrev = true;
                    prevKb = chosen.KnockbackSqr;
                    prevReach = chosen.Reach;
                    continue;
                }

                // No direct attack qualified. Before settling for an
                // unconditional dash, check whether a forward dash or a
                // forward jump would set up a direct attack on the next beat
                // without needing yet another movement. A movement note
                // resets the progression constraint.
                //
                // Preference order:
                //  - If the defender is airborne, try jump first (grounded
                //    moves struggle to convert on an airborne target).
                //  - Otherwise try dash first.

                // Candidate trials left _working advanced up to MAX_TEST_FRAMES
                // past the beat, during which FaceTowards (and, for grab
                // candidates, back-throw) can have flipped the attacker's
                // FacingDir. Restore to the pristine beat snapshot so the
                // fallback's forward-direction and defender-airborne reads
                // reflect the beat itself, not the tail of the last probed
                // attack — otherwise the emitted Dash/Up note can point
                // backwards on a cross-up.
                RestoreWorking();
                InputFlags forwardInput = _working.Fighters[_attackerIndex].ForwardInput;
                InputFlags dashMove = InputFlags.Dash | forwardInput;
                InputFlags jumpMove = InputFlags.Up | forwardInput;

                // Beat after nextBeat, used so the lookahead's candidate
                // attack must also satisfy the hitstop-in-window rule against
                // its own next-note window (i.e., not chain two fallbacks'
                // worth of hitstop into the note after nextBeat).
                Frame beatAfterNext = (i + 2 < noteFrames.Length) ? noteFrames[i + 2] : Frame.Infinity;

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
                    // Neither setup move enables a hit. Fall back to the
                    // preferred movement (jump if defender airborne, else
                    // dash) so the note is at least thematically appropriate.
                    chosenMovement = firstTry;
                }

                RestoreWorking();
                ApplyInputToWorking(chosenMovement);

                moves.Add(
                    new GeneratedComboMove
                    {
                        Input = chosenMovement,
                        BeatFrame = currentBeat,
                        IsMovement = true,
                    }
                );

                CaptureBeatSnapshot(beatSnapshots, currentBeat, nextBeat);

                hasPrev = false;
                prevKb = sfloat.Zero;
                prevReach = sfloat.Zero;
            }

            // ManiaState deactivation frame: trail the last note by the
            // median gap of the authored slice, so the window closes at a
            // musically sensible distance past the finisher. Falls back to
            // 30 frames (half a second at 60 TPS) for single-note slices.
            int trailingPad = 30;
            if (noteFrames.Length >= 2)
            {
                int lastGap = noteFrames[noteFrames.Length - 1] - noteFrames[noteFrames.Length - 2];
                if (lastGap > 0)
                    trailingPad = lastGap;
            }
            // Extra buffer past the last beat so the finisher's hit lands
            // while still in GameMode.Mania — otherwise the hit registers
            // during Fighting and grants super meter from the combo itself.
            trailingPad += 10;
            Frame endFrame = noteFrames[noteFrames.Length - 1] + trailingPad;
            return new GeneratedCombo
            {
                Moves = moves,
                EndFrame = endFrame,
                BeatSnapshots = beatSnapshots,
            };
        }

        /// <summary>
        /// Try a single candidate input. Restores the working state from the
        /// beat snapshot, applies the input, and advances until the move hits
        /// or MAX_TEST_FRAMES is reached. Appends a result to <paramref name="candidates"/>
        /// only if the move connected AND the resulting hitstop does not overlap
        /// the next note's hit window.
        /// </summary>
        private void TryCandidate(List<MoveTestResult> candidates, InputFlags input, Frame nextBeat)
        {
            // Respect per-move opt-out: moves whose HitboxData.ComboEligible is
            // false must never appear in a generated combo.
            CharacterState state = MapInputToState(input);
            HitboxData data = _attackerConfig.GetHitboxData(state);
            if (data == null || !data.ComboEligible)
                return;

            RestoreWorking();

            int defenderIndex = 1 - _attackerIndex;

            bool checkWindow = nextBeat < Frame.Infinity;
            Frame windowStart = checkWindow ? nextBeat - _noteHitHalfRange : Frame.Infinity;
            Frame windowEnd = checkWindow ? nextBeat + _noteHitHalfRange : Frame.Infinity;

            bool hit = false;
            BoxProps hitProps = default;
            bool hitstopInWindow = false;

            // Phase 1: advance up to MAX_TEST_FRAMES looking for the hit.
            // While advancing, also watch for the game being in hitstop inside
            // the next note's window — residual hitstop from a prior move can
            // land in the window even before this candidate connects.
            for (int frame = 0; frame < MAX_TEST_FRAMES; frame++)
            {
                InputFlags attackerFlags = frame == 0 ? input : InputFlags.None;

                // AlwaysRhythmCancel must only be true on the input frame
                // itself. If it stays true on subsequent frames, ApplyActiveState
                // can override the attack with a different variant (e.g.
                // standing overriding crouching once Down is no longer held).
                _options.AlwaysRhythmCancel = frame == 0;

                AdvanceOnce(attackerFlags);

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
                return;

            // Phase 2: the current move connected. Continue advancing through
            // the end of the next note's window, checking whether the fresh
            // hitstop overlaps. Early-exit as soon as we find overlap.
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

            if (hitstopInWindow)
                return;

            candidates.Add(
                new MoveTestResult
                {
                    Hit = true,
                    Input = input,
                    KnockbackSqr = hitProps.Knockback.sqrMagnitude,
                    Reach = GetReach(input),
                }
            );
        }

        /// <summary>
        /// True if _working.RealFrame is inside the inclusive window
        /// [windowStart, windowEnd] and the game is currently in hitstop
        /// (HitstopFramesRemaining &gt; 0).
        /// </summary>
        private bool IsHitstopInWindow(Frame windowStart, Frame windowEnd)
        {
            return _working.RealFrame >= windowStart
                && _working.RealFrame <= windowEnd
                && _working.HitstopFramesRemaining > 0;
        }

        /// <summary>
        /// Lookahead: restore to the current beat snapshot, apply
        /// <paramref name="movement"/> on the beat frame, advance to
        /// <paramref name="nextBeat"/>, then test whether any grounded attack
        /// (standing or crouching, each tier) would land a direct hit from
        /// that post-movement position AND not cause hitstop overlap with
        /// <paramref name="beatAfterNext"/>'s input window. Returns true on
        /// the first attack that passes both checks. Mutates _working; the
        /// caller is expected to RestoreWorking() before committing the
        /// chosen movement for real.
        /// </summary>
        private bool TryMovementLookahead(InputFlags movement, Frame nextBeat, Frame beatAfterNext)
        {
            RestoreWorking();
            ApplyInputToWorking(movement);
            // Stop one frame short of nextBeat's dispatch frame so the
            // lookahead attack's AdvanceOnce(input) in LookaheadAttackHits
            // lands on nextBeat + HitHalfRange — matching the real game's
            // withheld HitEvent dispatch for the next note.
            AdvanceWorkingTo(nextBeat + _noteHitHalfRange - 1);
            CloneInto(ref _lookaheadSnapshot, _working);

            foreach (InputFlags atk in AttackInputs)
            {
                if (LookaheadAttackHits(atk, beatAfterNext))
                    return true;
                if (LookaheadAttackHits(atk | InputFlags.Down, beatAfterNext))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Restore _working from _lookaheadSnapshot, apply <paramref name="input"/>
        /// for one frame with rhythm cancel, and advance up to MAX_TEST_FRAMES
        /// looking for a hit on the defender. Respects ComboEligible and the
        /// hitstop-in-window rule against <paramref name="beatAfterNext"/>'s
        /// input window, matching the main TryCandidate logic one beat
        /// further out. Used exclusively by the movement lookahead path.
        /// </summary>
        private bool LookaheadAttackHits(InputFlags input, Frame beatAfterNext)
        {
            CharacterState state = MapInputToState(input);
            HitboxData data = _attackerConfig.GetHitboxData(state);
            if (data == null || !data.ComboEligible)
                return false;

            CloneInto(ref _working, _lookaheadSnapshot);

            int defenderIndex = 1 - _attackerIndex;

            bool checkWindow = beatAfterNext < Frame.Infinity;
            Frame windowStart = checkWindow ? beatAfterNext - _noteHitHalfRange : Frame.Infinity;
            Frame windowEnd = checkWindow ? beatAfterNext + _noteHitHalfRange : Frame.Infinity;

            bool hit = false;
            bool hitstopInWindow = false;

            // Phase 1: advance up to MAX_TEST_FRAMES looking for the hit.
            // Also watch for hitstop inside the window — residual hitstop
            // from a prior move can land in the window before the trial
            // attack connects.
            for (int frame = 0; frame < MAX_TEST_FRAMES; frame++)
            {
                InputFlags attackerFlags = frame == 0 ? input : InputFlags.None;
                _options.AlwaysRhythmCancel = frame == 0;
                AdvanceOnce(attackerFlags);

                if (checkWindow && IsHitstopInWindow(windowStart, windowEnd))
                    hitstopInWindow = true;

                if (_working.Fighters[defenderIndex].HitProps.HasValue)
                {
                    hit = true;
                    break;
                }
            }
            _options.AlwaysRhythmCancel = false;

            if (!hit)
                return false;

            // Phase 2: continue advancing through the end of the window,
            // checking whether the fresh hitstop overlaps.
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

        /// <summary>
        /// Pick the index of the best candidate.
        ///
        /// Non-last-beat rule:
        ///  - If there is no previous move, all hitting candidates qualify.
        ///  - Otherwise a candidate qualifies only if its knockback strictly
        ///    exceeds prev OR its reach strictly exceeds prev.
        ///  - Among qualifying candidates, smallest knockback wins, with one
        ///    exception: if the smallest-knockback move is a heavy attack,
        ///    every qualifying heavy attack (standing and crouching) enters
        ///    a deterministic random pick instead of selecting by knockback.
        ///
        /// Last-beat rule (the final note in the pattern, no chain to build):
        ///  - Ignore the progression filter entirely: every hitting candidate
        ///    qualifies regardless of previous move.
        ///  - Prefer the LARGEST knockback (finisher semantics).
        ///  - Random-pick among every candidate sharing the best's attack
        ///    tier bit (Light / Medium / Heavy / Special), generalizing the
        ///    heavy-attack randomization to whichever tier wins.
        ///
        /// Ties are broken deterministically via <paramref name="hashValue"/>.
        /// Returns -1 if no candidate qualifies.
        /// </summary>
        private static int PickBestCandidate(
            List<MoveTestResult> pool,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            int hashValue,
            bool isLastBeat
        )
        {
            // First pass: find the best candidate under the active rule.
            // Last beat: highest knockback, no progression filter.
            // Otherwise: lowest knockback, progression filter applies.
            bool any = false;
            sfloat bestKb = sfloat.Zero;
            InputFlags bestInput = InputFlags.None;
            for (int i = 0; i < pool.Count; i++)
            {
                MoveTestResult c = pool[i];
                if (!isLastBeat && hasPrev && !(c.KnockbackSqr > prevKb || c.Reach > prevReach))
                    continue;

                bool better;
                if (!any)
                    better = true;
                else if (isLastBeat)
                    better = c.KnockbackSqr > bestKb;
                else
                    better = c.KnockbackSqr < bestKb;

                if (better)
                {
                    bestKb = c.KnockbackSqr;
                    bestInput = c.Input;
                    any = true;
                }
            }
            if (!any)
                return -1;

            bool bestIsHeavy = (bestInput & InputFlags.HeavyAttack) != 0;
            InputFlags bestTier = GetAttackTierBit(bestInput);

            // Second pass: count and pick among eligible candidates.
            int tieCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (IsEligibleForPick(pool[i], hasPrev, prevKb, prevReach, bestKb, bestIsHeavy, isLastBeat, bestTier))
                    tieCount++;
            }
            if (tieCount == 0)
                return -1;

            int pick = hashValue % tieCount;
            int seen = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (!IsEligibleForPick(pool[i], hasPrev, prevKb, prevReach, bestKb, bestIsHeavy, isLastBeat, bestTier))
                    continue;
                if (seen == pick)
                    return i;
                seen++;
            }
            return -1;
        }

        private static bool IsEligibleForPick(
            MoveTestResult c,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            sfloat bestKb,
            bool bestIsHeavy,
            bool isLastBeat,
            InputFlags bestTier
        )
        {
            if (isLastBeat)
            {
                // Progression filter disabled for the finisher. Eligible set
                // is every hitting candidate sharing the winner's tier bit.
                return (c.Input & bestTier) != 0;
            }
            if (hasPrev && !(c.KnockbackSqr > prevKb || c.Reach > prevReach))
                return false;
            if (bestIsHeavy)
                return (c.Input & InputFlags.HeavyAttack) != 0;
            return c.KnockbackSqr == bestKb;
        }

        /// <summary>
        /// Returns the tier bit (Light/Medium/Heavy/Special) carried by an
        /// attack input, or <see cref="InputFlags.None"/> if none is present.
        /// Candidates are always generated from a single tier bit OR'd with
        /// an optional <see cref="InputFlags.Down"/> modifier, so exactly one
        /// tier bit is set in practice.
        /// </summary>
        private static InputFlags GetAttackTierBit(InputFlags input)
        {
            if ((input & InputFlags.SpecialAttack) != 0)
                return InputFlags.SpecialAttack;
            if ((input & InputFlags.HeavyAttack) != 0)
                return InputFlags.HeavyAttack;
            if ((input & InputFlags.MediumAttack) != 0)
                return InputFlags.MediumAttack;
            if ((input & InputFlags.LightAttack) != 0)
                return InputFlags.LightAttack;
            return InputFlags.None;
        }

        /// <summary>
        /// Maximum horizontal hitbox/grabbox extent from attacker origin for the
        /// given attack input, across every frame of the move. Uses attacker-
        /// local coordinates (not mirrored by facing) since BoxData.CenterLocal
        /// is stored facing-agnostic — see FighterState.AddBoxes where the X
        /// mirror is applied at read time.
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
        /// Map an attack InputFlags (with optional Down modifier) to the
        /// corresponding grounded CharacterState. Mirrors the Standing/Crouching
        /// rows of FighterState._attackDictionary.
        /// </summary>
        private static CharacterState MapInputToState(InputFlags input)
        {
            bool crouching = (input & InputFlags.Down) != 0;
            if ((input & InputFlags.LightAttack) != 0)
                return crouching ? CharacterState.LightCrouching : CharacterState.LightAttack;
            if ((input & InputFlags.MediumAttack) != 0)
                return crouching ? CharacterState.MediumCrouching : CharacterState.MediumAttack;
            if ((input & InputFlags.HeavyAttack) != 0)
                return crouching ? CharacterState.HeavyCrouching : CharacterState.HeavyAttack;
            if ((input & InputFlags.SpecialAttack) != 0)
                return crouching ? CharacterState.SpecialCrouching : CharacterState.SpecialAttack;
            return CharacterState.Idle;
        }

        /// <summary>
        /// Deterministic hash for tie-breaking, derived from game state so
        /// rollback produces identical selections.
        /// </summary>
        private static int DeterministicHash(int realFrame, int beatIndex)
        {
            unchecked
            {
                int h = realFrame * 31 + beatIndex;
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
        /// Advance <c>_working</c> from <paramref name="currentBeat"/> (where
        /// the chosen input was just applied) to the last frame of this note's
        /// hit window (<paramref name="currentBeat"/> + <c>HitHalfRange</c>),
        /// clamped to <paramref name="nextBeat"/> - 1 so the next iteration's
        /// <see cref="AdvanceWorkingTo"/> still has forward progress. Deep-clones
        /// the resulting <c>_working</c> and appends it to
        /// <paramref name="snapshots"/>. No-op when the caller passed a null
        /// list (verify flag off).
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
        /// <paramref name="dst"/>. Uses a thread-static ArrayBufferWriter to
        /// avoid per-call allocations.
        /// </summary>
        private static void CloneInto(ref GameState dst, GameState src)
        {
            CloneWriter.Clear();
            MemoryPackSerializer.Serialize(CloneWriter, src);
            dst = MemoryPackSerializer.Deserialize<GameState>(CloneWriter.WrittenSpan.ToArray());
        }

        /// <summary>
        /// Advance _working forward with empty inputs until its RealFrame
        /// reaches <paramref name="targetRealFrame"/>. GameState.Advance
        /// increments RealFrame at the top of its call, so the last
        /// AdvanceOnce here processes frame <paramref name="targetRealFrame"/>
        /// itself with empty input. Callers that want a subsequent
        /// AdvanceOnce(input) to land the input on beat frame F should
        /// pass F - 1 here, so the input frame matches where the real
        /// game's DoManiaStep dispatches the note's HitInput at
        /// RealFrame = F.
        /// </summary>
        private void AdvanceWorkingTo(Frame targetRealFrame)
        {
            while (_working.RealFrame < targetRealFrame)
            {
                AdvanceOnce(InputFlags.None);
            }
        }

        /// <summary>
        /// Apply a single attacker input for one frame on _working. Used to
        /// consume a chosen move permanently (not to test). Rhythm cancel is
        /// enabled for exactly this one frame and disabled afterward so the
        /// subsequent inter-beat empty-input catch-up cannot retrigger the
        /// attack from the buffered press.
        /// </summary>
        private void ApplyInputToWorking(InputFlags input)
        {
            _options.AlwaysRhythmCancel = true;
            AdvanceOnce(input);
            _options.AlwaysRhythmCancel = false;
        }

        /// <summary>
        /// Advance _working by exactly one frame with the given attacker input.
        /// </summary>
        private void AdvanceOnce(InputFlags attackerInput)
        {
            (GameInput input, InputStatus status)[] inputs;
            if (_attackerIndex == 0)
            {
                inputs = new (GameInput, InputStatus)[]
                {
                    (new GameInput(attackerInput), InputStatus.Confirmed),
                    (GameInput.None, InputStatus.Confirmed),
                };
            }
            else
            {
                inputs = new (GameInput, InputStatus)[]
                {
                    (GameInput.None, InputStatus.Confirmed),
                    (new GameInput(attackerInput), InputStatus.Confirmed),
                };
            }
            _working.Advance(_options, inputs);
        }
    }
}
