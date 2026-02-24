#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Design.Animation.Sorting.Editors
{
    internal static class SortingLayerUtil
    {
        // Unity does not expose an official "get all sorting layers" API in runtime.
        // In editor, reflection is the common approach.
        private static readonly Type InternalEditorUtilityType = Type.GetType(
            "UnityEditorInternal.InternalEditorUtility, UnityEditor"
        );

        private static readonly PropertyInfo SortingLayersProp = InternalEditorUtilityType?.GetProperty(
            "sortingLayerNames",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        private static readonly PropertyInfo SortingLayerUniqueIDsProp = InternalEditorUtilityType?.GetProperty(
            "sortingLayerUniqueIDs",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        public static string[] GetSortingLayerNames()
        {
            var v = SortingLayersProp?.GetValue(null, null) as string[];
            return v ?? Array.Empty<string>();
        }

        public static int[] GetSortingLayerIds()
        {
            var v = SortingLayerUniqueIDsProp?.GetValue(null, null) as int[];
            return v ?? Array.Empty<int>();
        }

        public static int IndexOfLayerId(int layerId)
        {
            var ids = GetSortingLayerIds();
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == layerId)
                    return i;
            return -1;
        }

        public static int PopupSortingLayer(string label, int currentLayerId)
        {
            var names = GetSortingLayerNames();
            var ids = GetSortingLayerIds();

            if (names.Length == 0 || ids.Length != names.Length)
            {
                EditorGUILayout.HelpBox("No sorting layers found (or mismatch).", MessageType.Warning);
                return currentLayerId;
            }

            int currentIndex = IndexOfLayerId(currentLayerId);
            if (currentIndex < 0)
                currentIndex = 0;

            int newIndex = EditorGUILayout.Popup(label, currentIndex, names);
            return ids[Mathf.Clamp(newIndex, 0, ids.Length - 1)];
        }
    }
}
#endif
