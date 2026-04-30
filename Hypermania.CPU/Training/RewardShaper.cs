using System;
using Game.Sim;
using Game.Sim.Configs;

namespace Hypermania.CPU.Training
{
    public struct RewardConfig
    {
        public float DamageScale; // per HP of damage
        public float WinBonus; // terminal, match-level win
        public float LossPenalty; // terminal, match-level loss (negative)
        public float StepPenalty; // per-tick constant (negative)
        public float ApproachReward; // per (unit/tick) of horizontal velocity toward the opponent
        public float BlockReward; // per frame this fighter is in a successful block
        public float WhiffPenalty; // per recovery tick where the opponent isn't stunned

        public static RewardConfig Default =>
            new RewardConfig
            {
                DamageScale = 0.005f,
                WinBonus = 1.0f,
                LossPenalty = -1.0f,
                StepPenalty = 0.0001f,
                // Dense per-frame shaping scaled so that a full 1500-frame
                // round can't accumulate more total reward than the terminal
                // ±1.0 win/loss signal.
                ApproachReward = 0.00002f,
                // Per-tick while in blockstun. Dominant shaping term by
                // design: a single short block (~10 frames) ≈ 0.25, a
                // heavy-block round (~150 blocked frames) ≈ 3.75 — well above
                // the ±1.0 win/loss anchor. Forces blocking to emerge before
                // the policy figures out winning, accepting that the agent
                // may early on prefer eating shaping over closing matches.
                BlockReward = 0.025f,
                WhiffPenalty = 0.001f,
            };
    }

    // Per-fighter, per-tick scalar reward decomposed by source. The TUI shows
    // these category totals so the human can see whether shaping is balanced
    // (no single term dominating). Sum equals the scalar reward fed to PPO.
    public struct RewardBreakdown
    {
        public float Damage;
        public float Step;
        public float Approach;
        public float Block;
        public float Whiff;
        public float Terminal;

        public float Total => Damage + Step + Approach + Block + Whiff + Terminal;

        public static RewardBreakdown operator +(RewardBreakdown a, RewardBreakdown b) =>
            new RewardBreakdown
            {
                Damage = a.Damage + b.Damage,
                Step = a.Step + b.Step,
                Approach = a.Approach + b.Approach,
                Block = a.Block + b.Block,
                Whiff = a.Whiff + b.Whiff,
                Terminal = a.Terminal + b.Terminal,
            };

        public static RewardBreakdown operator /(RewardBreakdown a, float s) =>
            new RewardBreakdown
            {
                Damage = a.Damage / s,
                Step = a.Step / s,
                Approach = a.Approach / s,
                Block = a.Block / s,
                Whiff = a.Whiff / s,
                Terminal = a.Terminal / s,
            };
    }

    // Computes per-tick scalar rewards for each fighter from before/after
    // GameState snapshots. Usage: BeforeStep(state) -> env.Step(...) ->
    // AfterStep(state, out).
    public sealed class RewardShaper
    {
        readonly RewardConfig _cfg;
        readonly SimOptions _simOptions;
        readonly float[] _prevHealth;
        GameMode _prevMode;

        public RewardShaper(SimOptions simOptions, RewardConfig cfg)
        {
            _cfg = cfg;
            _simOptions = simOptions;
            _prevHealth = new float[simOptions.Players.Length];
        }

        public void Reset()
        {
            for (int i = 0; i < _prevHealth.Length; i++)
                _prevHealth[i] = 0f;
            _prevMode = GameMode.Countdown;
        }

        // Capture the scalar fields needed for the reward diff before the
        // sim advances. Cheap (no GameState clone).
        public void BeforeStep(in GameState s)
        {
            for (int i = 0; i < _prevHealth.Length; i++)
                _prevHealth[i] = (float)s.Fighters[i].Health;
            _prevMode = s.GameMode;
        }

        // Compute per-fighter rewards for the tick that just produced `cur`,
        // decomposed by source. Caller can read the scalar `.Total` for the
        // PPO reward stream and the individual fields for telemetry.
        public void AfterStep(in GameState cur, Span<RewardBreakdown> outBreakdowns)
        {
            int n = cur.Fighters.Length;
            if (outBreakdowns.Length < n)
                throw new ArgumentException(
                    $"need {n} breakdown slots, got {outBreakdowns.Length}"
                );

            for (int i = 0; i < n; i++)
                outBreakdowns[i] = default;

            // Damage delta: damage taken by self penalizes self; damage dealt
            // to opponent rewards self. Symmetric over the two fighters in 1v1.
            for (int i = 0; i < n; i++)
            {
                float prevSelf = _prevHealth[i];
                float curSelf = (float)cur.Fighters[i].Health;
                int opp = 1 - i;
                float prevOpp = _prevHealth[opp];
                float curOpp = (float)cur.Fighters[opp].Health;

                float dmgTaken = Math.Max(0f, prevSelf - curSelf);
                float dmgDealt = Math.Max(0f, prevOpp - curOpp);

                outBreakdowns[i].Damage = _cfg.DamageScale * (dmgDealt - dmgTaken);
                outBreakdowns[i].Step = -_cfg.StepPenalty;
            }

            // Approach shaping: per-fighter, per-tick reward proportional to
            // this fighter's own horizontal velocity toward the opponent.
            // Jumping in place → 0; walking forward → positive; walking back
            // → negative; jump-toward → positive. Per-fighter attribution
            // (vs the earlier symmetric distance-closure signal) prevents a
            // passive fighter from free-riding on its opponent's motion.
            if (_cfg.ApproachReward != 0f && n >= 2)
            {
                for (int i = 0; i < n; i++)
                {
                    int opp = 1 - i;
                    float selfX = (float)cur.Fighters[i].Position.x;
                    float oppX = (float)cur.Fighters[opp].Position.x;
                    float selfVx = (float)cur.Fighters[i].Velocity.x;
                    float dirToOpp = oppX > selfX ? 1f : -1f;
                    outBreakdowns[i].Approach = _cfg.ApproachReward * selfVx * dirToOpp;
                }
            }

            // Block reward: per-tick while the fighter is in blockstun
            // (BlockStand / BlockCrouch). Rewards holding the guard through
            // the full block window, not just the contact frame, so the
            // signal scales with how long an attack the fighter chose to
            // block — discouraging mash-out-of-block. Symmetric damage term
            // already rewards "not getting hit" generally; this adds a
            // targeted signal toward block over jump/dash-back avoidance.
            if (_cfg.BlockReward != 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    CharacterState s = cur.Fighters[i].State;
                    if (s == CharacterState.BlockStand || s == CharacterState.BlockCrouch)
                        outBreakdowns[i].Block = _cfg.BlockReward;
                }
            }

            // Whiff penalty: per-tick negative on recovery frames of an
            // attack the opponent didn't have to respect. If the opponent
            // is stunned (hitstun, blockstun, knockdown, grabbed) the move
            // either connected or got blocked — both fine, no penalty.
            // Recovery against a free opponent is the punishable state we
            // want to discourage spamming.
            if (_cfg.WhiffPenalty != 0f && n >= 2)
            {
                for (int i = 0; i < n; i++)
                {
                    int opp = 1 - i;
                    if (cur.Fighters[opp].State.IsStunned())
                        continue;
                    CharacterStats stats = _simOptions.Players[i].Character;
                    if (stats == null)
                        continue;
                    HitboxData curData = stats.GetHitboxData(cur.Fighters[i].State);
                    FrameData frameData = curData?.GetFrame(
                        cur.SimFrame - cur.Fighters[i].StateStart
                    );
                    if (frameData != null && frameData.FrameType == FrameType.Recovery)
                        outBreakdowns[i].Whiff = -_cfg.WhiffPenalty;
                }
            }

            // Terminal win/loss. Fires once when the match has just ended
            // this tick. Lives count tells us who survived.
            if (cur.GameMode == GameMode.End && _prevMode != GameMode.End)
            {
                int p1Lives = cur.Fighters[0].Lives;
                int p2Lives = cur.Fighters[1].Lives;
                if (p1Lives > p2Lives)
                {
                    outBreakdowns[0].Terminal = _cfg.WinBonus;
                    outBreakdowns[1].Terminal = _cfg.LossPenalty;
                }
                else if (p2Lives > p1Lives)
                {
                    outBreakdowns[1].Terminal = _cfg.WinBonus;
                    outBreakdowns[0].Terminal = _cfg.LossPenalty;
                }
                // Tie: no terminal bonus either way.
            }
        }
    }
}
