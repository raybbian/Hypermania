using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>
    /// Disk catalog of <see cref="ControlsProfile"/> records. One JSON file
    /// per profile under <see cref="ProfilesDirectory"/>, named by a
    /// filename-safe form of the profile name.
    /// </summary>
    public static class ControlsProfileStore
    {
        private const string ProfilesFolder = "ControlsProfiles";

        public static string ProfilesDirectory => Path.Combine(Application.persistentDataPath, ProfilesFolder);

        public static List<ControlsProfile> LoadAll()
        {
            List<ControlsProfile> list = new List<ControlsProfile>();
            string dir = ProfilesDirectory;
            if (!Directory.Exists(dir))
                return list;
            foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    ControlsProfile profile = JsonUtility.FromJson<ControlsProfile>(json);
                    if (profile == null || string.IsNullOrEmpty(profile.Name))
                        continue;
                    list.Add(profile);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ControlsProfileStore] Failed to load {path}: {ex.Message}");
                }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Load one profile by name, returning null when no matching file
        /// exists. Called at commit time so the game sees fresh bindings
        /// rather than a cached copy from the menu.
        /// </summary>
        public static ControlsProfile LoadByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            string path = Path.Combine(ProfilesDirectory, SafeFilename(name) + ".json");
            if (!File.Exists(path))
                return null;
            try
            {
                string json = File.ReadAllText(path);
                ControlsProfile profile = JsonUtility.FromJson<ControlsProfile>(json);
                return profile != null && !string.IsNullOrEmpty(profile.Name) ? profile : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ControlsProfileStore] Failed to load {path}: {ex.Message}");
                return null;
            }
        }

        public static void Save(ControlsProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Name))
                return;
            string dir = ProfilesDirectory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, SafeFilename(profile.Name) + ".json");
            string json = JsonUtility.ToJson(profile, prettyPrint: true);
            File.WriteAllText(path, json);
        }

        public static void Delete(ControlsProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Name))
                return;
            string path = Path.Combine(ProfilesDirectory, SafeFilename(profile.Name) + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }

        // One-word pool used by SuggestName. The menu never prompts the
        // user to type a name — it picks the next free fruit here.
        private static readonly string[] ProfileNamePool =
        {
            "Apple",
            "Apricot",
            "Avocado",
            "Banana",
            "Blackberry",
            "Blueberry",
            "Boysenberry",
            "Breadfruit",
            "Cantaloupe",
            "Cherimoya",
            "Cherry",
            "Clementine",
            "Coconut",
            "Cranberry",
            "Currant",
            "Date",
            "Dragonfruit",
            "Durian",
            "Elderberry",
            "Feijoa",
            "Fig",
            "Gooseberry",
            "Grape",
            "Grapefruit",
            "Guava",
            "Honeydew",
            "Jackfruit",
            "Jujube",
            "Kiwi",
            "Kumquat",
            "Lemon",
            "Lime",
            "Loganberry",
            "Longan",
            "Loquat",
            "Lychee",
            "Mandarin",
            "Mango",
            "Mangosteen",
            "Medlar",
            "Melon",
            "Mulberry",
            "Nectarine",
            "Olive",
            "Orange",
            "Papaya",
            "Passionfruit",
            "Peach",
            "Pear",
            "Persimmon",
            "Pineapple",
            "Plantain",
            "Plum",
            "Pomegranate",
            "Pomelo",
            "Prune",
            "Pumpkin",
            "Quince",
            "Raisin",
            "Rambutan",
            "Raspberry",
            "Redcurrant",
            "Rhubarb",
            "Salak",
            "Sapodilla",
            "Satsuma",
            "Soursop",
            "Starfruit",
            "Strawberry",
            "Tamarind",
            "Tangelo",
            "Tangerine",
            "Watermelon",
            "Yuzu",
        };

        public static string SuggestName(IEnumerable<ControlsProfile> existing)
        {
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (ControlsProfile p in existing)
                {
                    if (p != null && !string.IsNullOrEmpty(p.Name))
                        taken.Add(p.Name);
                }
            }
            for (int i = 0; i < ProfileNamePool.Length; i++)
            {
                if (!taken.Contains(ProfileNamePool[i]))
                    return ProfileNamePool[i];
            }
            return UniqueName(ProfileNamePool[ProfileNamePool.Length - 1], existing);
        }

        public static string UniqueName(string baseName, IEnumerable<ControlsProfile> existing)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "New Profile";
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (ControlsProfile p in existing)
                {
                    if (p != null && !string.IsNullOrEmpty(p.Name))
                        taken.Add(p.Name);
                }
            }
            if (!taken.Contains(baseName))
                return baseName;
            int n = 2;
            while (taken.Contains($"{baseName} {n}"))
                n++;
            return $"{baseName} {n}";
        }

        private static string SafeFilename(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
