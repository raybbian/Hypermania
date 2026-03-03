using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Design.Animation.MoveBuilder.Editors
{
    public sealed class MoveBuilderVisibilityTreeView : TreeView
    {
        private readonly MoveBuilderVisibilityModel _model;

        private const float EyeColWidth = 18f;
        private const float RowHeight = 18f;

        private static GUIContent _eyeOn;
        private static GUIContent _eyeOff;

        public MoveBuilderVisibilityTreeView(TreeViewState state, MoveBuilderVisibilityModel model)
            : base(state)
        {
            _model = model;
            rowHeight = RowHeight;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            _model.OnVisibilityCacheUpdated += Reload;
            Reload();
        }

        ~MoveBuilderVisibilityTreeView()
        {
            _model.OnVisibilityCacheUpdated -= Reload;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem
            {
                id = -1,
                depth = -1,
                displayName = "Root",
            };

            root.children = new List<TreeViewItem>();

            if (_model == null)
            {
                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            // Create all items first
            var itemsById = new Dictionary<int, VisibilityItem>(_model.VisibilityNodes.Count);
            for (int i = 0; i < _model.VisibilityNodes.Count; i++)
            {
                var n = _model.VisibilityNodes[i];
                itemsById[n.Id] = new VisibilityItem(n.Id, n.Depth, n.Name, n.Path);
            }

            // Wire parent -> children
            for (int i = 0; i < _model.VisibilityNodes.Count; i++)
            {
                var n = _model.VisibilityNodes[i];
                var item = itemsById[n.Id];

                if (n.ParentId == -1)
                {
                    root.children.Add(item);
                    continue;
                }

                if (itemsById.TryGetValue(n.ParentId, out var parent))
                {
                    parent.children ??= new List<TreeViewItem>();
                    parent.children.Add(item);
                }
                else
                {
                    // If parent is missing for some reason, fall back to root.
                    root.children.Add(item);
                }
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item is not VisibilityItem item)
            {
                base.RowGUI(args);
                return;
            }

            EnsureIcons();

            Rect rowRect = args.rowRect;

            const float pad = 2f;
            Rect eyeRect = new Rect(rowRect.xMax - EyeColWidth - pad, rowRect.y + 1f, EyeColWidth, rowRect.height - 2f);

            Rect contentRect = rowRect;
            contentRect.xMax = eyeRect.xMin - pad;

            args.rowRect = contentRect;
            base.RowGUI(args);

            bool visible = _model.GetPathVisible(item.Path);
            if (GUI.Button(eyeRect, visible ? _eyeOn : _eyeOff, GUIStyle.none))
            {
                _model.SetPathVisible(item.Path, !visible);
                RepaintOwnerWindow();
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            if (_model == null)
                return;

            if (_model.TryGetPathById(id, out string path))
            {
                bool cur = _model.GetPathVisible(path);
                _model.SetPathVisible(path, !cur);
                RepaintOwnerWindow();
            }
        }

        private void EnsureIcons()
        {
            if (_eyeOn != null && _eyeOff != null)
                return;

            _eyeOn =
                EditorGUIUtility.IconContent("scenevis_visible")
                ?? EditorGUIUtility.IconContent("VisibilityOn")
                ?? new GUIContent("◉");

            _eyeOff =
                EditorGUIUtility.IconContent("scenevis_hidden")
                ?? EditorGUIUtility.IconContent("VisibilityOff")
                ?? new GUIContent("○");
        }

        private void RepaintOwnerWindow()
        {
            // TreeView doesn't automatically repaint the host window when only internal state changes.
            EditorWindow.focusedWindow?.Repaint();
        }

        private sealed class VisibilityItem : TreeViewItem
        {
            public readonly string Path;

            public VisibilityItem(int id, int depth, string displayName, string path)
                : base(id, depth, displayName)
            {
                Path = path;
            }
        }
    }
}
