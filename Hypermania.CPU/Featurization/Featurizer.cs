using System;
using Hypermania.CPU.Training;
using Hypermania.Game;
using Hypermania.Shared;
using Hypermania.Shared.SoftFloat;

namespace Hypermania.CPU.Featurization
{
    // Hand-built observation encoder. An earlier reflection-driven version
    // silently encoded every array, every sfloat (recursed to private uint
    // rawValue), and every generic type as 0 - the policy was effectively
    // blind to all per-fighter and per-projectile state. This encoder writes
    // an explicit, normalized, world-frame observation.
    //
    // Layout (lengths in parens):
    //   global      (GlobalFeatures)
    //   self        (FighterFeatures)        - learner-perspective fighter
    //   opp         (FighterFeatures)        - the other fighter
    //   relative    (3)                      - opp - self displacement, distance
    //   projectiles (MaxProjectiles * ProjectileFeaturesPerSlot)
    //   history     (ActionHistoryFrames * (NumDirections + NumButtons))
    //
    // Sim-only: nothing in this encoder may read FighterState.View or any
    // *LastRealFrame property. Those are transient view-layer notifiers
    // (cleared every Advance) and are explicitly NonObservable in the sim;
    // the headless training env doesn't drive a view, so they'd be junk or
    // empty most of the time. State-derived signals (CharacterState one-hot,
    // sim flags) are the source of truth.
    //
    // The history block holds the learner's own last N actions (oldest first,
    // one-hot per categorical head). It closes the information gap that
    // delayed observations would otherwise leave: at decision time t the
    // policy sees state(t-latency) plus the actions it took in
    // [t-latency, t-1], which is the standard delayed-MDP augmentation.
    //
    // Self comes first in the layout (regardless of slot index) so the policy
    // sees a consistent self/opp ordering. X-axis values are NOT mirrored:
    // blocking is defined by InputFlags.Left/Right matching FacingDir's
    // BackwardInput, and FacingDir flips on crossup, so any slot-based mirror
    // is wrong as soon as fighters cross. Action emit stays world-frame; the
    // policy learns left/right behavior directly from world coords.
    public static class Featurizer
    {
        const int NumCharacterStates = 40; // CharacterState max value (GetUp = 39) + 1
        const int MaxProjectiles = GameState.MAX_PROJECTILES;

        // Global block: hype, hitstop, sinceRoundStart, untilRoundEnd. The
        // 6-way GameMode one-hot is gone (only Fighting frames feed the
        // policy), as are SpeedRatio and the PendingRhythmComboAttacker bits
        // (rhythm-combo mode is unused in the freestyle training config).
        const int GlobalFeatures = 4;

        // Per-fighter scalars in WriteFighter (count must equal the number of
        // dst[idx++] = ... lines preceding the one-hot loop).
        const int FighterScalars = 23;
        const int FighterFeatures = FighterScalars + NumCharacterStates;
        const int RelFeatures = 3;
        const int ProjectileFeaturesPerSlot = 7;
        const int ProjectileFeatures = MaxProjectiles * ProjectileFeaturesPerSlot;

        // Past-action lookback. Should track the policy's reaction-latency
        // setting; if the two diverge the policy still sees real actions but
        // the t-1 entry may be stale by `latency - history` frames.
        public const int ActionHistoryFrames = 10;
        const int HistoryFeaturesPerStep = ActionSpace.NumDirections + ActionSpace.NumButtons;
        const int HistoryFeatures = ActionHistoryFrames * HistoryFeaturesPerStep;

        // Scalar offset of f.Actionable within a fighter block. Update if
        // WriteFighter's scalar order changes. The policy uses this to gate
        // logits when the learner can't legally act.
        const int FighterActionableScalarOffset = 11;

        const int StateFeatures =
            GlobalFeatures + 2 * FighterFeatures + RelFeatures + ProjectileFeatures;

        // Absolute offset of the perspective's own Actionable bit in the
        // full obs vector. The self block sits right after the global block,
        // so the same column works for both learner and opponent (each sees
        // themselves in "self").
        public static int OwnActionableObsOffset =>
            GlobalFeatures + FighterActionableScalarOffset;

        // Absolute offset of a specific CharacterState's one-hot bit in the
        // perspective's own fighter block. The one-hot follows the scalar
        // prefix; bit index k corresponds to CharacterState value k. Used by
        // the policy to read state-derived signals (e.g. blockstun bits)
        // straight off the obs without re-deriving them from FighterState.
        public static int OwnStateObsOffset(CharacterState s) =>
            GlobalFeatures + FighterScalars + (int)s;

        public static int Length => _length;
        public static int StateLength => StateFeatures;
        public static int HistoryLength => HistoryFeatures;
        public static ulong SchemaHash => _schemaHash;

        static readonly int _length = StateFeatures + HistoryFeatures;

        // FNV-1a over a stable layout descriptor. Bump LayoutVersion (the
        // leading "vN") whenever the feature list or normalization changes
        // semantics - the existing snapshot loader rejects mismatched hashes.
        // v3: dropped slot-based x-axis mirror, observation is world-frame.
        // v4: added action-history block (own last N actions, one-hot).
        // v5: removed view-layer flags (BlockedLastRealFrame, HitLastRealFrame,
        //     DashedLastRealFrame); state info now comes from the
        //     CharacterState one-hot only.
        // v6: dropped GameMode one-hot, SpeedRatio, PendingRhythmComboAttacker,
        //     and the rhythm-combo / util fighter flags (IsSuperAttack,
        //     AttackConnected, LockedHitstun, RhythmComboFinisherActive,
        //     RhythmComboTier2, NoOpBonus, NoOpBonusRemaining,
        //     SuperComboBeats). Added per-fighter prevHighBlocking and
        //     prevLowBlocking flags derived from the previous-frame action
        //     and the fighter's BackwardInput.
        static readonly ulong _schemaHash = Fnv1a64(
            $"v6;global={GlobalFeatures};fighter={FighterFeatures};"
                + $"rel={RelFeatures};proj={MaxProjectiles}x{ProjectileFeaturesPerSlot};"
                + $"states={NumCharacterStates};"
                + $"history={ActionHistoryFrames}x{HistoryFeaturesPerStep}"
        );

        // Write the state portion of both perspectives in one pass. Caller
        // is responsible for appending the history block (WriteHistory) at
        // policy-input time; baking history into the per-frame state ring
        // would freeze stale actions into delayed observations.
        //
        // prevDir0 / prevDir1 are the action-space direction indices each
        // fighter committed on the previous Fighting frame, or any negative
        // value (e.g. -1) if there is no prior frame. These drive the per-
        // fighter prevHighBlocking / prevLowBlocking flags inside WriteFighter.
        public static void EncodePair(
            in GameState state,
            SimOptions options,
            int learnerIdx,
            int prevDir0,
            int prevDir1,
            Span<float> dstLearner,
            Span<float> dstOpp
        )
        {
            if (dstLearner.Length < StateFeatures)
                throw new ArgumentException(
                    $"dstLearner too short: need {StateFeatures}, got {dstLearner.Length}",
                    nameof(dstLearner)
                );
            if (dstOpp.Length < StateFeatures)
                throw new ArgumentException(
                    $"dstOpp too short: need {StateFeatures}, got {dstOpp.Length}",
                    nameof(dstOpp)
                );
            if ((uint)learnerIdx >= 2)
                throw new ArgumentOutOfRangeException(nameof(learnerIdx));

            int oppIdx = 1 - learnerIdx;

            float wallsX = SafeScale((float)options.Global.WallsX, 10f);
            float groundY = (float)options.Global.GroundY;
            float maxHype = SafeScale((float)options.Global.MaxHype, 100f);
            float superMax = SafeScale((float)options.Global.SuperMax, 100f);
            float roundTicks = SafeScale(options.Global.RoundTimeTicks, 3600f);

            Span<float> hpMax = stackalloc float[2];
            Span<float> burstMax = stackalloc float[2];
            Span<float> fwdSpeed = stackalloc float[2];
            Span<float> airDashMax = stackalloc float[2];
            for (int f = 0; f < 2; f++)
            {
                var ch = options.Players[f].Character;
                hpMax[f] = SafeScale((float)(ch?.Health ?? (sfloat)100f), 100f);
                burstMax[f] = SafeScale((float)(ch?.BurstMax ?? (sfloat)100f), 100f);
                fwdSpeed[f] = SafeScale((float)(ch?.ForwardSpeed ?? (sfloat)10f), 10f);
                airDashMax[f] = MathF.Max(1f, ch?.NumAirDashes ?? 1);
            }

            // Stage each fighter's block once. The block bytes only depend on
            // the fighter and its own norms, not on which perspective reads it.
            Span<float> fighter0Block = stackalloc float[FighterFeatures];
            Span<float> fighter1Block = stackalloc float[FighterFeatures];
            int sidx = 0;
            WriteFighter(
                state.Fighters[0], state.SimFrame, wallsX, fwdSpeed[0], hpMax[0],
                burstMax[0], superMax, airDashMax[0], prevDir0,
                fighter0Block, ref sidx
            );
            sidx = 0;
            WriteFighter(
                state.Fighters[1], state.SimFrame, wallsX, fwdSpeed[1], hpMax[1],
                burstMax[1], superMax, airDashMax[1], prevDir1,
                fighter1Block, ref sidx
            );

            const int SelfOff = GlobalFeatures;
            const int OppOff = GlobalFeatures + FighterFeatures;
            const int RelOff = GlobalFeatures + 2 * FighterFeatures;
            const int ProjOff = RelOff + RelFeatures;

            // Global block: identical for both perspectives (no perspective-
            // dependent fields after the rhythm-combo cleanup).
            int gi = 0;
            float hypeF = (float)state.HypeMeter / maxHype;
            dstLearner[gi] = hypeF; dstOpp[gi] = hypeF; gi++;
            float hitstopF = state.HitstopFramesRemaining / 60f;
            dstLearner[gi] = hitstopF; dstOpp[gi] = hitstopF; gi++;
            float sinceF = Clamp(
                (state.SimFrame.No - state.RoundStart.No) / roundTicks, -1f, 2f
            );
            dstLearner[gi] = sinceF; dstOpp[gi] = sinceF; gi++;
            float untilF = Clamp(
                (state.RoundEnd.No - state.SimFrame.No) / roundTicks, -1f, 2f
            );
            dstLearner[gi] = untilF; dstOpp[gi] = untilF; gi++;

            // Fighter blocks: fighter[learnerIdx] -> dstLearner.self + dstOpp.opp.
            //                 fighter[oppIdx]    -> dstLearner.opp + dstOpp.self.
            Span<float> learnerBlock = learnerIdx == 0 ? fighter0Block : fighter1Block;
            Span<float> opponentBlock = learnerIdx == 0 ? fighter1Block : fighter0Block;
            learnerBlock.CopyTo(dstLearner.Slice(SelfOff, FighterFeatures));
            opponentBlock.CopyTo(dstLearner.Slice(OppOff, FighterFeatures));
            opponentBlock.CopyTo(dstOpp.Slice(SelfOff, FighterFeatures));
            learnerBlock.CopyTo(dstOpp.Slice(OppOff, FighterFeatures));

            // Relative block. Learner sees opp - self; opp sees self - opp.
            float learnerX = (float)state.Fighters[learnerIdx].Position.x;
            float learnerY = (float)state.Fighters[learnerIdx].Position.y;
            float opponentX = (float)state.Fighters[oppIdx].Position.x;
            float opponentY = (float)state.Fighters[oppIdx].Position.y;
            float dx = (opponentX - learnerX) / wallsX;
            float dy = (opponentY - learnerY) / 4f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            dstLearner[RelOff + 0] = dx;
            dstLearner[RelOff + 1] = dy;
            dstLearner[RelOff + 2] = dist;
            dstOpp[RelOff + 0] = -dx;
            dstOpp[RelOff + 1] = -dy;
            dstOpp[RelOff + 2] = dist;

            // Projectile slots. pdy / IsDying / Active are perspective-invariant;
            // pdx / pvx / pvy / Owner-flag flip via selfX / selfFwdSpeed / idx.
            int projCount = state.Projectiles?.Length ?? 0;
            float learnerFwd = fwdSpeed[learnerIdx];
            float oppFwd = fwdSpeed[oppIdx];
            int po = ProjOff;
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (i < projCount && state.Projectiles[i].Active)
                {
                    ProjectileState p = state.Projectiles[i];
                    float px = (float)p.Position.x;
                    float py = (float)p.Position.y;
                    float pvx = (float)p.Velocity.x;
                    float pvy = (float)p.Velocity.y;
                    float pdy = (py - groundY) / 4f;
                    float isDyingF = p.IsDying ? 1f : 0f;

                    dstLearner[po + 0] = 1f;
                    dstLearner[po + 1] = (px - learnerX) / wallsX;
                    dstLearner[po + 2] = pdy;
                    dstLearner[po + 3] = pvx / learnerFwd;
                    dstLearner[po + 4] = pvy / learnerFwd;
                    dstLearner[po + 5] = isDyingF;
                    dstLearner[po + 6] = p.Owner == learnerIdx ? 1f : 0f;

                    dstOpp[po + 0] = 1f;
                    dstOpp[po + 1] = (px - opponentX) / wallsX;
                    dstOpp[po + 2] = pdy;
                    dstOpp[po + 3] = pvx / oppFwd;
                    dstOpp[po + 4] = pvy / oppFwd;
                    dstOpp[po + 5] = isDyingF;
                    dstOpp[po + 6] = p.Owner == oppIdx ? 1f : 0f;
                }
                else
                {
                    for (int k = 0; k < ProjectileFeaturesPerSlot; k++)
                    {
                        dstLearner[po + k] = 0f;
                        dstOpp[po + k] = 0f;
                    }
                }
                po += ProjectileFeaturesPerSlot;
            }
        }

        // Single-perspective state encode. Writes StateFeatures floats; caller
        // appends history with WriteHistory if the full obs vector is needed.
        // prevDir0 / prevDir1 are slot-indexed previous-frame action-space
        // direction indices (or any negative value for "no prior frame").
        public static void Encode(
            in GameState state,
            SimOptions options,
            int learnerIdx,
            int prevDir0,
            int prevDir1,
            Span<float> dst
        )
        {
            if (dst.Length < StateFeatures)
                throw new ArgumentException(
                    $"destination too short: need {StateFeatures}, got {dst.Length}",
                    nameof(dst)
                );
            if ((uint)learnerIdx >= 2)
                throw new ArgumentOutOfRangeException(nameof(learnerIdx));

            int oppIdx = 1 - learnerIdx;

            // Pull normalization constants from the active config. Fallbacks
            // guard against a missing/zero value producing inf or nan.
            float wallsX = SafeScale((float)options.Global.WallsX, 10f);
            float groundY = (float)options.Global.GroundY;
            float maxHype = SafeScale((float)options.Global.MaxHype, 100f);
            float superMax = SafeScale((float)options.Global.SuperMax, 100f);
            float roundTicks = SafeScale(options.Global.RoundTimeTicks, 3600f);

            float selfHpMax = SafeScale(
                (float)(options.Players[learnerIdx].Character?.Health ?? (sfloat)100f),
                100f
            );
            float oppHpMax = SafeScale(
                (float)(options.Players[oppIdx].Character?.Health ?? (sfloat)100f),
                100f
            );
            float selfBurstMax = SafeScale(
                (float)(options.Players[learnerIdx].Character?.BurstMax ?? (sfloat)100f),
                100f
            );
            float oppBurstMax = SafeScale(
                (float)(options.Players[oppIdx].Character?.BurstMax ?? (sfloat)100f),
                100f
            );
            float selfFwdSpeed = SafeScale(
                (float)(options.Players[learnerIdx].Character?.ForwardSpeed ?? (sfloat)10f),
                10f
            );
            float oppFwdSpeed = SafeScale(
                (float)(options.Players[oppIdx].Character?.ForwardSpeed ?? (sfloat)10f),
                10f
            );
            float selfAirDashMax = MathF.Max(
                1f,
                options.Players[learnerIdx].Character?.NumAirDashes ?? 1
            );
            float oppAirDashMax = MathF.Max(
                1f,
                options.Players[oppIdx].Character?.NumAirDashes ?? 1
            );

            int idx = 0;

            // ---- global block ----
            dst[idx++] = (float)state.HypeMeter / maxHype;
            dst[idx++] = state.HitstopFramesRemaining / 60f;
            int sinceRoundStart = state.SimFrame.No - state.RoundStart.No;
            dst[idx++] = Clamp(sinceRoundStart / roundTicks, -1f, 2f);
            int untilRoundEnd = state.RoundEnd.No - state.SimFrame.No;
            dst[idx++] = Clamp(untilRoundEnd / roundTicks, -1f, 2f);

            // ---- self fighter block ----
            int prevDirSelf = learnerIdx == 0 ? prevDir0 : prevDir1;
            int prevDirOpp = learnerIdx == 0 ? prevDir1 : prevDir0;
            WriteFighter(
                state.Fighters[learnerIdx],
                state.SimFrame,
                wallsX,
                selfFwdSpeed,
                selfHpMax,
                selfBurstMax,
                superMax,
                selfAirDashMax,
                prevDirSelf,
                dst,
                ref idx
            );

            // ---- opp fighter block ----
            WriteFighter(
                state.Fighters[oppIdx],
                state.SimFrame,
                wallsX,
                oppFwdSpeed,
                oppHpMax,
                oppBurstMax,
                superMax,
                oppAirDashMax,
                prevDirOpp,
                dst,
                ref idx
            );

            // ---- relative block ----
            float selfX = (float)state.Fighters[learnerIdx].Position.x;
            float selfY = (float)state.Fighters[learnerIdx].Position.y;
            float oppX = (float)state.Fighters[oppIdx].Position.x;
            float oppY = (float)state.Fighters[oppIdx].Position.y;
            float dx = (oppX - selfX) / wallsX;
            float dy = (oppY - selfY) / 4f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            dst[idx++] = dx;
            dst[idx++] = dy;
            dst[idx++] = dist;

            // ---- projectile slots ----
            int projCount = state.Projectiles?.Length ?? 0;
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (i < projCount && state.Projectiles[i].Active)
                {
                    ProjectileState p = state.Projectiles[i];
                    float pdx = ((float)p.Position.x - selfX) / wallsX;
                    float pdy = ((float)p.Position.y - groundY) / 4f;
                    float pvx = (float)p.Velocity.x / selfFwdSpeed;
                    float pvy = (float)p.Velocity.y / selfFwdSpeed;
                    dst[idx++] = 1f;
                    dst[idx++] = pdx;
                    dst[idx++] = pdy;
                    dst[idx++] = pvx;
                    dst[idx++] = pvy;
                    dst[idx++] = p.IsDying ? 1f : 0f;
                    dst[idx++] = p.Owner == learnerIdx ? 1f : 0f;
                }
                else
                {
                    for (int k = 0; k < ProjectileFeaturesPerSlot; k++)
                        dst[idx++] = 0f;
                }
            }

            if (idx != StateFeatures)
                throw new InvalidOperationException(
                    $"feature count mismatch: wrote {idx}, expected {StateFeatures}"
                );
        }

        // Write the action-history block. dirHistory[i] / btnHistory[i] is the
        // action taken i+1 frames ago indexed oldest-first; entries past the
        // available history (or values < 0) encode as all-zeros. Both spans
        // must be ActionHistoryFrames long; dst must be HistoryFeatures long.
        public static void WriteHistory(
            ReadOnlySpan<int> dirHistory,
            ReadOnlySpan<int> btnHistory,
            Span<float> dst
        )
        {
            if (dirHistory.Length != ActionHistoryFrames)
                throw new ArgumentException(
                    $"dirHistory length must be {ActionHistoryFrames}, got {dirHistory.Length}",
                    nameof(dirHistory)
                );
            if (btnHistory.Length != ActionHistoryFrames)
                throw new ArgumentException(
                    $"btnHistory length must be {ActionHistoryFrames}, got {btnHistory.Length}",
                    nameof(btnHistory)
                );
            if (dst.Length < HistoryFeatures)
                throw new ArgumentException(
                    $"destination too short: need {HistoryFeatures}, got {dst.Length}",
                    nameof(dst)
                );

            for (int i = 0; i < ActionHistoryFrames; i++)
            {
                int dirOff = i * HistoryFeaturesPerStep;
                int btnOff = dirOff + ActionSpace.NumDirections;
                for (int k = 0; k < ActionSpace.NumDirections; k++)
                    dst[dirOff + k] = 0f;
                for (int k = 0; k < ActionSpace.NumButtons; k++)
                    dst[btnOff + k] = 0f;
                int d = dirHistory[i];
                int b = btnHistory[i];
                if ((uint)d < (uint)ActionSpace.NumDirections)
                    dst[dirOff + d] = 1f;
                if ((uint)b < (uint)ActionSpace.NumButtons)
                    dst[btnOff + b] = 1f;
            }
        }

        static void WriteFighter(
            in FighterState f,
            Frame simFrame,
            float wallsX,
            float fwdSpeed,
            float hpMax,
            float burstMax,
            float superMax,
            float airDashMax,
            int prevDir,
            Span<float> dst,
            ref int idx
        )
        {
            dst[idx++] = (float)f.Position.x / wallsX;
            dst[idx++] = (float)f.Position.y / 4f;
            dst[idx++] = (float)f.Velocity.x / fwdSpeed;
            dst[idx++] = (float)f.Velocity.y / fwdSpeed;

            dst[idx++] = (float)f.Health / hpMax;
            dst[idx++] = f.Lives / 3f;
            dst[idx++] = (float)f.Super / superMax;
            dst[idx++] = (float)f.Burst / burstMax;
            dst[idx++] = f.AirDashCount / airDashMax;
            dst[idx++] = f.ComboedCount / 20f;

            // Damage/knockback buff signal in freestyle mode (super landed,
            // multiplier active). Not a rhythm-combo flag - kept across the
            // v6 cleanup.
            dst[idx++] = f.FreestyleActive ? 1f : 0f;

            dst[idx++] = f.Actionable ? 1f : 0f;
            dst[idx++] = f.State.IsStunned() ? 1f : 0f;
            dst[idx++] = f.State.IsAerial() ? 1f : 0f;
            dst[idx++] = f.State.IsDash() ? 1f : 0f;
            dst[idx++] = f.State.IsKnockdown() ? 1f : 0f;

            dst[idx++] = f.FacingDir == FighterFacing.Right ? 1f : -1f;

            int framesInState = simFrame.No - f.StateStart.No;
            dst[idx++] = Clamp(framesInState / 60f, -1f, 4f);
            int framesUntilStateEnd =
                f.StateEnd.No >= int.MaxValue / 2 ? 60 : f.StateEnd.No - simFrame.No;
            dst[idx++] = Clamp(framesUntilStateEnd / 60f, -1f, 4f);

            dst[idx++] = (float)f.StoredJumpVelocity.x / fwdSpeed;
            dst[idx++] = (float)f.StoredJumpVelocity.y / fwdSpeed;

            // Previous-frame block intent, derived from the action committed
            // last Fighting frame and the fighter's BackwardInput. The action
            // mask in PolicyNet already reads BlockStand/BlockCrouch from the
            // CharacterState one-hot, but those bits track the resulting state
            // machine - these flags expose the intent (held back / down-back)
            // before the sim has transitioned into blockstun. prev == None
            // (or prevDir < 0 sentinel) → both flags zero.
            InputFlags prev = ActionSpace.DirectionFlagsAt(prevDir);
            InputFlags back = f.BackwardInput;
            bool prevHighBlocking = prev == back;
            bool prevLowBlocking = prev == (back | InputFlags.Down);
            dst[idx++] = prevHighBlocking ? 1f : 0f;
            dst[idx++] = prevLowBlocking ? 1f : 0f;

            // CharacterState one-hot (40 buckets covers 0..39).
            int s = (int)f.State;
            for (int k = 0; k < NumCharacterStates; k++)
                dst[idx++] = k == s ? 1f : 0f;
        }

        static float SafeScale(float value, float fallback)
        {
            // Avoid divide-by-zero or sign-flipping if a config value lands at
            // 0 (e.g. a placeholder Character with default sfloat).
            float a = MathF.Abs(value);
            return a > 1e-3f ? a : fallback;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        static ulong Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong h = offset;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= prime;
            }
            return h;
        }
    }
}
