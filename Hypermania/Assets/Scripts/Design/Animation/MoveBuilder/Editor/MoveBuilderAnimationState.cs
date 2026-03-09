using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Design.Animation.MoveBuilder.Editors
{
    public struct MoveBuilderAnimationState
    {
        public readonly AnimationClip Clip;
        public readonly HitboxData Data;
        public readonly int Tick;

        public MoveBuilderAnimationState(AnimationClip clip, HitboxData data, int tick)
        {
            Clip = clip;
            Data = data;
            Tick = tick;
        }

        public static MoveBuilderAnimationState? GetAnimState()
        {
            bool clipExists = TryGetAnimationWindowClipAndFrame(out var clip, out int frame);
            if (!clipExists)
                return null;
            HitboxData data = EnsureSiblingHitboxData(clip);
            if (data == null)
                return null;
            return new MoveBuilderAnimationState(clip, data, frame);
        }

        private static bool TryGetAnimationWindowClipAndFrame(out AnimationClip clip, out int frame)
        {
            clip = null;
            frame = 0;

            // If multiple Animation windows are open, pick the focused one if possible.
            var windows = Resources.FindObjectsOfTypeAll<AnimationWindow>();
            if (windows == null || windows.Length == 0)
                return false;

            AnimationWindow win = windows.FirstOrDefault(w => EditorWindow.focusedWindow == w) ?? windows[0];

            clip = win.animationClip;
            frame = win.frame;
            return true;
        }

        private static HitboxData EnsureSiblingHitboxData(AnimationClip clip)
        {
            string clipPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(clipPath))
                return null;

            string dir = Path.GetDirectoryName(clipPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir))
                return null;

            string expected = $"{dir}/{clip.name}.asset";
            var direct = AssetDatabase.LoadAssetAtPath<HitboxData>(expected);
            if (direct == null)
            {
                direct = ScriptableObject.CreateInstance<HitboxData>();
                AssetDatabase.CreateAsset(direct, expected);
            }
            return direct;
        }
    }
}
