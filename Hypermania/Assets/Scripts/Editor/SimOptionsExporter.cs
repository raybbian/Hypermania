using System;
using System.IO;
using Game.Sim;
using Game.Sim.Configs;
using MemoryPack;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    // Authoring tool: drop in a GlobalStats + two CharacterStats and write a
    // SimOptions binary the headless trainer can MemoryPack-deserialize. The
    // sim types are all [MemoryPackable] - no DTO mirror, just direct
    // serialization of the same SimOptions the runtime uses.
    public sealed class SimOptionsExporter : EditorWindow
    {
        const string DefaultRelOutputDir = "Hypermania.CPU/snapshots";

        [SerializeField] GlobalStats _global;
        [SerializeField] CharacterStats _p1;
        [SerializeField] CharacterStats _p2;
        [SerializeField] bool _alwaysRhythmCancel = false;

        [MenuItem("Hypermania/Export Sim Options Preset...")]
        public static void Open()
        {
            GetWindow<SimOptionsExporter>(utility: true, title: "Sim Options Preset", focus: true);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source assets", EditorStyles.boldLabel);
            _global = (GlobalStats)EditorGUILayout.ObjectField("Global Stats", _global, typeof(GlobalStats), false);
            _p1 = (CharacterStats)EditorGUILayout.ObjectField("P1 Character", _p1, typeof(CharacterStats), false);
            _p2 = (CharacterStats)EditorGUILayout.ObjectField("P2 Character", _p2, typeof(CharacterStats), false);
            _alwaysRhythmCancel = EditorGUILayout.Toggle("Always Rhythm Cancel", _alwaysRhythmCancel);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_global == null || _p1 == null || _p2 == null))
            {
                if (GUILayout.Button("Export to .bin..."))
                    DoExport();
            }
        }

        void DoExport()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string defaultDir = Path.Combine(projectRoot, DefaultRelOutputDir);
            Directory.CreateDirectory(defaultDir);
            string defaultName = $"sim_{_p1.Character}_vs_{_p2.Character}.bin";
            string path = EditorUtility.SaveFilePanel(
                "Export Sim Options Preset", defaultDir, defaultName, "bin"
            );
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                SimOptions sim = Build(_global, _p1, _p2, _alwaysRhythmCancel);
                byte[] bytes = MemoryPackSerializer.Serialize(sim);
                File.WriteAllBytes(path, bytes);
                Debug.Log($"SimOptionsExporter: wrote {bytes.Length} bytes to {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SimOptionsExporter: {ex}");
                EditorUtility.DisplayDialog("Export failed", ex.Message, "OK");
            }
        }

        static SimOptions Build(GlobalStats g, CharacterStats p1, CharacterStats p2, bool alwaysRhythmCancel)
        {
            return new SimOptions
            {
                Global = g,
                AlwaysRhythmCancel = alwaysRhythmCancel,
                InfoOptions = new InfoOptions(),
                Players = new[]
                {
                    new PlayerSimOptions { Character = p1, ComboMode = ComboMode.Freestyle },
                    new PlayerSimOptions { Character = p2, ComboMode = ComboMode.Freestyle },
                },
            };
        }
    }
}
