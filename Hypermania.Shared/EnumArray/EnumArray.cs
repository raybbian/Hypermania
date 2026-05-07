using System;
using MemoryPack;

namespace Utils.EnumArray
{
    [Serializable]
    [MemoryPackable]
    public partial class EnumArray<TEnum, TValue>
        where TEnum : unmanaged, Enum
    {
        public TValue[] values;

        public TValue this[TEnum key]
        {
            get
            {
                EnsureSize();
                return values[EnumIndexCache<TEnum>.ToIndex(key)];
            }
            set
            {
                EnsureSize();
                values[EnumIndexCache<TEnum>.ToIndex(key)] = value;
            }
        }

        public TValue[] RawValues => values;

        private void EnsureSize()
        {
            int n = EnumIndexCache<TEnum>.Count;
            if (values == null || values.Length != n)
                Array.Resize(ref values, n);
        }
    }
}
