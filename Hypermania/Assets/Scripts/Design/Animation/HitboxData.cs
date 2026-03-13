using System;
using System.Collections.Generic;
using Game;
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
        public bool StartsRhythmCombo;
        public KnockdownKind KnockdownKind;
        public SVector2 Knockback;

        public bool Equals(BoxProps other) =>
            Kind == other.Kind
            && AttackKind == other.AttackKind
            && HitstunTicks == other.HitstunTicks
            && Damage == other.Damage
            && BlockstunTicks == other.BlockstunTicks
            && Knockback == other.Knockback
            && KnockdownKind == other.KnockdownKind
            && StartsRhythmCombo == other.StartsRhythmCombo
            && HitstopTicks == other.HitstopTicks
            && BlockstopTicks == other.BlockstopTicks;

        public override bool Equals(object obj) => obj is BoxProps other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                HashCode.Combine(
                    Kind,
                    AttackKind,
                    HitstunTicks,
                    Damage,
                    BlockstunTicks,
                    StartsRhythmCombo,
                    KnockdownKind,
                    Knockback
                ),
                HitstopTicks,
                BlockstopTicks
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

        public FrameData Clone()
        {
            var copy = new FrameData();
            copy.Boxes = new List<BoxData>(Boxes);
            copy.FrameType = FrameType;
            copy.Floating = Floating;
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
            return hc.ToHashCode();
        }

        public bool HasHitbox()
        {
            foreach (BoxData box in Boxes)
            {
                if (box.Props.Kind == HitboxKind.Hitbox)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [CreateAssetMenu(menuName = "Hypermania/Move Data")]
    [Serializable]
    public class HitboxData : ScriptableObject
    {
        public AnimationClip Clip;
        public int TotalTicks => Frames.Count;
        public List<FrameData> Frames = new List<FrameData>();

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
                if (frame.HasHitbox())
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly FrameType[] ATTACK_FRAME_TYPE_ORDER =
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
