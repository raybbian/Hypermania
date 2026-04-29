using System;
using System.Buffers;
using System.Collections.Generic;
using Game.Sim.Configs;
using MemoryPack;
using Netcode.Rollback;
using Utils;
using Utils.SoftFloat;
using Game.Sim;

namespace Game.Sim
{
    public enum ComboMoveKind
    {
        Attack,
        Movement,
        NoOp,
    }

    public struct GeneratedComboMove
    {
        public InputFlags Input;
        public Frame BeatFrame;
        public ComboMoveKind Kind;
        public int Channel;
    }

    public struct ComboBeatSnapshot
    {
        public Frame CompareFrame;
        public GameState Predicted;
    }

    public struct GeneratedCombo
    {
        public List<GeneratedComboMove> Moves;
        public Frame EndFrame;

        // Only filled in when InfoOptions.VerifyComboPrediction is on.
        public List<ComboBeatSnapshot> BeatSnapshots;
    }

    // Things this generator depends on:
    //
    // Dispatch alignment: stop one frame short of dispatch, then apply the
    // attacker input with a single AdvanceOnce. If you advance all the way
    // to the dispatch frame first, it gets consumed with empty input and
    // the sim desyncs by one. See ApplyInputAtBeat.
    //
    // AlwaysRhythmCancel is only on for the frame the attacker input is
    // applied. Leaving it on across empty-input advances lets a buffered
    // press retrigger mid-recovery, and then the prediction won't match
    // the real Mania game.
    //
    // _working advances monotonically. Per-beat trials revert to
    // _beatSnapshot. The movement lookahead has its own _lookaheadSnapshot
    // so nested trials there don't clobber the per-beat snapshot.
    public class ComboGenerator
    {
        private const int MAX_TEST_FRAMES = 40;

        // Keeps GameMode == Mania for a few frames after the last beat,
        // otherwise the finisher's hit lands in Fighting mode and the combo
        // grants super meter to itself.
        private const int POST_FINISHER_BUFFER = 15;

        private const InputFlags AttackTierMask =
            InputFlags.LightAttack | InputFlags.MediumAttack | InputFlags.HeavyAttack | InputFlags.SpecialAttack;

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

        // Lets the strict and relaxed passes within one beat draw
        // independent picks instead of landing on the same index.
        private enum HashSalt
        {
            Strict = 0,
            Relaxed = 1,
        }

        private readonly ArrayBufferWriter<byte> _cloneWriter = new ArrayBufferWriter<byte>(4096);

        private GameState _working;
        private GameState _beatSnapshot;
        private GameState _lookaheadSnapshot;

        private SimOptions _options;
        private int _attackerIndex;
        private CharacterStats _attackerConfig;
        private int _noteHitHalfRange;
        private AudioStats _audio;
        private (GameInput input, InputStatus status)[] _inputScratch;

        // Reach is config-static, so we only need to compute it once per
        // CharacterState per run.
        private readonly Dictionary<CharacterState, sfloat> _reachCache = new Dictionary<CharacterState, sfloat>();

        private struct MoveTestResult
        {
            public InputFlags Input;
            public sfloat KnockbackSqr;
            public sfloat Reach;
        }

        public static GeneratedCombo Generate(
            in GameState state,
            SimOptions options,
            int attackerIndex,
            BeatmapNote[] notes,
            int gameHitstop
        )
        {
            ComboGenerator gen = new ComboGenerator();
            return gen.Run(state, options, attackerIndex, notes, gameHitstop);
        }

        public GeneratedCombo Run(
            in GameState state,
            SimOptions options,
            int attackerIndex,
            BeatmapNote[] notes,
            int gameHitstop
        )
        {
            if (notes == null || notes.Length == 0)
            {
                return new GeneratedCombo { Moves = new List<GeneratedComboMove>(), EndFrame = state.RealFrame };
            }

            // When multiple notes share a tick, only the first runs through
            // the full attack/movement pipeline; the extras come back as
            // NoOps appended at the end. If we let every duplicate iterate
            // normally they'd snapshot the post-attack state, fail every
            // strict candidate (attacker is mid-animation), and fall through
            // to dashes that do no damage.
            List<BeatmapNote> primaryNotes = new List<BeatmapNote>(notes.Length);
            List<BeatmapNote> extraNoOps = new List<BeatmapNote>();
            for (int i = 0; i < notes.Length; i++)
            {
                if (i > 0 && notes[i].Tick == notes[i - 1].Tick)
                    extraNoOps.Add(notes[i]);
                else
                    primaryNotes.Add(notes[i]);
            }

            InitializeFromCaller(options, attackerIndex);
            SeedWorkingState(state, gameHitstop, primaryNotes[0].Tick);

            List<GeneratedComboMove> moves = new List<GeneratedComboMove>();
            List<MoveTestResult> candidates = new List<MoveTestResult>();
            List<ComboBeatSnapshot> beatSnapshots =
                options.InfoOptions != null && options.InfoOptions.VerifyComboPrediction
                    ? new List<ComboBeatSnapshot>()
                    : null;

            // Each move after the first has to strictly beat the previous
            // one on knockback or reach. The movement fallback wipes this.
            bool hasPrev = false;
            sfloat prevKb = sfloat.Zero;
            sfloat prevReach = sfloat.Zero;

            for (int i = 0; i < primaryNotes.Count; i++)
            {
                Frame currentBeat = primaryNotes[i].Tick;
                int currentChannel = primaryNotes[i].Channel;
                Frame nextBeat = (i + 1 < primaryNotes.Count) ? primaryNotes[i + 1].Tick : Frame.Infinity;
                bool isLastBeat = i == primaryNotes.Count - 1;

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
                        currentChannel,
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

                bool canTryNoOp =
                    i + 2 < primaryNotes.Count && _audio.IsOnBeat(currentBeat, _options.Global.PreGameDelayTicks);
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
                        currentChannel,
                        nextBeat,
                        primaryNotes[i + 1].Channel,
                        primaryNotes[i + 2].Tick,
                        moves,
                        beatSnapshots,
                        out MoveTestResult relaxed
                    )
                )
                {
                    hasPrev = true;
                    prevKb = relaxed.KnockbackSqr;
                    prevReach = relaxed.Reach;
                    i++;
                    continue;
                }

                Frame beatAfterNext = (i + 2 < primaryNotes.Count) ? primaryNotes[i + 2].Tick : Frame.Infinity;
                CommitMovementFallback(currentBeat, currentChannel, nextBeat, beatAfterNext, moves, beatSnapshots);
                hasPrev = false;
                prevKb = sfloat.Zero;
                prevReach = sfloat.Zero;
            }

            // Each channel's deque has to stay Tick-ordered or ManiaState.Tick
            // will auto-miss earlier notes that ended up queued behind later
            // ones. The on-beat pair logic can emit a later-frame NoOp on a
            // channel before an appended duplicate NoOp on that same channel,
            // so we sort once at the end.
            for (int k = 0; k < extraNoOps.Count; k++)
            {
                moves.Add(
                    new GeneratedComboMove
                    {
                        Input = InputFlags.None,
                        BeatFrame = extraNoOps[k].Tick,
                        Kind = ComboMoveKind.NoOp,
                        Channel = extraNoOps[k].Channel,
                    }
                );
            }
            moves.Sort((a, b) => a.BeatFrame.No.CompareTo(b.BeatFrame.No));

            return new GeneratedCombo
            {
                Moves = moves,
                EndFrame = primaryNotes[primaryNotes.Count - 1].Tick + POST_FINISHER_BUFFER,
                BeatSnapshots = beatSnapshots,
            };
        }

        private void InitializeFromCaller(SimOptions options, int attackerIndex)
        {
            _attackerIndex = attackerIndex;
            _attackerConfig = options.Players[attackerIndex].Character;
            _noteHitHalfRange = 5;
            _audio = options.Global.Audio;

            // Clone Players so we can flip the attacker to Freestyle on our
            // copy without touching the real game's PlayerSimOptions. Without
            // this, the inner sim would recursively trigger mania every time
            // its super hit landed.
            PlayerSimOptions[] clonedPlayers = new PlayerSimOptions[options.Players.Length];
            for (int p = 0; p < options.Players.Length; p++)
            {
                clonedPlayers[p] = options.Players[p];
            }
            PlayerSimOptions atk = options.Players[attackerIndex];
            clonedPlayers[attackerIndex] = new PlayerSimOptions
            {
                HealOnActionable = atk.HealOnActionable,
                SuperMaxOnActionable = atk.SuperMaxOnActionable,
                BurstMaxOnActionable = atk.BurstMaxOnActionable,
                Immortal = atk.Immortal,
                Character = atk.Character,
                ComboMode = ComboMode.Freestyle,
                ManiaDifficulty = atk.ManiaDifficulty,
                SuperInputMode = atk.SuperInputMode,
            };

            _options = new SimOptions
            {
                Global = options.Global,
                Players = clonedPlayers,
                InfoOptions = options.InfoOptions,
                AlwaysRhythmCancel = false,
            };

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

            // Pull in the live hitstop, which is already snapped to the next
            // quarter-note boundary, so our hitstop and slow-mo split match
            // the real game.
            _working.HitstopFramesRemaining = gameHitstop;
            _working.GameMode = GameMode.ManiaStart;
            _working.ModeStart = _working.RealFrame;

            AdvanceToDispatchOf(firstBeatFrame);

            // Flip back to Fighting so trials run at full speed instead of
            // through the ManiaStart slow-mo curve.
            _working.GameMode = GameMode.Fighting;
            _working.SpeedRatio = (sfloat)1f;
        }

        private bool TryStrictAttack(
            in GameState state,
            List<MoveTestResult> candidates,
            int beatIndex,
            bool isLastBeat,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            Frame currentBeat,
            int currentChannel,
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
            CommitFromBeatSnapshot(
                chosen.Input,
                currentBeat,
                currentChannel,
                nextBeat,
                ComboMoveKind.Attack,
                moves,
                beatSnapshots
            );
            return true;
        }

        private bool TryOnBeatNoOpPair(
            in GameState state,
            List<MoveTestResult> candidates,
            int beatIndex,
            bool hasPrev,
            sfloat prevKb,
            sfloat prevReach,
            Frame currentBeat,
            int currentChannel,
            Frame nextBeat,
            int nextChannel,
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
            CommitFromBeatSnapshot(
                chosen.Input,
                currentBeat,
                currentChannel,
                nextBeat,
                ComboMoveKind.Attack,
                moves,
                beatSnapshots
            );
            CommitNoOpBeat(nextBeat, nextChannel, beatAfterNoop, moves, beatSnapshots);
            return true;
        }

        private void CommitMovementFallback(
            Frame currentBeat,
            int currentChannel,
            Frame nextBeat,
            Frame beatAfterNext,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots
        )
        {
            // Restore before reading ForwardInput / Location. Probing trials
            // can flip facing (FaceTowards, back-throw), and we don't want
            // the emitted Dash or Up to inherit that flip and end up pointing
            // backwards on a cross-up.
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

            CommitFromBeatSnapshot(
                chosenMovement,
                currentBeat,
                currentChannel,
                nextBeat,
                ComboMoveKind.Movement,
                moves,
                beatSnapshots
            );
        }

        private void CommitFromBeatSnapshot(
            InputFlags input,
            Frame currentBeat,
            int currentChannel,
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
                    Channel = currentChannel,
                }
            );
            CaptureBeatSnapshot(beatSnapshots, currentBeat, nextBeat);
        }

        private void CommitNoOpBeat(
            Frame noOpBeat,
            int noOpChannel,
            Frame beatAfterNoop,
            List<GeneratedComboMove> moves,
            List<ComboBeatSnapshot> beatSnapshots
        )
        {
            ApplyInputAtBeat(noOpBeat, InputFlags.None);
            // DoManiaStep calls this on every Input event, so we need the
            // same call here or the verify snapshot won't match byte-for-byte.
            _working.Fighters[_attackerIndex].RegisterManiaNoOp();
            moves.Add(
                new GeneratedComboMove
                {
                    Input = InputFlags.None,
                    BeatFrame = noOpBeat,
                    Kind = ComboMoveKind.NoOp,
                    Channel = noOpChannel,
                }
            );
            CaptureBeatSnapshot(beatSnapshots, noOpBeat, beatAfterNoop);
        }

        // Probes whether `input`, applied from `snapshotSource`, lands a hit
        // within MAX_TEST_FRAMES without any hitstop bleeding into
        // `windowBeat`'s hit window.
        private bool TryHit(ref GameState snapshotSource, InputFlags input, Frame windowBeat, out BoxProps hitProps)
        {
            hitProps = default;

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

            // Phase 1: step forward looking for the hit. We also watch for
            // hitstop landing in the window here, since residual hitstop from
            // an earlier move can show up before this candidate even connects.
            for (int frame = 0; frame < MAX_TEST_FRAMES; frame++)
            {
                InputFlags flags = frame == 0 ? input : InputFlags.None;
                _options.AlwaysRhythmCancel = frame == 0;
                AdvanceOnce(flags);

                if (checkWindow && IsHitstopInWindow(windowStart, windowEnd))
                    hitstopInWindow = true;

                if (_working.Fighters[defenderIndex].View.HitProps.HasValue)
                {
                    hit = true;
                    hitProps = _working.Fighters[defenderIndex].View.HitProps.Value;
                    break;
                }
            }
            _options.AlwaysRhythmCancel = false;

            if (!hit)
                return false;

            // Phase 2: keep stepping through the window to catch any fresh
            // hitstop from the move we just landed.
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

        private bool TryHitFromLookaheadSnapshot(InputFlags input, Frame windowBeat)
        {
            return TryHit(ref _lookaheadSnapshot, input, windowBeat, out _);
        }

        // Apply the movement on the beat, advance to nextBeat's dispatch,
        // and see if any grounded attack would land without bleeding hitstop
        // into beatAfterNext's window. Mutates _working, so the caller has
        // to RestoreWorking before committing anything.
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

        // Finisher pick: take the largest knockback, then random-pick among
        // any candidates sharing the winner's tier bit. No progression
        // filter here.
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

        // Chain-link pick: candidates have to strictly beat the previous
        // move on knockback or reach. Take the smallest qualifying knockback.
        // If the winner happens to be a heavy, random-pick among the
        // qualifying heavies; otherwise random-pick among the candidates
        // tied on knockback.
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

        private static InputFlags GetAttackTierBit(InputFlags input) => input & AttackTierMask;

        // Keep this in sync with the Standing/Crouching rows of
        // FighterState._attackDictionary.
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

        // BoxData.CenterLocal is stored facing-agnostic, so we compute reach
        // in attacker-local coordinates and never mirror it. The X mirror
        // happens at read time in FighterState.AddBoxes.
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

        private void SnapshotWorking()
        {
            CloneInto(ref _beatSnapshot, _working);
        }

        private void RestoreWorking()
        {
            CloneInto(ref _working, _beatSnapshot);
        }

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

        private void CloneInto(ref GameState dst, GameState src)
        {
            _cloneWriter.Clear();
            MemoryPackSerializer.Serialize(_cloneWriter, src);
            dst = MemoryPackSerializer.Deserialize<GameState>(_cloneWriter.WrittenSpan);
        }

        private void AdvanceWorkingTo(Frame targetRealFrame)
        {
            while (_working.RealFrame < targetRealFrame)
            {
                AdvanceOnce(InputFlags.None);
            }
        }

        // Stops one frame before dispatch, so the next AdvanceOnce(input)
        // lands the input on the dispatch frame itself. See class invariants.
        private void AdvanceToDispatchOf(Frame beat)
        {
            AdvanceWorkingTo(beat + _noteHitHalfRange - 1);
        }

        // Assumes _working is sitting at (dispatch frame - 1). Rhythm cancel
        // is on for exactly this single frame. See class invariants.
        private void ApplyInputToWorking(InputFlags input)
        {
            _options.AlwaysRhythmCancel = true;
            AdvanceOnce(input);
            _options.AlwaysRhythmCancel = false;
        }

        private void ApplyInputAtBeat(Frame beat, InputFlags input)
        {
            AdvanceToDispatchOf(beat);
            ApplyInputToWorking(input);
        }

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
