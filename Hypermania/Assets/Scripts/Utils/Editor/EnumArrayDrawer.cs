using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Utils.EnumArray
{
    [CustomPropertyDrawer(typeof(EnumArray<,>), true)]
    public class EnumArrayDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var valuesProp = property.FindPropertyRelative("values");
            if (valuesProp == null)
                return EditorGUIUtility.singleLineHeight;

            float h = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return h;

            for (int i = 0; i < valuesProp.arraySize; i++)
                h += EditorGUI.GetPropertyHeight(valuesProp.GetArrayElementAtIndex(i), true) + 2f;

            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valuesProp = property.FindPropertyRelative("values");
            if (valuesProp == null)
            {
                EditorGUI.LabelField(position, label.text, "Missing values[]");
                return;
            }

            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
                property.isExpanded,
                label,
                true
            );

            if (!property.isExpanded)
                return;

            EditorGUI.indentLevel++;

            Type enumType = GetEnumTypeFromDrawerField();
            if (enumType == null || !enumType.IsEnum)
            {
                EditorGUI.indentLevel--;
                EditorGUI.LabelField(
                    new Rect(
                        position.x,
                        position.y + EditorGUIUtility.singleLineHeight,
                        position.width,
                        EditorGUIUtility.singleLineHeight
                    ),
                    "EnumArray",
                    "Could not resolve enum type."
                );
                return;
            }

            // Ensure array size using shared cache Count
            int n = GetCacheCount(enumType);
            if (valuesProp.arraySize != n)
                valuesProp.arraySize = n;

            string[] names = GetCacheNames(enumType);

            float y = position.y + EditorGUIUtility.singleLineHeight + 2f;
            for (int i = 0; i < valuesProp.arraySize; i++)
            {
                var elem = valuesProp.GetArrayElementAtIndex(i);
                float elemH = EditorGUI.GetPropertyHeight(elem, true);
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, elemH),
                    elem,
                    new GUIContent(names[i]),
                    true
                );
                y += elemH + 2f;
            }

            EditorGUI.indentLevel--;
        }

        private Type GetEnumTypeFromDrawerField()
        {
            var ft = fieldInfo.FieldType;
            if (!ft.IsGenericType)
                return null;
            return ft.GetGenericArguments()[0];
        }

        private static int GetCacheCount(Type enumType)
        {
            Type cacheType = typeof(EnumIndexCache<>).MakeGenericType(enumType);
            FieldInfo countFi = cacheType.GetField("Count", BindingFlags.Public | BindingFlags.Static);
            return (int)countFi.GetValue(null);
        }

        private static string[] GetCacheNames(Type enumType)
        {
            Type cacheType = typeof(EnumIndexCache<>).MakeGenericType(enumType);
            FieldInfo namesFi = cacheType.GetField("Names", BindingFlags.Public | BindingFlags.Static);
            return (string[])namesFi.GetValue(null);
        }
    }
}
