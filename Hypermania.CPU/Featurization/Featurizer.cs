using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Sim;
using Game.Sim.Observation;
using Utils.SoftFloat;

namespace Hypermania.CPU.Featurization
{
    // Turns a (GameState, fighterIndex) pair into a flat float vector by
    // walking ObservationSchema. The actual normalization / encoding is left
    // to whichever ML library lands later - this is the contract layer.
    public static class Featurizer
    {
        public static int Length => ObservationSchema.For(typeof(GameState)).Length;

        public static ulong SchemaHash => ObservationSchema.Hash(typeof(GameState));

        public static void Encode(in GameState state, int fighterIndex, Span<float> dst)
        {
            ObservationField[] schema = ObservationSchema.For(typeof(GameState));
            if (dst.Length < schema.Length)
                throw new ArgumentException(
                    $"destination too short: need {schema.Length}, got {dst.Length}",
                    nameof(dst)
                );

            for (int i = 0; i < schema.Length; i++)
            {
                dst[i] = ToFloat(ResolveValue(state, schema[i].Path));
            }
        }

        static object ResolveValue(object root, string path)
        {
            object cur = root;
            foreach (string segment in path.Split('.'))
            {
                if (cur == null)
                    return null;
                Type t = cur.GetType();
                MemberInfo m = (MemberInfo)t.GetField(
                    segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                ) ?? t.GetProperty(
                    segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (m == null)
                    return null;
                cur = m is FieldInfo fi ? fi.GetValue(cur) : ((PropertyInfo)m).GetValue(cur);
            }
            return cur;
        }

        static float ToFloat(object v)
        {
            switch (v)
            {
                case null: return 0f;
                case sfloat sf: return (float)sf;
                case bool b: return b ? 1f : 0f;
                case int i: return i;
                case long l: return l;
                case float f: return f;
                case double d: return (float)d;
                case Enum e: return Convert.ToInt32(e);
                default: return 0f;
            }
        }
    }
}
