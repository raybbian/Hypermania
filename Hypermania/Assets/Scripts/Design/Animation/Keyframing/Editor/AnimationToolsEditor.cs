using Design.Animation.MoveBuilder.Editor;
using UnityEditor;
using UnityEngine;

namespace Design.Animation.Keyframing.Editor
{
    [CustomEditor(typeof(AnimationTools))]
    public sealed class AnimationKeyframeToolsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (AnimationTools)target;

            var animState = MoveBuilderAnimationState.GetAnimState();
            if (!animState.HasValue)
            {
                EditorGUILayout.HelpBox(
                    "Open the Animation window and select an object/clip there to drive the Animation Tools.",
                    MessageType.Info
                );
                return;
            }
            var state = animState.Value;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Animation Clip (Animation Window)",
                    state.Clip,
                    typeof(AnimationClip),
                    false
                );
                EditorGUILayout.IntField("Anim Frame (Animation Window)", state.Tick);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(state.Clip == null))
            {
                if (GUILayout.Button("Add keyframes (for current pose) for all animatable values at time 0"))
                {
                    t.AddTimeZeroKeys(state.Clip);
                }

                if (GUILayout.Button("Copy all time 0 keys to clip end"))
                {
                    t.CopyTimeZeroKeysToClipEnd(state.Clip);
                }

                if (GUILayout.Button("Set SpriteRenderer Order-in-Layer tangents to Constant"))
                {
                    t.SetSortingOrderTangentsConstant(state.Clip);
                }
            }
        }
    }
}
