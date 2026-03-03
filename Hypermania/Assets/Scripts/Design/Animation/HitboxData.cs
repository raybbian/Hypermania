using System;
using System.Collections.Generic;
using UnityEngine;
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
    public struct BoxProps : IEquatable<BoxProps>
    {
        // NOTE: ensure that any new fields added above are added to the equals and hashcode implementation!!!
        public HitboxKind Kind;
        public AttackKind AttackKind;
        public int Damage;
        public int HitstunTicks;
        public int BlockstunTicks;
        public int HitstopTicks;
        public bool StartsRhythmCombo;
        public SVector2 Knockback;

        public bool Equals(BoxProps other) =>
            Kind == other.Kind
            && AttackKind == other.AttackKind
            && HitstunTicks == other.HitstunTicks
            && Damage == other.Damage
            && BlockstunTicks == other.BlockstunTicks
            && Knockback == other.Knockback
            && StartsRhythmCombo == other.StartsRhythmCombo
            && HitstopTicks == other.HitstopTicks;

        public override bool Equals(object obj) => obj is BoxProps other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                Kind,
                AttackKind,
                HitstunTicks,
                Damage,
                BlockstunTicks,
                StartsRhythmCombo,
                Knockback,
                HitstopTicks
            );

        public static bool operator ==(BoxProps a, BoxProps b) => a.Equals(b);

        public static bool operator !=(BoxProps a, BoxProps b) => !a.Equals(b);
    }

    [Serializable]
    public struct BoxData : IEquatable<BoxData>
    {
        public string Name;

        public SVector2 CenterLocal;
        public SVector2 SizeLocal;
        public BoxProps Props;

        public bool Equals(BoxData other)
        {
            return Name == other.Name
                && CenterLocal == other.CenterLocal
                && SizeLocal == other.SizeLocal
                && Props.Equals(other.Props);
        }

        public override bool Equals(object obj) => obj is BoxData other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, CenterLocal, SizeLocal, Props);
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
    }

    [Serializable]
    public class FrameData
    {
        public List<BoxData> Boxes = new List<BoxData>();
        public FrameType FrameType = FrameType.Neutral;

        public FrameData Clone()
        {
            var copy = new FrameData();

            if (Boxes != null)
                copy.Boxes = new List<BoxData>(Boxes);
            else
                copy.Boxes = new List<BoxData>();

            copy.FrameType = FrameType;
            return copy;
        }

        public void CopyFrom(FrameData other)
        {
            Boxes.Clear();
            if (other?.Boxes != null)
                Boxes.AddRange(other.Boxes);
            FrameType = other.FrameType;
        }

        public override int GetHashCode()
        {
            var hc = new HashCode();
            hc.Add(Boxes != null ? Boxes.Count : 0);
            for (int j = 0; j < Boxes.Count; j++)
            {
                hc.Add(Boxes[j]);
            }
            hc.Add(FrameType);
            return hc.ToHashCode();
        }
    }

    [CreateAssetMenu(menuName = "Hypermania/Move Data")]
    [Serializable]
    public class HitboxData : ScriptableObject
    {
        public AnimationClip Clip;
        public int TotalTicks => Frames.Count;
        public List<FrameData> Frames = new List<FrameData>();

        public void EnsureSize(int totalTicks)
        {
            if (totalTicks < 1)
            {
                throw new InvalidOperationException("total ticks must be >= 1");
            }
            while (Frames.Count < totalTicks)
                Frames.Add(new FrameData());
            while (Frames.Count > totalTicks)
                Frames.RemoveAt(Frames.Count - 1);
        }

        public FrameData GetFrame(int tick)
        {
            if (Frames == null || Frames.Count == 0)
                return null;
            tick = Mathf.Clamp(tick, 0, TotalTicks - 1);
            return Frames[tick];
        }

        public override int GetHashCode()
        {
            var hc = new HashCode();

            hc.Add(Clip ? Clip.GetInstanceID() : 0);
            hc.Add(Frames != null ? Frames.Count : 0);

            if (Frames != null)
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    hc.Add(Frames[i]);
                }
            }

            return hc.ToHashCode();
        }
    }
}
