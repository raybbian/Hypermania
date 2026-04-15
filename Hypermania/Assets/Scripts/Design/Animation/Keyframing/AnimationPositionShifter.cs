using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Design.Animation.Keyframing
{
    public sealed class AnimationPositionShifter : MonoBehaviour
    {
        [Header("GameObjects whose position curves will be shifted")]
        [SerializeField]
        private GameObject[] _targets;

        [Header("Shift amount (local position delta)")]
        [SerializeField]
        private Vector3 _shift;

        [Header("Animation clips to modify")]
        [SerializeField]
        private AnimationClip[] _clips;

#if UNITY_EDITOR
        public void ShiftAll()
        {
            if (_targets == null || _clips == null)
                return;

            Transform animRoot = transform;

            foreach (var go in _targets)
            {
                if (go == null)
                    continue;
                Undo.RecordObject(go.transform, "Shift transform position");
                go.transform.localPosition += _shift;
            }

            foreach (var clip in _clips)
            {
                if (clip == null)
                    continue;

                Undo.RegisterCompleteObjectUndo(clip, "Shift animation positions");

                foreach (var go in _targets)
                {
                    if (go == null)
                        continue;

                    string path = AnimationUtility.CalculateTransformPath(go.transform, animRoot);
                    ShiftAxis(clip, path, "m_LocalPosition.x", _shift.x);
                    ShiftAxis(clip, path, "m_LocalPosition.y", _shift.y);
                    ShiftAxis(clip, path, "m_LocalPosition.z", _shift.z);
                }

                EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
        }

        private static void ShiftAxis(AnimationClip clip, string path, string property, float delta)
        {
            if (Mathf.Approximately(delta, 0f))
                return;

            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0)
                return;

            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
                keys[i].value += delta;
            curve.keys = keys;

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
#endif
    }
}
