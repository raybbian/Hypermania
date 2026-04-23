using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Utils;
using Utils.SoftFloat;

namespace Design.Configs.Editor
{
    /// <summary>
    /// Editor-only parser for osu!mania .osz archives. Lists the difficulties
    /// (one .osu file per difficulty) inside the archive and converts a chosen
    /// difficulty's HitObjects into (frame, channel) pairs at a target framerate.
    /// </summary>
    public static class OszParser
    {
        /// <summary>
        /// Returns the difficulty names found in the .osz, in archive order.
        /// Names come from each .osu's [Metadata] Version field, falling back
        /// to the entry filename (without .osu extension). Only mania (Mode=3)
        /// difficulties are returned.
        /// </summary>
        public static string[] ListDifficulties(byte[] oszData)
        {
            var names = new List<string>();
            using var stream = new MemoryStream(oszData);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = ReadEntryText(entry);
                Dictionary<string, Dictionary<string, string>> sections = ParseSections(text);

                if (!IsMania(sections))
                    continue;

                names.Add(GetDifficultyName(sections, entry.FullName));
            }

            if (names.Count == 0)
                throw new InvalidOperationException("No osu!mania (Mode=3) difficulties found in .osz archive.");

            return names.ToArray();
        }

        /// <summary>
        /// Parse a single difficulty's HitObjects into (frame, channel) pairs.
        /// channel is computed from the standard osu!mania mapping
        /// <c>floor(x * columnCount / 512)</c> using CircleSize as the column
        /// count. Hold notes (type bit 7) are emitted as a single tap at the
        /// start time. Notes are returned sorted by frame ascending.
        /// </summary>
        public static BeatmapNote[] ParseToNotes(byte[] oszData, string difficultyName, int fps = 60)
        {
            using var stream = new MemoryStream(oszData);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var available = new List<string>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = ReadEntryText(entry);
                Dictionary<string, Dictionary<string, string>> sections = ParseSections(text);

                if (!IsMania(sections))
                    continue;

                string name = GetDifficultyName(sections, entry.FullName);
                available.Add(name);

                if (name != difficultyName)
                    continue;

                int columnCount = ParseIntOrDefault(GetField(sections, "Difficulty", "CircleSize"), 4);
                if (columnCount <= 0)
                    columnCount = 4;

                return ParseHitObjects(text, columnCount, fps);
            }

            throw new InvalidOperationException(
                $"Difficulty '{difficultyName}' not found in .osz. Available: [{string.Join(", ", available)}]"
            );
        }

        // ------------------------------------------------------------------
        // .osu text parsing
        // ------------------------------------------------------------------

        private static BeatmapNote[] ParseHitObjects(string osuText, int columnCount, int fps)
        {
            var notes = new List<BeatmapNote>();
            bool inHitObjects = false;

            using var reader = new StringReader(osuText);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inHitObjects = string.Equals(trimmed, "[HitObjects]", StringComparison.Ordinal);
                    continue;
                }

                if (!inHitObjects)
                    continue;

                // x,y,time,type,hitSound,objectParams,hitSample
                string[] parts = trimmed.Split(',');
                if (parts.Length < 4)
                    continue;
                if (!int.TryParse(parts[0], out int x))
                    continue;
                if (!int.TryParse(parts[2], out int timeMs))
                    continue;

                int channel = (x * columnCount) / 512;
                if (channel < 0)
                    channel = 0;
                else if (channel >= columnCount)
                    channel = columnCount - 1;

                int frame = (int)Mathsf.Round((sfloat)timeMs * (sfloat)fps / (sfloat)1000.0);

                notes.Add(new BeatmapNote { Tick = frame, Channel = channel });
            }

            // SliceFrom's binary search requires ascending Tick order.
            notes.Sort((a, b) => a.Tick.No.CompareTo(b.Tick.No));
            return notes.ToArray();
        }

        private static Dictionary<string, Dictionary<string, string>> ParseSections(string text)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Dictionary<string, string> current = null;

            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    string section = trimmed.Substring(1, trimmed.Length - 2);
                    if (section == "HitObjects" || section == "TimingPoints" || section == "Events")
                    {
                        // List sections — skip key/value parsing.
                        current = null;
                    }
                    else
                    {
                        current = new Dictionary<string, string>(StringComparer.Ordinal);
                        sections[section] = current;
                    }
                    continue;
                }

                if (current == null)
                    continue;

                int colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    continue;
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                current[key] = value;
            }

            return sections;
        }

        private static bool IsMania(Dictionary<string, Dictionary<string, string>> sections)
        {
            return ParseIntOrDefault(GetField(sections, "General", "Mode"), 0) == 3;
        }

        private static string GetDifficultyName(
            Dictionary<string, Dictionary<string, string>> sections,
            string entryName
        )
        {
            string version = GetField(sections, "Metadata", "Version");
            if (!string.IsNullOrWhiteSpace(version))
                return version;

            string fileName = Path.GetFileNameWithoutExtension(entryName);
            return string.IsNullOrEmpty(fileName) ? entryName : fileName;
        }

        private static string GetField(
            Dictionary<string, Dictionary<string, string>> sections,
            string section,
            string key
        )
        {
            return sections.TryGetValue(section, out var dict) && dict.TryGetValue(key, out var value) ? value : null;
        }

        private static int ParseIntOrDefault(string s, int fallback)
        {
            return int.TryParse(s, out int v) ? v : fallback;
        }

        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
