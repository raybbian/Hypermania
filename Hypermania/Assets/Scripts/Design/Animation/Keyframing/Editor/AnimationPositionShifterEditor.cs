using UnityEditor;
using UnityEngine;

namespace Design.Animation.Keyframing.Editor
{
    [CustomEditor(typeof(AnimationPositionShifter))]
    public sealed class AnimationPositionShifterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (AnimationPositionShifter)target;

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Shift positions in all listed clips"))
            {
                t.ShiftAll();
            }
        }
    }
}
