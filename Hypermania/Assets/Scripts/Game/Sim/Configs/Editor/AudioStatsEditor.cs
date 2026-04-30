using System.IO;
using Game.Sim;
using UnityEditor;
using UnityEngine;
using Utils;

namespace Game.Sim.Configs.Editor
{
    [CustomEditor(typeof(AudioStats))]
    public sealed class AudioStatsEditor : UnityEditor.Editor
    {
        // Transient editor-only ref; not persisted. Re-pick when you want to import.
        private Object _oszFile;
        private string _cachedOszPath;
        private string[] _cachedDifficulties;
        private string _cachedError;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            var stats = (AudioStats)target;

            _oszFile = EditorGUILayout.ObjectField(".osz Source", _oszFile, typeof(Object), false);

            string oszPath = _oszFile != null ? AssetDatabase.GetAssetPath(_oszFile) : null;
            RefreshDifficultyCacheIfNeeded(oszPath);

            if (_cachedError != null)
            {
                EditorGUILayout.HelpBox(_cachedError, MessageType.Warning);
            }

            if (_cachedDifficulties != null && _cachedDifficulties.Length > 0)
            {
                DrawDifficultyPopup(stats, ManiaDifficulty.Normal);
                DrawDifficultyPopup(stats, ManiaDifficulty.Hard);
            }

            using (new EditorGUI.DisabledScope(_oszFile == null || _cachedDifficulties == null))
            {
                if (GUILayout.Button("Generate Notes from Beatmap"))
                {
                    GenerateNotes(stats, oszPath);
                }
            }
        }

        private void DrawDifficultyPopup(AudioStats stats, ManiaDifficulty difficulty)
        {
            string currentName = GetSlotName(stats, difficulty);
            int currentIdx = System.Array.IndexOf(_cachedDifficulties, currentName);
            if (currentIdx < 0)
                currentIdx = 0;

            int newIdx = EditorGUILayout.Popup(difficulty.ToString(), currentIdx, _cachedDifficulties);
            string newName = _cachedDifficulties[newIdx];
            if (newName != currentName)
            {
                Undo.RecordObject(stats, $"Select {difficulty} Beatmap Difficulty");
                SetSlotName(stats, difficulty, newName);
                EditorUtility.SetDirty(stats);
            }
        }

        private static string GetSlotName(AudioStats stats, ManiaDifficulty difficulty) =>
            difficulty == ManiaDifficulty.Hard ? stats.Hard.DifficultyName : stats.Normal.DifficultyName;

        private static void SetSlotName(AudioStats stats, ManiaDifficulty difficulty, string name)
        {
            if (difficulty == ManiaDifficulty.Hard)
                stats.Hard.DifficultyName = name;
            else
                stats.Normal.DifficultyName = name;
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

        private void GenerateNotes(AudioStats stats, string oszPath)
        {
            if (string.IsNullOrEmpty(oszPath) || !File.Exists(oszPath))
            {
                EditorUtility.DisplayDialog("Beatmap Error", $"Cannot read file at '{oszPath}'.", "OK");
                return;
            }

            if (
                string.IsNullOrWhiteSpace(stats.Normal.DifficultyName)
                || string.IsNullOrWhiteSpace(stats.Hard.DifficultyName)
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

            BeatmapNote[] normalNotes,
                hardNotes;
            try
            {
                normalNotes = OszParser.ParseToNotes(data, stats.Normal.DifficultyName);
                hardNotes = OszParser.ParseToNotes(data, stats.Hard.DifficultyName);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Beatmap Parse Error", ex.Message, "OK");
                return;
            }

            Undo.RecordObject(stats, "Generate Notes from Beatmap");
            stats.Normal = new BeatmapDifficulty { DifficultyName = stats.Normal.DifficultyName, Notes = normalNotes };
            stats.Hard = new BeatmapDifficulty { DifficultyName = stats.Hard.DifficultyName, Notes = hardNotes };
            EditorUtility.SetDirty(stats);

            Debug.Log(
                $"AudioStats: Generated {normalNotes.Length} Normal notes ('{stats.Normal.DifficultyName}') "
                    + $"and {hardNotes.Length} Hard notes ('{stats.Hard.DifficultyName}') from beatmap."
            );
        }
    }
}
