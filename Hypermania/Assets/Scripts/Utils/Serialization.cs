using System;

namespace Utils
{
    public interface ISerializable
    {
        public int SerdeSize();
        public int Serialize(Span<byte> outBytes);
        public int Deserialize(ReadOnlySpan<byte> inBytes);
    }

    public static class Serializer<T>
        where T : ISerializable
    {
        private static readonly T SAMPLE = default;

        public static int DefaultSize()
        {
            return SAMPLE.SerdeSize();
        }
    }
}
