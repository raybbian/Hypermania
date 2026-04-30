using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim.Configs
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
            && OnHitTransition == other.OnHitTransition;

        public override bool Equals(object obj) => obj is BoxProps other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                HashCode.Combine(Kind, AttackKind, HitstunTicks, Damage, BlockstunTicks, KnockdownKind, Knockback),
                HashCode.Combine(HitstopTicks, BlockstopTicks, GrabPosition, GrabsGrounded, GrabsAirborne),
                Techable,
                HasTransition,
                OnHitTransition
            );

        public static bool operator ==(BoxProps a, BoxProps b) => a.Equals(b);

        public static bool operator !=(BoxProps a, BoxProps b) => !a.Equals(b);
    }

    [Serializable]
    [MemoryPackable]
    public partial struct BoxData : IEquatable<BoxData>
    {
        public SVector2 CenterLocal;
        public SVector2 SizeLocal;
        public BoxProps Props;

        public bool Equals(BoxData other) =>
            CenterLocal == other.CenterLocal && SizeLocal == other.SizeLocal && Props.Equals(other.Props);

        public override bool Equals(object obj) => obj is BoxData other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(CenterLocal, SizeLocal, Props);

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
    [MemoryPackable]
    public partial class FrameData
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
    [MemoryPackable]
    public partial class HitboxData : ScriptableObject
    {
        // Editor-time imported metadata. The MoveBuilder pulls these off the
        // authored AnimationClip; runtime sim never reads the clip itself, so
        // we don't carry a Unity ref here.
        public bool AnimLoops;
        public string ClipName;
        public int TotalTicks => Frames.Count;
        public bool ComboEligible = true;
        public CharacterState Followup = CharacterState.Idle;
        public InputFlags FollowupInput = InputFlags.None;
        public bool IgnoreOwner;
        public bool ApplyRootMotion;
        public List<FrameData> Frames = new List<FrameData>();

        [MemoryPackIgnore]
        public new string name
        {
            get => base.name;
            set => base.name = value;
        }

        [MemoryPackIgnore]
        public new HideFlags hideFlags
        {
            get => base.hideFlags;
            set => base.hideFlags = value;
        }

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

        // Frame-advantage on hit, measured from the first hitbox in the last
        // contiguous interval of hitbox-bearing frames (the reference hit).
        // Positive means the attacker becomes actionable before the defender
        // leaves hitstun. Returns 0 for moves with no hitbox.
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

        // Editor-time bind: MoveBuilder pulls the metadata it cares about off
        // the AnimationClip and pushes it in here. Returns true iff anything
        // changed (so the editor can mark dirty).
        public bool ApplyClipMeta(string clipName, int totalTicks, bool animLoops)
        {
            if (totalTicks < 1)
            {
                throw new InvalidOperationException("total ticks must be >= 1");
            }
            bool changed = false;
            if (ClipName != clipName)
            {
                ClipName = clipName;
                changed = true;
            }
            if (AnimLoops != animLoops)
            {
                AnimLoops = animLoops;
                changed = true;
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
            tick = System.Math.Clamp(tick, 0, TotalTicks - 1);
            return Frames[tick];
        }
    }
}
