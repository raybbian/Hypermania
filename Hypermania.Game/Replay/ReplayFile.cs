using System;
using System.IO;
using System.IO.Compression;
using Hypermania.Game.Configs;
using Hypermania.Shared;
using MemoryPack;

namespace Hypermania.Game.Replay
{
    // .hmrep replay payload. Carries enough to deterministically replay an
    // input stream, plus pinned presentation hints (skins, stage). The sim
    // graph is split: non-stats fields ride inside SimOptionsBlob, while
    // stats (GlobalStats + per-player CharacterStats) are NOT serialized -
    // we hash them instead and let the loader reattach the editor's live
    // SOs after verifying the hash matches. This keeps the replay file
    // independent of balance edits while still detecting drift.
    [MemoryPackable]
    public partial class ReplayFile
    {
        public const byte CurrentVersion = 4;

        public byte Version;
        public ulong SchemaHash;
        
        public Character P1Character;
        public Character P2Character;
        public int P1Skin;
        public int P2Skin;
        public Stage Stage;

        public int[] P1Inputs; // (int)InputFlags per tick
        public int[] P2Inputs; // same length as P1Inputs
        public string Source;

        // FNV-1a 64 over MemoryPack-serialized GlobalStats || P1.CharacterStats
        // || P2.CharacterStats. Both CharacterStats and the HitboxData SOs it
        // references are MemoryPackable, so this hash transitively covers move
        // and hitbox content too. The loader recomputes from the editor's live
        // SOs and warns on mismatch.
        public ulong StatsHash;

        // MemoryPack-serialized SimOptions with Global = null and each
        // Players[i].Character = null. The loader deserializes this and then
        // reattaches the live stats SOs from its registry.
        public byte[] SimOptionsBlob;

        // Who occupied each slot. Informational - playback ignores these.
        public string P1Player;
        public string P2Player;

        // DateTimeOffset.UtcNow.ToUnixTimeSeconds() at save.
        public long RecordedAtUnix;

        // Equals P1Inputs.Length, but stored explicitly so a reader can pull
        // match duration without scanning the full input array.
        public int MatchLengthTicks;

        public static ReplayFile Build(
            ulong schemaHash,
            Character p1Char,
            Character p2Char,
            int p1Skin,
            int p2Skin,
            Stage stage,
            int[] p1Inputs,
            int[] p2Inputs,
            string source,
            SimOptions simOptions,
            string p1Player,
            string p2Player
        )
        {
            if (p1Inputs == null)
                throw new ArgumentNullException(nameof(p1Inputs));
            if (p2Inputs == null)
                throw new ArgumentNullException(nameof(p2Inputs));
            if (simOptions == null)
                throw new ArgumentNullException(nameof(simOptions));
            if (p1Inputs.Length != p2Inputs.Length)
                throw new ArgumentException("input lengths must match");
            if (simOptions.Players == null || simOptions.Players.Length != 2)
                throw new ArgumentException("simOptions must have 2 players");
            if (simOptions.Global == null)
                throw new ArgumentException("simOptions.Global is null; can't hash stats");
            if (simOptions.Players[0].Character == null || simOptions.Players[1].Character == null)
                throw new ArgumentException("simOptions player characters are null; can't hash stats");

            ulong statsHash = ComputeStatsHash(
                simOptions.Global,
                simOptions.Players[0].Character,
                simOptions.Players[1].Character
            );

            SimOptions stripped = StripStats(simOptions);
            byte[] blob = MemoryPackSerializer.Serialize(stripped);

            return new ReplayFile
            {
                Version = CurrentVersion,
                SchemaHash = schemaHash,
                P1Character = p1Char,
                P2Character = p2Char,
                P1Skin = p1Skin,
                P2Skin = p2Skin,
                Stage = stage,
                P1Inputs = p1Inputs,
                P2Inputs = p2Inputs,
                Source = source ?? string.Empty,
                StatsHash = statsHash,
                SimOptionsBlob = blob,
                P1Player = p1Player,
                P2Player = p2Player,
                RecordedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                MatchLengthTicks = p1Inputs.Length,
            };
        }

        // Returns the stripped SimOptions (Global / Players[i].Character all
        // null), or null if the replay didn't carry one. Callers are expected
        // to reattach stats from their own registry before using the result.
        public SimOptions DeserializeSimOptions()
        {
            if (SimOptionsBlob == null || SimOptionsBlob.Length == 0)
                return null;
            return MemoryPackSerializer.Deserialize<SimOptions>(SimOptionsBlob);
        }

        // Stable content hash over the stats SOs. Loader uses this to detect
        // balance edits between record and playback.
        public static ulong ComputeStatsHash(GlobalStats global, CharacterStats p1, CharacterStats p2)
        {
            if (global == null)
                throw new ArgumentNullException(nameof(global));
            if (p1 == null)
                throw new ArgumentNullException(nameof(p1));
            if (p2 == null)
                throw new ArgumentNullException(nameof(p2));

            ulong hash = Fnv1aOffset;
            hash = FoldBytes(hash, MemoryPackSerializer.Serialize(global));
            hash = FoldBytes(hash, MemoryPackSerializer.Serialize(p1));
            hash = FoldBytes(hash, MemoryPackSerializer.Serialize(p2));
            return hash;
        }

        static SimOptions StripStats(SimOptions src)
        {
            SimOptions result = new SimOptions
            {
                Global = null,
                InfoOptions = src.InfoOptions,
                AlwaysRhythmCancel = src.AlwaysRhythmCancel,
                Players = new PlayerSimOptions[src.Players.Length],
            };
            for (int i = 0; i < src.Players.Length; i++)
            {
                PlayerSimOptions p = src.Players[i];
                result.Players[i] = new PlayerSimOptions
                {
                    HealOnActionable = p.HealOnActionable,
                    SuperMaxOnActionable = p.SuperMaxOnActionable,
                    BurstMaxOnActionable = p.BurstMaxOnActionable,
                    Immortal = p.Immortal,
                    Character = null,
                    Hitboxes = null,
                    ComboMode = p.ComboMode,
                    ManiaDifficulty = p.ManiaDifficulty,
                    SuperInputMode = p.SuperInputMode,
                };
            }
            return result;
        }

        const ulong Fnv1aOffset = 14695981039346656037UL;
        const ulong Fnv1aPrime = 1099511628211UL;

        static ulong FoldBytes(ulong hash, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Fnv1aPrime;
            }
            return hash;
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
