using System.IO;
using Game.Sim;
using UnityEditor;
using UnityEngine;
using Utils;

namespace Design.Configs.Editor
{
    [CustomEditor(typeof(AudioConfig))]
    public sealed class AudioConfigEditor : UnityEditor.Editor
    {
        private string _cachedOszPath;
        private string[] _cachedDifficulties;
        private string _cachedError;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            var config = (AudioConfig)target;

            string oszPath = config.OszFile != null ? AssetDatabase.GetAssetPath(config.OszFile) : null;
            RefreshDifficultyCacheIfNeeded(oszPath);

            if (_cachedError != null)
            {
                EditorGUILayout.HelpBox(_cachedError, MessageType.Warning);
            }

            if (_cachedDifficulties != null && _cachedDifficulties.Length > 0)
            {
                DrawDifficultyPopup(config, ManiaDifficulty.Normal);
                DrawDifficultyPopup(config, ManiaDifficulty.Hard);
            }

            using (new EditorGUI.DisabledScope(config.OszFile == null || _cachedDifficulties == null))
            {
                if (GUILayout.Button("Generate Notes from Beatmap"))
                {
                    GenerateNotes(config, oszPath);
                }
            }
        }

        private void DrawDifficultyPopup(AudioConfig config, ManiaDifficulty difficulty)
        {
            string currentName = GetSlotName(config, difficulty);
            int currentIdx = System.Array.IndexOf(_cachedDifficulties, currentName);
            if (currentIdx < 0)
                currentIdx = 0;

            int newIdx = EditorGUILayout.Popup(difficulty.ToString(), currentIdx, _cachedDifficulties);
            string newName = _cachedDifficulties[newIdx];
            if (newName != currentName)
            {
                Undo.RecordObject(config, $"Select {difficulty} Beatmap Difficulty");
                SetSlotName(config, difficulty, newName);
                EditorUtility.SetDirty(config);
            }
        }

        private static string GetSlotName(AudioConfig config, ManiaDifficulty difficulty) =>
            difficulty == ManiaDifficulty.Hard ? config.Hard.DifficultyName : config.Normal.DifficultyName;

        private static void SetSlotName(AudioConfig config, ManiaDifficulty difficulty, string name)
        {
            if (difficulty == ManiaDifficulty.Hard)
                config.Hard.DifficultyName = name;
            else
                config.Normal.DifficultyName = name;
        }

        private void RefreshDifficultyCacheIfNeeded(string oszPath)
        {
            if (oszPath == _cachedOszPath)
                return;

            _cachedOszPath = oszPath;
            _cachedDifficulties = null;
            _cachedError = null;

            if (string.IsNullOrEmpty(oszPath) || !File.Exists(oszPath))
                return;

            try
            {
                byte[] data = File.ReadAllBytes(oszPath);
                _cachedDifficulties = OszParser.ListDifficulties(data);
            }
            catch (System.Exception ex)
            {
                _cachedError = $"Could not read .osz: {ex.Message}";
            }
        }

        private void GenerateNotes(AudioConfig config, string oszPath)
        {
            if (string.IsNullOrEmpty(oszPath) || !File.Exists(oszPath))
            {
                EditorUtility.DisplayDialog("Beatmap Error", $"Cannot read file at '{oszPath}'.", "OK");
                return;
            }

            if (
                string.IsNullOrWhiteSpace(config.Normal.DifficultyName)
                || string.IsNullOrWhiteSpace(config.Hard.DifficultyName)
            )
            {
                EditorUtility.DisplayDialog(
                    "Beatmap Error",
                    "Select a difficulty for both Normal and Hard slots first.",
                    "OK"
                );
                return;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(oszPath);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Beatmap Read Error", ex.Message, "OK");
                return;
            }

            BeatmapNote[] normalNotes, hardNotes;
            try
            {
                normalNotes = OszParser.ParseToNotes(data, config.Normal.DifficultyName);
                hardNotes = OszParser.ParseToNotes(data, config.Hard.DifficultyName);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Beatmap Parse Error", ex.Message, "OK");
                return;
            }

            Undo.RecordObject(config, "Generate Notes from Beatmap");
            config.Normal = new BeatmapDifficulty
            {
                DifficultyName = config.Normal.DifficultyName,
                Notes = normalNotes,
            };
            config.Hard = new BeatmapDifficulty
            {
                DifficultyName = config.Hard.DifficultyName,
                Notes = hardNotes,
            };
            EditorUtility.SetDirty(config);

            Debug.Log(
                $"AudioConfig: Generated {normalNotes.Length} Normal notes ('{config.Normal.DifficultyName}') "
                    + $"and {hardNotes.Length} Hard notes ('{config.Hard.DifficultyName}') from beatmap."
            );
        }
    }
}
