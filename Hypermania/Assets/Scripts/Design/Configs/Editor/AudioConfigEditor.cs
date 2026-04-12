using System.IO;
using UnityEditor;
using UnityEngine;
using Utils;

namespace Design.Configs.Editor
{
    [CustomEditor(typeof(AudioConfig))]
    public sealed class AudioConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            var config = (AudioConfig)target;

            if (GUILayout.Button("Generate Notes from MIDI"))
            {
                if (config.MidiFile == null)
                {
                    EditorUtility.DisplayDialog("MIDI Error", "Assign a MIDI file first.", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(config.MidiTrackName))
                {
                    EditorUtility.DisplayDialog("MIDI Error", "Enter a track name.", "OK");
                    return;
                }

                string path = AssetDatabase.GetAssetPath(config.MidiFile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    EditorUtility.DisplayDialog("MIDI Error", $"Cannot read file at '{path}'.", "OK");
                    return;
                }

                byte[] data = File.ReadAllBytes(path);
                int[] frames;
                try
                {
                    frames = MidiParser.ParseTrackToFrames(data, config.MidiTrackName);
                }
                catch (System.Exception ex)
                {
                    EditorUtility.DisplayDialog("MIDI Parse Error", ex.Message, "OK");
                    return;
                }

                Undo.RecordObject(config, "Generate Notes from MIDI");
                config.Notes = new Frame[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                    config.Notes[i] = frames[i]; // implicit int -> Frame
                EditorUtility.SetDirty(config);

                Debug.Log($"AudioConfig: Generated {frames.Length} notes from MIDI track '{config.MidiTrackName}'.");
            }
        }
    }
}
