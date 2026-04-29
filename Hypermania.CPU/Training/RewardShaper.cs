using System;
using Game.Sim;

namespace Hypermania.CPU.Training
{
    public struct RewardConfig
    {
        public float DamageScale;     // per HP of damage
        public float WhiffPenalty;    // per attack that ended without connecting
        public float WinBonus;        // terminal, match-level win
        public float LossPenalty;     // terminal, match-level loss (negative)
        public float StepPenalty;     // per-tick constant (negative)

        public static RewardConfig Default => new RewardConfig
        {
            DamageScale = 0.01f,
            WhiffPenalty = 0.05f,
            WinBonus = 1.0f,
            LossPenalty = -1.0f,
            StepPenalty = 0.0001f,
        };
    }

    // Computes per-tick scalar rewards for each fighter from before/after
    // GameState snapshots. The shaper owns the cross-tick bookkeeping needed
    // to detect attack windows that ended without ever registering a hit
    // (whiffs). Usage: BeforeStep(state) -> env.Step(...) -> AfterStep(state, out).
    public sealed class RewardShaper
    {
        readonly RewardConfig _cfg;
        readonly bool[] _attackInProgress;
        readonly bool[] _attackConnectedSticky;
        readonly CharacterState[] _attackState;
        readonly float[] _prevHealth;
        GameMode _prevMode;

        public RewardShaper(int numFighters, RewardConfig cfg)
        {
            _cfg = cfg;
            _attackInProgress = new bool[numFighters];
            _attackConnectedSticky = new bool[numFighters];
            _attackState = new CharacterState[numFighters];
            _prevHealth = new float[numFighters];
        }

        public void Reset()
        {
            for (int i = 0; i < _attackInProgress.Length; i++)
            {
                _attackInProgress[i] = false;
                _attackConnectedSticky[i] = false;
                _attackState[i] = default;
                _prevHealth[i] = 0f;
            }
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

        // Compute per-fighter rewards for the tick that just produced `cur`.
        public void AfterStep(in GameState cur, Span<float> outRewards)
        {
            int n = cur.Fighters.Length;
            if (outRewards.Length < n)
                throw new ArgumentException($"need {n} reward slots, got {outRewards.Length}");

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

                float r = _cfg.DamageScale * (dmgDealt - dmgTaken);
                r -= _cfg.StepPenalty;
                outRewards[i] = r;
            }

            // Whiff edge detection: track per-fighter "is currently in an
            // attack state" + sticky AttackConnected within that window.
            for (int i = 0; i < n; i++)
            {
                CharacterState s = cur.Fighters[i].State;
                bool isAttack = IsAttackState(s);
                bool ackNow = cur.Fighters[i].AttackConnected;

                if (isAttack)
                {
                    if (!_attackInProgress[i])
                    {
                        _attackInProgress[i] = true;
                        _attackState[i] = s;
                        _attackConnectedSticky[i] = ackNow;
                    }
                    else if (s != _attackState[i])
                    {
                        // Attack -> attack transition (gatling/cancel). The
                        // sim only allows that when AttackConnected was true
                        // (FighterState.IsGatlingCancelAllowed), so closing
                        // out the previous window here can't produce a
                        // false whiff.
                        if (!_attackConnectedSticky[i])
                            outRewards[i] -= _cfg.WhiffPenalty;
                        _attackState[i] = s;
                        _attackConnectedSticky[i] = ackNow;
                    }
                    else
                    {
                        _attackConnectedSticky[i] |= ackNow;
                    }
                }
                else if (_attackInProgress[i])
                {
                    if (!_attackConnectedSticky[i])
                        outRewards[i] -= _cfg.WhiffPenalty;
                    _attackInProgress[i] = false;
                    _attackConnectedSticky[i] = false;
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
                    outRewards[0] += _cfg.WinBonus;
                    outRewards[1] += _cfg.LossPenalty;
                }
                else if (p2Lives > p1Lives)
                {
                    outRewards[1] += _cfg.WinBonus;
                    outRewards[0] += _cfg.LossPenalty;
                }
                // Tie: no terminal bonus either way.
            }
        }

        // Whether a CharacterState represents the player having thrown out an
        // attack (something that *should* connect to be worthwhile). Includes
        // grounded / aerial / crouching variants and special-move follow-ups.
        // Excludes Burst (defensive) and Grab (graphic outcome of grab depends
        // on contact, but the connect-vs-whiff signal works the same way).
        static bool IsAttackState(CharacterState s)
        {
            switch (s)
            {
                case CharacterState.LightAttack:
                case CharacterState.LightAerial:
                case CharacterState.LightCrouching:
                case CharacterState.MediumAttack:
                case CharacterState.MediumAerial:
                case CharacterState.MediumCrouching:
                case CharacterState.MediumAttackFollowUp:
                case CharacterState.HeavyAttack:
                case CharacterState.HeavyAerial:
                case CharacterState.HeavyCrouching:
                case CharacterState.HeavyAerialFollowUp:
                case CharacterState.SpecialAttack:
                case CharacterState.SpecialAerial:
                case CharacterState.SpecialCrouching:
                case CharacterState.SpecialAerialFollowUp:
                case CharacterState.Ultimate:
                case CharacterState.Grab:
                    return true;
                default:
                    return false;
            }
        }
    }
}
