using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;
#endif

namespace Design.Animation.Keyframing
{
    public sealed class AnimationTools : MonoBehaviour
    {
        [Header("Target Clip")]
        [SerializeField]
        private AnimationClip _clip;

        [Header("Named Children (searched under this GameObject)")]
        [SerializeField]
        private string[] _rigChildNames = { "Root", "ScytheHandle1" };

        [SerializeField]
        private string _spriteChildName = "Sprites";

        [SerializeField]
        private string _ikChildName = "IK";

        public AnimationClip Clip => _clip;

#if UNITY_EDITOR
        private const float Epsilon = 1e-6f;

        public void AddTimeZeroKeys()
        {
            if (_clip == null)
                return;

            Undo.RegisterCompleteObjectUndo(_clip, "Add time 0 keys");
            _clip.EnsureQuaternionContinuity();

            Transform animRoot = transform;

            foreach (var rigChildName in _rigChildNames)
            {
                Transform rootNode = FindChildByName(animRoot, rigChildName);
                if (rootNode != null)
                {
                    foreach (Transform t in EnumerateHierarchy(rootNode))
                    {
                        AddTransformKeysAtTime(
                            _clip,
                            animRoot,
                            t,
                            0f,
                            includePosition: true,
                            includeScale: true,
                            includeEuler: true
                        );
                    }
                }
            }

            Transform spriteNode = FindChildByName(animRoot, _spriteChildName);
            if (spriteNode != null)
            {
                foreach (Transform t in EnumerateHierarchy(spriteNode))
                {
                    var sr = t.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        AddFloatKeyAtTime(
                            clip: _clip,
                            animRoot: animRoot,
                            componentType: typeof(SpriteRenderer),
                            targetTransform: t,
                            propertyName: "m_SortingOrder",
                            time: 0f,
                            value: sr.sortingOrder
                        );
                    }

                    var resolver = t.GetComponent<SpriteResolver>();
                    if (resolver != null)
                    {
                        float hash = ReadSpriteResolverSpriteHash(resolver);
                        AddDiscreteIntKeyAtTime(
                            clip: _clip,
                            animRoot: animRoot,
                            targetTransform: t,
                            componentType: typeof(SpriteResolver),
                            propertyName: "m_SpriteHash",
                            time: 0f,
                            value: hash
                        );
                    }
                }
            }

            foreach (var marker in GetComponentsInChildren<IKTargetMarker>(includeInactive: true))
            {
                Transform t = marker.transform;
                AddTransformKeysAtTime(
                    _clip,
                    animRoot,
                    t,
                    0f,
                    includePosition: true,
                    includeScale: false,
                    includeEuler: false
                );
            }

            Transform ikNode = FindChildByName(animRoot, _ikChildName);
            if (ikNode != null)
            {
                foreach (Transform t in EnumerateHierarchy(ikNode))
                {
                    var solver = t.GetComponent<LimbSolver2D>();
                    if (solver != null)
                    {
                        AddFloatKeyAtTime(
                            clip: _clip,
                            animRoot: animRoot,
                            componentType: typeof(LimbSolver2D),
                            targetTransform: t,
                            propertyName: "m_Flip",
                            time: 0f,
                            value: solver.flip ? 1f : 0f
                        );
                    }
                }
            }

            EditorUtility.SetDirty(_clip);
            AssetDatabase.SaveAssets();
        }

        public void CopyTimeZeroKeysToClipEnd()
        {
            if (_clip == null)
                return;

            Undo.RegisterCompleteObjectUndo(_clip, "Copy time 0 keys to end");

            float endTime = GetClipEndTime(_clip);
            if (endTime <= 0f)
                return;

            // Copy all float curve bindings: if a curve has a key at time 0, clone it to endTime.
            var bindings = AnimationUtility.GetCurveBindings(_clip);
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(_clip, binding);
                if (curve == null || curve.keys == null || curve.keys.Length == 0)
                    continue;

                int k0 = FindKeyIndexAtTime(curve.keys, 0f);
                if (k0 < 0)
                    continue;

                Keyframe src = curve.keys[k0];
                if (Mathf.Abs(endTime - src.time) < Epsilon)
                    continue;

                // Replace existing end key if present, otherwise add.
                int kend = FindKeyIndexAtTime(curve.keys, endTime);
                var dst = new Keyframe(endTime, src.value, src.inTangent, src.outTangent)
                {
                    weightedMode = src.weightedMode,
                    inWeight = src.inWeight,
                    outWeight = src.outWeight,
                };

                if (kend >= 0)
                {
                    curve.MoveKey(kend, dst);
                }
                else
                {
                    curve.AddKey(dst);
                }

                AnimationUtility.SetEditorCurve(_clip, binding, curve);
            }

            EditorUtility.SetDirty(_clip);
            AssetDatabase.SaveAssets();
        }

        public void SetSortingOrderTangentsConstant()
        {
            if (_clip == null)
                return;

            Undo.RegisterCompleteObjectUndo(_clip, "Set sorting order tangents constant");

            var bindings = AnimationUtility.GetCurveBindings(_clip);
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(SpriteRenderer))
                    continue;
                if (binding.propertyName != "m_SortingOrder")
                    continue;

                var curve = AnimationUtility.GetEditorCurve(_clip, binding);
                if (curve == null || curve.length == 0)
                    continue;

                for (int i = 0; i < curve.length; i++)
                {
                    SetConstantTangents(curve, i);
                }

                AnimationUtility.SetEditorCurve(_clip, binding, curve);
            }

            EditorUtility.SetDirty(_clip);
            AssetDatabase.SaveAssets();
        }

        private static float ReadSpriteResolverSpriteHash(SpriteResolver resolver)
        {
            if (resolver == null)
                return 0;
            //holy hack

            unsafe float ConvertDiscreteIntToFloat(int f)
            {
                int* ptr = &f;
                float* ptr2 = (float*)ptr;
                return *ptr2;
            }
            int hash = Bit30Hash_GetStringHash(resolver.GetCategory() + "_" + resolver.GetLabel());
            int Bit30Hash_GetStringHash(string value)
            {
                return PreserveFirst30Bits(Animator.StringToHash(value));
            }
            int PreserveFirst30Bits(int input)
            {
                return input & 0x3FFFFFFF;
            }
            return ConvertDiscreteIntToFloat(hash);
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;

            var q = new Queue<Transform>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var t = q.Dequeue();
                if (t != root && t.name == name)
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    q.Enqueue(t.GetChild(i));
            }
            return null;
        }

        private static IEnumerable<Transform> EnumerateHierarchy(Transform root)
        {
            if (root == null)
                yield break;

            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                yield return t;

                for (int i = t.childCount - 1; i >= 0; i--)
                    stack.Push(t.GetChild(i));
            }
        }

        private static void AddTransformKeysAtTime(
            AnimationClip clip,
            Transform animRoot,
            Transform target,
            float time,
            bool includePosition,
            bool includeScale,
            bool includeEuler
        )
        {
            if (clip == null || animRoot == null || target == null)
                return;

            if (includePosition)
            {
                Vector3 p = target.localPosition;
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalPosition.x", time, p.x);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalPosition.y", time, p.y);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalPosition.z", time, p.z);
            }

            if (includeScale)
            {
                Vector3 s = target.localScale;
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalScale.x", time, s.x);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalScale.y", time, s.y);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "m_LocalScale.z", time, s.z);
            }

            if (includeEuler)
            {
                Vector3 e = target.localEulerAngles;
                e.x = Mathf.DeltaAngle(0f, e.x);
                e.y = Mathf.DeltaAngle(0f, e.y);
                e.z = Mathf.DeltaAngle(0f, e.z);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "localEulerAnglesRaw.x", time, e.x);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "localEulerAnglesRaw.y", time, e.y);
                AddFloatKeyAtTime(clip, animRoot, typeof(Transform), target, "localEulerAnglesRaw.z", time, e.z);
            }
        }

        private static void AddFloatKeyAtTime(
            AnimationClip clip,
            Transform animRoot,
            Type componentType,
            Transform targetTransform,
            string propertyName,
            float time,
            float value
        )
        {
            string path = AnimationUtility.CalculateTransformPath(targetTransform, animRoot);
            var binding = EditorCurveBinding.FloatCurve(path, componentType, propertyName);
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();

            int idx = FindKeyIndexAtTime(curve.keys, time);
            var k = new Keyframe(time, value);

            if (idx >= 0)
                curve.MoveKey(idx, k);
            else
                curve.AddKey(k);

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static void AddDiscreteIntKeyAtTime(
            AnimationClip clip,
            Transform animRoot,
            Transform targetTransform,
            Type componentType,
            string propertyName,
            float time,
            float value
        )
        {
            string path = AnimationUtility.CalculateTransformPath(targetTransform, animRoot);
            var binding = EditorCurveBinding.DiscreteCurve(path, componentType, propertyName);
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();

            var k = new Keyframe(time, value)
            {
                inTangent = float.PositiveInfinity,
                outTangent = float.PositiveInfinity,
            };

            int idx = FindKeyIndexAtTime(curve.keys, time);
            if (idx >= 0)
                curve.MoveKey(idx, k);
            else
                curve.AddKey(k);

            // Re-apply tangents as constant using the supported API as well.
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }

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

        private static float GetClipEndTime(AnimationClip clip)
        {
            // Prefer an actual last-key time if possible, otherwise fall back to clip.length.
            float max = 0f;
            bool any = false;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var b in bindings)
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null || c.length == 0)
                    continue;

                float t = c.keys[c.length - 1].time;
                if (!any || t > max)
                {
                    max = t;
                    any = true;
                }
            }

            if (any && max > 0f)
                return max;

            return Mathf.Max(0f, clip.length);
        }

        private static void SetConstantTangents(AnimationCurve curve, int keyIndex)
        {
            if (curve == null || keyIndex < 0 || keyIndex >= curve.length)
                return;

            AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Constant);
        }
#endif
    }
}
