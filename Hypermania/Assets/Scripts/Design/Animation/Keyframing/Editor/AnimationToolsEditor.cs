using UnityEditor;
using UnityEngine;

namespace Design.Animation.Keyframing
{
    [CustomEditor(typeof(AnimationTools))]
    public sealed class AnimationKeyframeToolsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (AnimationTools)target;

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(t.Clip == null))
            {
                if (GUILayout.Button("1) Add time 0 keys [MAKE SURE TO CHANGE TARGET ANIM]"))
                {
                    t.AddTimeZeroKeys();
                }

                if (GUILayout.Button("2) Copy time 0 keys to clip end [MAKE SURE TO CHANGE TARGET ANIM]"))
                {
                    t.CopyTimeZeroKeysToClipEnd();
                }

                if (
                    GUILayout.Button(
                        "3) Set SpriteRenderer Order-in-Layer tangents to Constant [MAKE SURE TO CHANGE TARGET ANIM]"
                    )
                )
                {
                    t.SetSortingOrderTangentsConstant();
                }
            }

            if (t.Clip == null)
            {
                EditorGUILayout.HelpBox("Assign an AnimationClip to enable the buttons.", MessageType.Info);
            }
        }
    }
}
