using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameManager))]
public sealed class GameManagerEditor : Editor
{
    private ulong _roomId;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gm = (GameManager)target;
        if (gm == null) return;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Synapse Controls", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _roomId = (ulong)EditorGUILayout.LongField("Room Id", (long)_roomId);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Room"))
            {
                gm.CreateRoom();
            }

            if (GUILayout.Button("Join Room"))
            {
                gm.JoinRoom(_roomId);
            }

            if (GUILayout.Button("Leave Room"))
            {
                gm.LeaveRoom();
            }
            EditorGUILayout.EndHorizontal();
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            if (GUILayout.Button("Start Game"))
            {
                gm.StartGame();
            }
        }
    }
}
