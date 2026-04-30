using System;
using Game.Sim;
using Utils;
using Utils.SoftFloat;

namespace Hypermania.CPU.Featurization
{
    // Hand-built observation encoder. The schema-driven version (which walked
    // ObservationSchema and reflected ToFloat) silently encoded every array,
    // every sfloat (recursed to private uint rawValue), and every generic type
    // as 0 - the policy was effectively blind to all per-fighter and
    // per-projectile state. This encoder writes an explicit, normalized,
    // perspective-canonical observation.
    //
    // Layout (lengths in parens):
    //   global      (NumGameModes + 7)
    //   self        (FighterFeatures)        - learner-perspective fighter
    //   opp         (FighterFeatures)        - the other fighter
    //   relative    (3)                      - opp - self displacement, distance
    //   projectiles (MaxProjectiles * ProjectileFeaturesPerSlot)
    //
    // Canonicalization (so the policy sees a consistent view independent of
    // which side it's playing):
    //   - Self/opponent ordering: self comes first, regardless of learnerIdx.
    //   - X-axis mirroring: when learnerIdx == 1 (right side), every x-axis
    //     scalar is negated. The policy always sees self on the left, opp on
    //     the right, with positive vx pointing toward the opponent.
    //   - FacingDir is encoded as "facing toward opp" in the canonical frame.
    public static class Featurizer
    {
        const int NumGameModes = 6; // Fighting, ManiaStart, Mania, RoundEnd, Countdown, End
        const int NumCharacterStates = 40; // CharacterState max value (GetUp = 39) + 1
        const int MaxProjectiles = GameState.MAX_PROJECTILES;

        const int GlobalFeatures = NumGameModes + 7;

        // Per-fighter scalars in WriteFighter (count must equal the number of
        // dst[idx++] = ... lines preceding the one-hot loop).
        const int FighterScalars = 32;
        const int FighterFeatures = FighterScalars + NumCharacterStates;
        const int RelFeatures = 3;
        const int ProjectileFeaturesPerSlot = 7;
        const int ProjectileFeatures = MaxProjectiles * ProjectileFeaturesPerSlot;

        public static int Length => _length;
        public static ulong SchemaHash => _schemaHash;

        static readonly int _length =
            GlobalFeatures + 2 * FighterFeatures + RelFeatures + ProjectileFeatures;

        // FNV-1a over a stable layout descriptor. Bump LayoutVersion (the
        // leading "v2") whenever the feature list or normalization changes
        // semantics - the existing snapshot loader rejects mismatched hashes.
        static readonly ulong _schemaHash = Fnv1a64(
            $"v2;global={GlobalFeatures};fighter={FighterFeatures};"
                + $"rel={RelFeatures};proj={MaxProjectiles}x{ProjectileFeaturesPerSlot};"
                + $"states={NumCharacterStates};modes={NumGameModes}"
        );

        public static void Encode(
            in GameState state,
            SimOptions options,
            int learnerIdx,
            Span<float> dst
        )
        {
            if (dst.Length < _length)
                throw new ArgumentException(
                    $"destination too short: need {_length}, got {dst.Length}",
                    nameof(dst)
                );
            if ((uint)learnerIdx >= 2)
                throw new ArgumentOutOfRangeException(nameof(learnerIdx));

            int oppIdx = 1 - learnerIdx;
            float mirror = learnerIdx == 0 ? 1f : -1f;

            // Pull normalization constants from the active config. Fallbacks
            // guard against a missing/zero value producing inf or nan.
            float wallsX = SafeScale((float)options.Global.WallsX, 10f);
            float groundY = (float)options.Global.GroundY;
            float maxHype = SafeScale((float)options.Global.MaxHype, 100f);
            float superMax = SafeScale((float)options.Global.SuperMax, 100f);
            float roundTicks = SafeScale(options.Global.RoundTimeTicks, 3600f);
            float superTier2Beats = SafeScale(options.Global.SuperTier2Beats, 16f);

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
            int gameModeInt = (int)state.GameMode;
            for (int i = 0; i < NumGameModes; i++)
                dst[idx++] = i == gameModeInt ? 1f : 0f;

            dst[idx++] = (float)state.HypeMeter / maxHype;
            dst[idx++] = state.HitstopFramesRemaining / 60f;
            dst[idx++] = (float)state.SpeedRatio;
            int sinceRoundStart = state.SimFrame.No - state.RoundStart.No;
            dst[idx++] = Clamp(sinceRoundStart / roundTicks, -1f, 2f);
            int untilRoundEnd = state.RoundEnd.No - state.SimFrame.No;
            dst[idx++] = Clamp(untilRoundEnd / roundTicks, -1f, 2f);
            dst[idx++] = state.PendingRhythmComboAttacker == learnerIdx ? 1f : 0f;
            dst[idx++] = state.PendingRhythmComboAttacker == oppIdx ? 1f : 0f;

            // ---- self fighter block ----
            WriteFighter(
                state.Fighters[learnerIdx],
                state.SimFrame,
                mirror,
                oppIdx,
                wallsX,
                selfFwdSpeed,
                selfHpMax,
                selfBurstMax,
                superMax,
                selfAirDashMax,
                superTier2Beats,
                dst,
                ref idx
            );

            // ---- opp fighter block ----
            WriteFighter(
                state.Fighters[oppIdx],
                state.SimFrame,
                mirror,
                learnerIdx,
                wallsX,
                oppFwdSpeed,
                oppHpMax,
                oppBurstMax,
                superMax,
                oppAirDashMax,
                superTier2Beats,
                dst,
                ref idx
            );

            // ---- relative block ----
            float selfX = (float)state.Fighters[learnerIdx].Position.x;
            float selfY = (float)state.Fighters[learnerIdx].Position.y;
            float oppX = (float)state.Fighters[oppIdx].Position.x;
            float oppY = (float)state.Fighters[oppIdx].Position.y;
            float dx = (oppX - selfX) * mirror / wallsX;
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
                    float pdx = ((float)p.Position.x - selfX) * mirror / wallsX;
                    float pdy = ((float)p.Position.y - groundY) / 4f;
                    float pvx = (float)p.Velocity.x * mirror / selfFwdSpeed;
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

            if (idx != _length)
                throw new InvalidOperationException(
                    $"feature count mismatch: wrote {idx}, expected {_length}"
                );
        }

        static void WriteFighter(
            in FighterState f,
            Frame simFrame,
            float mirror,
            int oppIdx,
            float wallsX,
            float fwdSpeed,
            float hpMax,
            float burstMax,
            float superMax,
            float airDashMax,
            float superTier2Beats,
            Span<float> dst,
            ref int idx
        )
        {
            // canonical-frame position/velocity
            dst[idx++] = (float)f.Position.x * mirror / wallsX;
            dst[idx++] = (float)f.Position.y / 4f;
            dst[idx++] = (float)f.Velocity.x * mirror / fwdSpeed;
            dst[idx++] = (float)f.Velocity.y / fwdSpeed;

            dst[idx++] = (float)f.Health / hpMax;
            dst[idx++] = f.Lives / 3f;
            dst[idx++] = (float)f.Super / superMax;
            dst[idx++] = (float)f.Burst / burstMax;
            dst[idx++] = f.AirDashCount / airDashMax;
            dst[idx++] = f.ComboedCount / 20f;

            dst[idx++] = f.IsSuperAttack ? 1f : 0f;
            dst[idx++] = f.AttackConnected ? 1f : 0f;
            dst[idx++] = f.LockedHitstun ? 1f : 0f;
            dst[idx++] = f.RhythmComboFinisherActive ? 1f : 0f;
            dst[idx++] = f.RhythmComboTier2 ? 1f : 0f;
            dst[idx++] = f.FreestyleActive ? 1f : 0f;
            dst[idx++] = f.BlockedLastRealFrame ? 1f : 0f;
            dst[idx++] = f.HitLastRealFrame ? 1f : 0f;
            dst[idx++] = f.DashedLastRealFrame ? 1f : 0f;
            dst[idx++] = f.Actionable ? 1f : 0f;
            dst[idx++] = f.State.IsStunned() ? 1f : 0f;
            dst[idx++] = f.State.IsAerial() ? 1f : 0f;
            dst[idx++] = f.State.IsDash() ? 1f : 0f;
            dst[idx++] = f.State.IsKnockdown() ? 1f : 0f;

            // Facing in canonical frame: +1 if facing toward where opp is
            // drawn (right of self post-mirror), -1 otherwise.
            float facingWorld = f.FacingDir == FighterFacing.Right ? 1f : -1f;
            dst[idx++] = facingWorld * mirror;

            int framesInState = simFrame.No - f.StateStart.No;
            dst[idx++] = Clamp(framesInState / 60f, -1f, 4f);
            int framesUntilStateEnd =
                f.StateEnd.No >= int.MaxValue / 2 ? 60 : f.StateEnd.No - simFrame.No;
            dst[idx++] = Clamp(framesUntilStateEnd / 60f, -1f, 4f);

            dst[idx++] = (float)f.NoOpBonus;
            dst[idx++] = (float)f.NoOpBonusRemaining;
            dst[idx++] = (float)f.StoredJumpVelocity.x * mirror / fwdSpeed;
            dst[idx++] = (float)f.StoredJumpVelocity.y / fwdSpeed;
            dst[idx++] = f.SuperComboBeats / superTier2Beats;

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
