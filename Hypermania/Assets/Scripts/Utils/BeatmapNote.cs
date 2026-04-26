using System;
using MemoryPack;

namespace Utils
{
    [MemoryPackable]
    [Serializable]
    public partial struct BeatmapNote
    {
        public Frame Tick;
        public int Channel;
    }
}
