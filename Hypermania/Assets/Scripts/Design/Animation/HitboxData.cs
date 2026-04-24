using System;
using System.Collections.Generic;
using Game;
using Game.Sim;
using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Animation
{
    [Serializable]
    public enum HitboxKind
    {
        Hurtbox,
        Hitbox,
        Grabbox,
    }

    [Serializable]
    public enum AttackKind
    {
        Medium,
        Overhead,
        Low,
    }

    [Serializable]
    public enum KnockdownKind
    {
        None,
        Light,
        Heavy,
    }

    [Serializable]
    [MemoryPackable]
    public partial struct BoxProps : IEquatable<BoxProps>
    {
        // NOTE: ensure that any new fields added above are added to the equals and hashcode implementation!!!
        public HitboxKind Kind;
        public AttackKind AttackKind;
        public int Damage;
        public int HitstunTicks;
        public int BlockstunTicks;
        public int HitstopTicks;
        public int BlockstopTicks;
        public KnockdownKind KnockdownKind;
        public SVector2 Knockback;
        public SVector2 GrabPosition;
        public bool GrabsGrounded;
        public bool GrabsAirborne;
        public bool Techable;
        public bool HasTransition;
        public bool Unblockable;
        public CharacterState OnHitTransition;

        public bool Equals(BoxProps other) =>
            Kind == other.Kind
            && AttackKind == other.AttackKind
            && HitstunTicks == other.HitstunTicks
            && Damage == other.Damage
            && BlockstunTicks == other.BlockstunTicks
            && Knockback == other.Knockback
            && KnockdownKind == other.KnockdownKind
            && HitstopTicks == other.HitstopTicks
            && BlockstopTicks == other.BlockstopTicks
            && GrabPosition == other.GrabPosition
            && GrabsGrounded == other.GrabsGrounded
            && GrabsAirborne == other.GrabsAirborne
            && Techable == other.Techable
            && HasTransition == other.HasTransition
            && Unblockable == other.Unblockable
            && OnHitTransition == other.OnHitTransition;

        public override bool Equals(object obj) => obj is BoxProps other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                HashCode.Combine(Kind, AttackKind, HitstunTicks, Damage, BlockstunTicks, KnockdownKind, Knockback),
                HashCode.Combine(HitstopTicks, BlockstopTicks, GrabPosition, GrabsGrounded, GrabsAirborne),
                Techable,
                HasTransition,
                Unblockable,
                OnHitTransition
            );

        public static bool operator ==(BoxProps a, BoxProps b) => a.Equals(b);

        public static bool operator !=(BoxProps a, BoxProps b) => !a.Equals(b);
    }

    [Serializable]
    public struct BoxData : IEquatable<BoxData>
    {
        public SVector2 CenterLocal;
        public SVector2 SizeLocal;
        public BoxProps Props;

        public bool Equals(BoxData other)
        {
            return CenterLocal == other.CenterLocal && SizeLocal == other.SizeLocal && Props.Equals(other.Props);
        }

        public override bool Equals(object obj) => obj is BoxData other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(CenterLocal, SizeLocal, Props);
        }

        public static bool operator ==(BoxData left, BoxData right) => left.Equals(right);

        public static bool operator !=(BoxData left, BoxData right) => !left.Equals(right);
    }

    public enum FrameType
    {
        Neutral,
        Startup,
        Active,
        Recovery,
        Hitstun,
        Blockstun,
        Hitstop,
        Grabbed,
        Knockdown,
    }

    public enum FrameAttribute
    {
        Floating,
    }

    [Serializable]
    public class FrameData
    {
        public List<BoxData> Boxes = new List<BoxData>();
        public FrameType FrameType = FrameType.Neutral;
        public bool Floating;
        public bool GravityEnabled = true;
        public bool ShouldApplyVel;
        public SVector2 ApplyVelocity;
        public bool ShouldTeleport;
        public SVector2 TeleportLocation;
        public SVector2 RootMotionOffset;

        public FrameData Clone()
        {
            var copy = new FrameData();
            copy.Boxes = new List<BoxData>(Boxes);
            copy.FrameType = FrameType;
            copy.Floating = Floating;
            copy.ShouldApplyVel = ShouldApplyVel;
            copy.ApplyVelocity = ApplyVelocity;
            copy.ShouldTeleport = ShouldTeleport;
            copy.TeleportLocation = TeleportLocation;
            copy.GravityEnabled = GravityEnabled;
            copy.RootMotionOffset = RootMotionOffset;
            return copy;
        }

        public void CopyFrom(FrameData other)
        {
            if (other == null)
                return;
            Boxes.Clear();
            Boxes.AddRange(other.Boxes);
            Floating = other.Floating;
            FrameType = other.FrameType;
            ShouldApplyVel = other.ShouldApplyVel;
            ApplyVelocity = other.ApplyVelocity;
            ShouldTeleport = other.ShouldTeleport;
            TeleportLocation = other.TeleportLocation;
            GravityEnabled = other.GravityEnabled;
            RootMotionOffset = other.RootMotionOffset;
        }

        public int GetValueHash()
        {
            var hc = new HashCode();
            hc.Add(Boxes != null ? Boxes.Count : 0);
            if (Boxes != null)
            {
                for (int j = 0; j < Boxes.Count; j++)
                {
                    hc.Add(Boxes[j]);
                }
            }
            hc.Add(FrameType);
            hc.Add(Floating);
            hc.Add(ShouldApplyVel);
            hc.Add(ApplyVelocity);
            hc.Add(ShouldTeleport);
            hc.Add(TeleportLocation);
            hc.Add(GravityEnabled);
            hc.Add(RootMotionOffset);
            return hc.ToHashCode();
        }

        public bool HasHitbox(out BoxProps outBox)
        {
            foreach (BoxData box in Boxes)
            {
                if (box.Props.Kind == HitboxKind.Hitbox || box.Props.Kind == HitboxKind.Grabbox)
                {
                    outBox = box.Props;
                    return true;
                }
            }
            outBox = default;
            return false;
        }
    }

    [CreateAssetMenu(menuName = "Hypermania/Move Data")]
    [Serializable]
    public class HitboxData : ScriptableObject
    {
        public AnimationClip Clip;
        public int TotalTicks => Frames.Count;
        public bool AnimLoops => Clip.isLooping;
        public bool ComboEligible = true;
        public CharacterState Followup = CharacterState.Idle;
        public InputFlags FollowupInput = InputFlags.None;
        public bool IgnoreOwner;
        public bool ApplyRootMotion;
        public List<FrameData> Frames = new List<FrameData>();

        [NonSerialized]
        private int _startupTicks;

        [NonSerialized]
        private int _activeTicks;

        [NonSerialized]
        private int _recoveryTicks;

        [NonSerialized]
        private int _lastHitReferenceFrame;

        [NonSerialized]
        private int _lastHitHitstunTicks;

        [NonSerialized]
        private bool _frameDataCached;

        public int StartupTicks
        {
            get
            {
                EnsureFrameDataCached();
                return _startupTicks;
            }
        }
        public int ActiveTicks
        {
            get
            {
                EnsureFrameDataCached();
                return _activeTicks;
            }
        }
        public int RecoveryTicks
        {
            get
            {
                EnsureFrameDataCached();
                return _recoveryTicks;
            }
        }

        /// <summary>
        /// Frame-advantage on hit, measured from the first hitbox in the last contiguous
        /// interval of hitbox-bearing frames (the reference hit). Positive means the
        /// attacker becomes actionable before the defender leaves hitstun.
        /// Returns 0 for moves with no hitbox.
        /// </summary>
        public int OnHitAdvantage
        {
            get
            {
                EnsureFrameDataCached();
                if (_lastHitReferenceFrame < 0)
                    return 0;
                return _lastHitHitstunTicks - (TotalTicks - _lastHitReferenceFrame);
            }
        }

        private void OnEnable()
        {
            _frameDataCached = false;
            EnsureFrameDataCached();
        }

        private void EnsureFrameDataCached()
        {
            if (_frameDataCached)
                return;

            int[] counts = new int[ATTACK_FRAME_TYPE_ORDER.Length];
            if (IsValidAttack(counts))
            {
                _startupTicks = counts[0];
                _activeTicks = counts[1];
                _recoveryTicks = counts[2];
            }
            else
            {
                _startupTicks = _activeTicks = _recoveryTicks = 0;
            }

            int lastIntervalStart = -1;
            bool inInterval = false;
            for (int i = 0; i < Frames.Count; i++)
            {
                bool has = Frames[i].HasHitbox(out _);
                if (has && !inInterval)
                {
                    lastIntervalStart = i;
                    inInterval = true;
                }
                else if (!has)
                {
                    inInterval = false;
                }
            }
            if (lastIntervalStart >= 0 && Frames[lastIntervalStart].HasHitbox(out BoxProps props))
            {
                _lastHitReferenceFrame = lastIntervalStart;
                _lastHitHitstunTicks = props.HitstunTicks;
            }
            else
            {
                _lastHitReferenceFrame = -1;
                _lastHitHitstunTicks = 0;
            }

            _frameDataCached = true;
        }

        public float GetAnimNormalizedTime(int frame)
        {
            int totalTicks = TotalTicks;
            if (AnimLoops)
            {
                frame %= totalTicks;
            }
            else
            {
                frame = Mathf.Min(frame, totalTicks - 1);
            }
            return (float)frame / (totalTicks - 1);
        }

        public bool BindToClip(AnimationClip clip)
        {
            bool changed = false;
            if (Clip != clip)
            {
                Clip = clip;
                changed = true;
            }
            int totalTicks = Mathf.CeilToInt(clip.length * GameManager.TPS) + 1;
            if (totalTicks < 1)
            {
                throw new InvalidOperationException("total ticks must be >= 1");
            }
            while (Frames.Count < totalTicks)
            {
                Frames.Add(new FrameData());
                changed = true;
            }
            while (Frames.Count > totalTicks)
            {
                Frames.RemoveAt(Frames.Count - 1);
                changed = true;
            }
            return changed;
        }

        public bool HasHitbox()
        {
            foreach (FrameData frame in Frames)
            {
                if (frame.HasHitbox(out _))
                {
                    return true;
                }
            }

            return false;
        }

        public static readonly FrameType[] ATTACK_FRAME_TYPE_ORDER =
        {
            FrameType.Startup,
            FrameType.Active,
            FrameType.Recovery,
        };

        public bool IsValidAttack(int[] frameCount)
        {
            if (!HasHitbox())
            {
                return false;
            }

            for (int i = 0; i < ATTACK_FRAME_TYPE_ORDER.Length; i++)
            {
                frameCount[i] = 0;
            }

            int frameTypeIndex = 0;
            foreach (FrameData data in Frames)
            {
                if (data.FrameType != ATTACK_FRAME_TYPE_ORDER[frameTypeIndex])
                {
                    if (frameTypeIndex + 1 >= ATTACK_FRAME_TYPE_ORDER.Length)
                    {
                        return false;
                    }
                    if (data.FrameType != ATTACK_FRAME_TYPE_ORDER[frameTypeIndex + 1])
                    {
                        return false;
                    }
                    frameTypeIndex++;
                }

                frameCount[frameTypeIndex]++;
            }

            return frameCount[frameTypeIndex] > 0;
        }

        public FrameData GetFrame(int tick)
        {
            if (Frames == null || Frames.Count == 0)
                return null;
            tick = Mathf.Clamp(tick, 0, TotalTicks - 1);
            return Frames[tick];
        }

        public int GetValueHash()
        {
            var hc = new HashCode();

            hc.Add(Clip ? Clip.GetInstanceID() : 0);
            hc.Add(IgnoreOwner);
            hc.Add(ApplyRootMotion);
            hc.Add(Frames != null ? Frames.Count : 0);

            if (Frames != null)
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    hc.Add(Frames[i].GetValueHash());
                }
            }

            return hc.ToHashCode();
        }
    }
}
