using System;
using System.IO;
using System.IO.Compression;
using MemoryPack;

namespace Game.Sim.Replay
{
    // .hmrep replay payload. Carries enough to deterministically replay an
    // input stream against a SimOptions the caller resolves out of band.
    //
    // SimOptions is not bundled. GlobalStats and CharacterStats are Unity
    // ScriptableObjects, not MemoryPack-serializable - the caller (Unity
    // ReplayRunner / CPU `play` cmd) owns SimOptions construction. We bake
    // P1Character / P2Character so the loader can pick the right per-player
    // stats from its registry, plus a SchemaHash to flag drift.
    [MemoryPackable]
    public partial class ReplayFile
    {
        public const byte CurrentVersion = 1;

        public byte Version;
        public ulong SchemaHash;
        public Character P1Character;
        public Character P2Character;
        public int[] P1Inputs; // (int)InputFlags per tick
        public int[] P2Inputs; // same length as P1Inputs
        public string Source;

        public static ReplayFile Build(
            ulong schemaHash,
            Character p1Char,
            Character p2Char,
            int[] p1Inputs,
            int[] p2Inputs,
            string source
        )
        {
            if (p1Inputs == null) throw new ArgumentNullException(nameof(p1Inputs));
            if (p2Inputs == null) throw new ArgumentNullException(nameof(p2Inputs));
            if (p1Inputs.Length != p2Inputs.Length)
                throw new ArgumentException("input lengths must match");
            return new ReplayFile
            {
                Version = CurrentVersion,
                SchemaHash = schemaHash,
                P1Character = p1Char,
                P2Character = p2Char,
                P1Inputs = p1Inputs,
                P2Inputs = p2Inputs,
                Source = source ?? string.Empty,
            };
        }

        public static void Save(string path, ReplayFile r)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            using FileStream fs = File.Create(path);
            using GZipStream gz = new GZipStream(fs, CompressionLevel.Fastest);
            byte[] bytes = MemoryPackSerializer.Serialize(r);
            gz.Write(bytes, 0, bytes.Length);
        }

        public static ReplayFile Load(string path)
        {
            using FileStream fs = File.OpenRead(path);
            using GZipStream gz = new GZipStream(fs, CompressionMode.Decompress);
            using MemoryStream ms = new MemoryStream();
            gz.CopyTo(ms);
            return MemoryPackSerializer.Deserialize<ReplayFile>(ms.ToArray());
        }
    }
}