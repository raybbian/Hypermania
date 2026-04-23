using Design.Animation.MoveBuilder.Editor;
using UnityEditor;
using UnityEngine;

namespace Design.Animation.Keyframing.Editor
{
    [CustomEditor(typeof(AnimationChildCompensator))]
    public sealed class AnimationChildCompensatorEditor : UnityEditor.Editor
    {
        private const float Epsilon = 1e-6f;

        private Vector3? _snapshotLocalPos;
        private Vector3 _snapshotWorldPos;
        private AnimationClip _snapshotClip;
        private int _snapshotFrame;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (AnimationChildCompensator)target;

            var animState = MoveBuilderAnimationState.GetAnimState();
            if (!animState.HasValue)
            {
                EditorGUILayout.HelpBox(
                    "Open the Animation window and select an object/clip there to drive the Child Compensator.",
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

            DrawDirectChildrenList(t);

            EditorGUILayout.Space(8);

            if (_snapshotLocalPos == null)
            {
                EditorGUILayout.HelpBox(
                    "1) Scrub the Animation window to the target frame and enable Record.\n"
                        + "2) Click Capture.\n"
                        + "3) Drag the parent in the Scene view.\n"
                        + "4) Click Apply — direct children get an inverse offset (in parent's local frame) at the captured frame only.",
                    MessageType.None
                );

                using (new EditorGUI.DisabledScope(state.Clip == null))
                {
                    if (GUILayout.Button("Capture parent snapshot"))
                    {
                        _snapshotLocalPos = t.Parent.localPosition;
                        _snapshotWorldPos = t.Parent.position;
                        _snapshotClip = state.Clip;
                        _snapshotFrame = state.Tick;
                    }
                }
                return;
            }

            Vector3 worldDelta = t.Parent.position - _snapshotWorldPos;
            Vector3 localDelta = t.Parent.InverseTransformVector(worldDelta);
            Vector3 childOffset = -localDelta;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("Captured Local Position", _snapshotLocalPos.Value);
                EditorGUILayout.Vector3Field("Current Local Position", t.Parent.localPosition);
                EditorGUILayout.Vector3Field("Parent Drag (World Δ)", worldDelta);
                EditorGUILayout.Vector3Field("Parent Drag (Parent-Local Δ)", localDelta);
                EditorGUILayout.Vector3Field("Offset Applied To Children (-Δ)", childOffset);
                EditorGUILayout.ObjectField("Captured Clip", _snapshotClip, typeof(AnimationClip), false);
                EditorGUILayout.IntField("Captured Frame", _snapshotFrame);
            }

            if (_snapshotClip != state.Clip)
            {
                EditorGUILayout.HelpBox(
                    "The Animation window clip changed after capture. Cancel and re-capture on the intended clip.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(_snapshotClip == null || _snapshotClip != state.Clip))
            {
                if (GUILayout.Button("Apply compensation to direct children"))
                {
                    Apply(t);
                }
            }

            if (GUILayout.Button("Cancel snapshot"))
            {
                ClearSnapshot();
            }
        }

        public override bool RequiresConstantRepaint() => _snapshotLocalPos != null;

        private static void DrawDirectChildrenList(AnimationChildCompensator t)
        {
            Transform parent = t.Parent;
            int count = parent != null ? parent.childCount : 0;

            EditorGUILayout.LabelField($"Direct Children To Compensate ({count})", EditorStyles.boldLabel);

            if (parent == null || count == 0)
            {
                EditorGUILayout.HelpBox("No direct children under the parent.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                foreach (Transform c in parent)
                {
                    EditorGUILayout.ObjectField(c.name, c.gameObject, typeof(GameObject), true);
                }
            }
        }

        private void Apply(AnimationChildCompensator t)
        {
            if (_snapshotLocalPos == null || _snapshotClip == null)
                return;

            Vector3 worldDelta = t.Parent.position - _snapshotWorldPos;
            Vector3 localDelta = t.Parent.InverseTransformVector(worldDelta);

            if (localDelta.sqrMagnitude < Epsilon * Epsilon)
            {
                ClearSnapshot();
                return;
            }

            float frameRate = _snapshotClip.frameRate > 0f ? _snapshotClip.frameRate : 60f;
            float time = _snapshotFrame / frameRate;

            Transform animRoot = t.AnimRoot;
            Transform parent = t.Parent;

            Undo.RegisterCompleteObjectUndo(_snapshotClip, "Compensate children for parent drag");

            foreach (Transform child in parent)
            {
                ShiftChildAxisFromTime(
                    _snapshotClip,
                    animRoot,
                    child,
                    "m_LocalPosition.x",
                    child.localPosition.x,
                    time,
                    localDelta.x
                );
                ShiftChildAxisFromTime(
                    _snapshotClip,
                    animRoot,
                    child,
                    "m_LocalPosition.y",
                    child.localPosition.y,
                    time,
                    localDelta.y
                );
                ShiftChildAxisFromTime(
                    _snapshotClip,
                    animRoot,
                    child,
                    "m_LocalPosition.z",
                    child.localPosition.z,
                    time,
                    localDelta.z
                );
            }

            EditorUtility.SetDirty(_snapshotClip);
            AssetDatabase.SaveAssets();

            ClearSnapshot();
        }

        private static void ShiftChildAxisFromTime(
            AnimationClip clip,
            Transform animRoot,
            Transform child,
            string property,
            float currentLocalValue,
            float time,
            float delta
        )
        {
            if (Mathf.Abs(delta) < Epsilon)
                return;

            string path = AnimationUtility.CalculateTransformPath(child, animRoot);
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            bool isNewCurve = curve == null || curve.keys == null || curve.keys.Length == 0;
            if (isNewCurve)
                curve = new AnimationCurve();

            int idx = FindKeyIndexAtTime(curve.keys, time);
            if (idx < 0)
            {
                float holdValue = curve.length > 0 ? curve.Evaluate(time) : currentLocalValue;
                curve.AddKey(new Keyframe(time, holdValue));
                idx = FindKeyIndexAtTime(curve.keys, time);
            }

            var keys = curve.keys;
            keys[idx].value -= delta;
            curve.keys = keys;

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static int FindKeyIndexAtTime(Keyframe[] keys, float time)
        {
            if (keys == null)
                return -1;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - time) < Epsilon)
                    return i;
            }
            return -1;
        }

        private void ClearSnapshot()
        {
            _snapshotLocalPos = null;
            _snapshotWorldPos = Vector3.zero;
            _snapshotClip = null;
            _snapshotFrame = 0;
        }
    }
}
