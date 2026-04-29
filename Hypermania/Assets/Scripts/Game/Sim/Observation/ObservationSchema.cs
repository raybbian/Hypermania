using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Game.Sim.Observation
{
    public readonly struct ObservationField
    {
        public readonly string Path;
        public readonly Type Type;
        public readonly string Category;

        public ObservationField(string path, Type type, string category)
        {
            Path = path;
            Type = type;
            Category = category;
        }
    }

    public static class ObservationSchema
    {
        const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly ConcurrentDictionary<Type, ObservationField[]> _cache = new();
        static readonly ConcurrentDictionary<Type, ulong> _hashCache = new();

        // Returns a flat, ordered list of observable leaf fields for `t`.
        // Nested struct/class types are recursed into; primitives, enums,
        // strings, and arrays are treated as leaves. Members marked
        // [NonObservable] are skipped.
        public static ObservationField[] For(Type t)
        {
            return _cache.GetOrAdd(t, type =>
            {
                List<ObservationField> result = new();
                Walk(type, "", null, result, new HashSet<Type>());
                return result.ToArray();
            });
        }

        // Stable hash of the schema (FNV-1a 64) over field path + type name.
        // Used by PolicyCheckpoint to detect schema drift between training
        // and inference builds.
        public static ulong Hash(Type t)
        {
            return _hashCache.GetOrAdd(t, type =>
            {
                StringBuilder sb = new();
                foreach (ObservationField f in For(type))
                {
                    sb.Append(f.Path);
                    sb.Append(':');
                    sb.Append(f.Type.FullName);
                    sb.Append(';');
                }
                return Fnv1a64(sb.ToString());
            });
        }

        static void Walk(
            Type type,
            string prefix,
            string inheritedCategory,
            List<ObservationField> sink,
            HashSet<Type> visiting
        )
        {
            if (!visiting.Add(type))
                return;

            foreach (FieldInfo field in type.GetFields(MemberFlags))
            {
                if (field.IsStatic || field.IsDefined(typeof(NonObservableAttribute), false))
                    continue;

                string category = ResolveCategory(field, inheritedCategory);
                string path = string.IsNullOrEmpty(prefix) ? field.Name : prefix + "." + field.Name;
                AddOrRecurse(field.FieldType, path, category, sink, visiting);
            }

            foreach (PropertyInfo prop in type.GetProperties(MemberFlags))
            {
                if (
                    !prop.CanRead
                    || prop.GetIndexParameters().Length != 0
                    || prop.IsDefined(typeof(NonObservableAttribute), false)
                )
                    continue;

                string category = ResolveCategory(prop, inheritedCategory);
                string path = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                AddOrRecurse(prop.PropertyType, path, category, sink, visiting);
            }

            visiting.Remove(type);
        }

        static void AddOrRecurse(
            Type memberType,
            string path,
            string category,
            List<ObservationField> sink,
            HashSet<Type> visiting
        )
        {
            Type unwrapped = Nullable.GetUnderlyingType(memberType) ?? memberType;
            if (IsLeaf(unwrapped))
            {
                sink.Add(new ObservationField(path, memberType, category));
                return;
            }
            Walk(unwrapped, path, category, sink, visiting);
        }

        static bool IsLeaf(Type t)
        {
            if (t.IsPrimitive || t.IsEnum)
                return true;
            if (t == typeof(string) || t == typeof(decimal))
                return true;
            if (t.IsArray)
                return true;
            if (t.IsGenericType)
                return true;
            return false;
        }

        static string ResolveCategory(MemberInfo m, string inherited)
        {
            ObservationFieldAttribute attr = m.GetCustomAttribute<ObservationFieldAttribute>(false);
            return attr?.Category ?? inherited;
        }

        static ulong Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= prime;
            }
            return hash;
        }
    }
}
