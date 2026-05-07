using System;
using System.Collections.Generic;

namespace Utils.EnumArray
{
    public static class EnumIndexCache<TEnum>
        where TEnum : unmanaged, Enum
    {
        public static readonly TEnum[] Keys; // dense index -> enum value
        public static readonly string[] Names; // dense index -> enum name
        public static readonly Dictionary<TEnum, int> Map; // enum value -> dense index
        public static readonly int Count;

        static EnumIndexCache()
        {
            Array raw = Enum.GetValues(typeof(TEnum));
            var list = new List<(TEnum key, ulong u)>(raw.Length);

            foreach (var o in raw)
            {
                var k = (TEnum)o;
                list.Add((k, ToUInt64(k)));
            }

            list.Sort((a, b) => a.u.CompareTo(b.u));

            Count = list.Count;
            Keys = new TEnum[Count];
            Names = new string[Count];
            Map = new Dictionary<TEnum, int>(Count);

            for (int i = 0; i < Count; i++)
            {
                Keys[i] = list[i].key;
                Names[i] = Enum.GetName(typeof(TEnum), Keys[i]) ?? Keys[i].ToString();
                Map[Keys[i]] = i;
            }
        }

        public static int ToIndex(TEnum key)
        {
            if (!Map.TryGetValue(key, out int idx))
                throw new ArgumentOutOfRangeException(nameof(key), key, $"Unknown enum value for {typeof(TEnum).Name}");
            return idx;
        }

        private static ulong ToUInt64(TEnum value)
        {
            Type ut = Enum.GetUnderlyingType(typeof(TEnum));
            object boxed = value;

            if (ut == typeof(byte))
                return (byte)boxed;
            if (ut == typeof(sbyte))
                return unchecked((ulong)(sbyte)boxed);
            if (ut == typeof(short))
                return unchecked((ulong)(short)boxed);
            if (ut == typeof(ushort))
                return (ushort)boxed;
            if (ut == typeof(int))
                return unchecked((ulong)(int)boxed);
            if (ut == typeof(uint))
                return (uint)boxed;
            if (ut == typeof(long))
                return unchecked((ulong)(long)boxed);
            if (ut == typeof(ulong))
                return (ulong)boxed;

            return unchecked((ulong)Convert.ToInt64(boxed));
        }
    }
}
