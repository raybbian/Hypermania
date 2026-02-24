#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Design.Animation.Sorting.Editors
{
    [CustomEditor(typeof(SpriteSortGroup))]
    public sealed class SpriteSortGroupEditor : Editor
    {
        private SerializedProperty _sortingLayerIdProp;
        private SerializedProperty _baseOrderProp;

        private readonly List<SpriteSortItem> _derivedItems = new();
        private ReorderableList _list;

        private void OnEnable()
        {
            _sortingLayerIdProp = serializedObject.FindProperty("_sortingLayerId");
            _baseOrderProp = serializedObject.FindProperty("_baseOrder");

            BuildList();
            RefreshDerivedItems();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            int currentId = _sortingLayerIdProp.intValue;
            int newId = SortingLayerUtil.PopupSortingLayer("Sorting Layer", currentId);
            if (newId != currentId)
                _sortingLayerIdProp.intValue = newId;

            EditorGUILayout.PropertyField(_baseOrderProp);

            EditorGUILayout.Space(6);

            var group = (SpriteSortGroup)target;

            // Keep list always in sync with current children orders.
            RefreshDerivedItems();

            _list.DoLayoutList();

            if (serializedObject.ApplyModifiedProperties())
            {
                // If layer/base changes, re-apply (keeps renderer values synchronized).
                ApplySorting(group);
                RefreshDerivedItems();
            }
        }

        private void BuildList()
        {
            _list = new ReorderableList(
                _derivedItems, // IMPORTANT: not serialized; derived live
                typeof(SpriteSortItem),
                draggable: true,
                displayHeader: true,
                displayAddButton: false,
                displayRemoveButton: false
            );

            _list.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Sprite Sort Items (drag to reorder)");
            };

            _list.elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 4f;

            _list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;

                if (index < 0 || index >= _derivedItems.Count)
                    return;

                var item = _derivedItems[index];

                // Thumbnail rect
                var thumbRect = new Rect(
                    rect.x,
                    rect.y,
                    EditorGUIUtility.singleLineHeight,
                    EditorGUIUtility.singleLineHeight
                );

                // Object field rect
                var fieldRect = new Rect(
                    thumbRect.x + thumbRect.width + 4f,
                    rect.y,
                    EditorGUIUtility.singleLineHeight * 12f,
                    EditorGUIUtility.singleLineHeight
                );

                // Secondary info rect
                var infoRect = new Rect(
                    fieldRect.x + fieldRect.width + 4f,
                    rect.y,
                    rect.width - thumbRect.width - fieldRect.width - 8f,
                    EditorGUIUtility.singleLineHeight
                );

                DrawThumb(thumbRect, item);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.ObjectField(
                        fieldRect,
                        GUIContent.none,
                        item,
                        typeof(SpriteSortItem),
                        allowSceneObjects: true
                    );
                }

                if (item == null)
                {
                    EditorGUI.LabelField(infoRect, "Missing reference");
                }
                else
                {
                    var r = item.Renderer;
                    if (r == null)
                    {
                        EditorGUI.LabelField(infoRect, "No SpriteRenderer found");
                    }
                    else
                    {
                        EditorGUI.LabelField(infoRect, $"Order {r.sortingOrder}");
                    }
                }
            };

            _list.onReorderCallback = _ =>
            {
                var group = (SpriteSortGroup)target;
                ApplyOrdersFromCurrentList(group);
                RefreshDerivedItems();
            };
        }

        private void RefreshDerivedItems()
        {
            _derivedItems.Clear();

            var group = (SpriteSortGroup)target;
            group.GetComponentsInChildren(includeInactive: true, result: _derivedItems);

            _derivedItems.Sort(SpriteSortGroup.CompareItemsByRendererOrderThenSibling);

            _list.list = _derivedItems;
        }

        private void ApplySorting(SpriteSortGroup group)
        {
            // Apply serialized edits (sorting layer/base order) before operating.
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            var renderers = GetAllRenderers(group);
            Undo.RecordObjects(renderers, "Apply Sprite Sorting");
            Undo.RecordObject(group, "Apply Sprite Sorting");

            group.ApplyToRenderers();

            foreach (var r in renderers)
                if (r != null)
                    EditorUtility.SetDirty(r);

            EditorUtility.SetDirty(group);
        }

        private void ApplyOrdersFromCurrentList(SpriteSortGroup group)
        {
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            var renderers = GetAllRenderers(group);
            Undo.RecordObjects(renderers, "Reorder Sprite Sorting");
            Undo.RecordObject(group, "Reorder Sprite Sorting");

            int order = group.BaseOrder;
            for (int i = 0; i < _derivedItems.Count; i++)
            {
                var item = _derivedItems[i];
                if (item == null)
                    continue;

                var r = item.Renderer;
                if (r == null)
                    continue;

                r.sortingLayerID = group.SortingLayerId;
                r.sortingOrder = order;
                order++;

                EditorUtility.SetDirty(r);
            }

            EditorUtility.SetDirty(group);
        }

        private static SpriteRenderer[] GetAllRenderers(SpriteSortGroup group)
        {
            return group.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        }

        private static void DrawThumb(Rect rect, SpriteSortItem item)
        {
            Texture tex = null;

            if (item != null)
            {
                var thumbSprite = item.ThumbnailOverride;
                if (thumbSprite == null)
                {
                    var r = item.Renderer;
                    if (r != null)
                        thumbSprite = r.sprite;
                }

                if (thumbSprite != null)
                    tex = AssetPreview.GetAssetPreview(thumbSprite) ?? AssetPreview.GetMiniThumbnail(thumbSprite);
            }

            if (tex != null)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.HelpBox(rect, " ", MessageType.None);
            }
        }
    }
}
#endif
