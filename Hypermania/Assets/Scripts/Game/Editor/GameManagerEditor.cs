using Game.Runners;
using Steamworks;
using UnityEditor;
using UnityEngine;

namespace Game.Editors
{
    [CustomEditor(typeof(GameManager))]
    public sealed class GameManagerEditor : Editor
    {
        private ulong _roomId;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var gm = (GameManager)target;
            if (gm == null)
                return;

            bool inPlayMode = Application.isPlaying;
            bool isSinglePlayer = gm.Runner is SingleplayerRunner;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Online Controls", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!inPlayMode || isSinglePlayer))
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUI.DisabledScope(gm.Started))
                {
                    if (GUILayout.Button("Create Lobby"))
                    {
                        gm.CreateLobby();
                    }
                    _roomId = (ulong)EditorGUILayout.LongField("Room Id", (long)_roomId);
                    if (GUILayout.Button("Join Lobby"))
                    {
                        gm.JoinLobby(new CSteamID(_roomId));
                    }
                    if (GUILayout.Button("Leave Lobby"))
                    {
                        gm.LeaveLobby();
                    }
                    if (GUILayout.Button("Start Game"))
                    {
                        gm.StartGame();
                    }
                }
                using (new EditorGUI.DisabledScope(!gm.Started))
                {
                    if (GUILayout.Button("Stop Game"))
                    {
                        gm.DeInit();
                    }
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Local Controls", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!inPlayMode || !isSinglePlayer))
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUI.DisabledScope(gm.Started))
                {
                    if (GUILayout.Button("Start Local Game"))
                    {
                        gm.StartLocalGame();
                    }
                }
                using (new EditorGUI.DisabledScope(!gm.Started))
                {
                    if (GUILayout.Button("Stop Game"))
                    {
                        gm.DeInit();
                    }
                }
            }
        }
    }
}
