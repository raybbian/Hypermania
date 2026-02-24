using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation.MoveBuilder.Editors
{
    public sealed class MoveBuilderControlsView
    {
        public void DrawLeft(MoveBuilderModel m, int tps)
        {
            EditorGUI.BeginChangeCheck();
            var newPrefab = (GameObject)
                EditorGUILayout.ObjectField("Character Prefab", m.CharacterPrefab, typeof(GameObject), false);

            var newClip = (AnimationClip)
                EditorGUILayout.ObjectField("Animation Clip", m.Clip, typeof(AnimationClip), false);

            var newData = (HitboxData)
                EditorGUILayout.ObjectField("Move Data (Asset)", m.Data, typeof(HitboxData), false);

            if (EditorGUI.EndChangeCheck())
            {
                bool prefabChanged = newPrefab != m.CharacterPrefab;
                bool clipChanged = newClip != m.Clip;
                bool dataChanged = newData != m.Data;

                m.CharacterPrefab = newPrefab;
                m.Clip = newClip;
                m.Data = newData;

                if (clipChanged || dataChanged)
                    m.ResetTimelineSelection();
                if (prefabChanged)
                {
                    m.VisibilityModel.RebuildVisibilityCache();
                }
            }

            EditorGUILayout.Space(8);

            if (m.CharacterPrefab)
            {
                if (GUILayout.Button("Refresh Caches"))
                {
                    m.VisibilityModel.RebuildVisibilityCache();
                }
            }

            EditorGUILayout.Space(8);

            if (!m.HasAllInputs)
            {
                EditorGUILayout.HelpBox("Assign Prefab, Clip, and Move Data asset.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Initialize Data"))
                m.BindDataToClipLength(m, tps);

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            DrawControls(m);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Box List", EditorStyles.boldLabel);
            DrawBoxList(m);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Selected Box", EditorStyles.boldLabel);
            DrawSelectedBoxInspector(m);

            EditorGUILayout.Space(8);
            if (m.HasUnsavedChanges)
            {
                EditorGUILayout.HelpBox("You have unsaved changes.", MessageType.Warning);
            }
        }

        public void DrawBottomTimelineLayout(MoveBuilderModel m, int tps)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!m.HasAllInputs)
                {
                    EditorGUILayout.LabelField("Timeline: assign Prefab, Clip, and Move Data.", EditorStyles.miniLabel);
                    return;
                }

                int total = Mathf.Max(1, m.TotalTicks);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("<<", GUILayout.Width(36)))
                        m.SetTick(0);
                    if (GUILayout.Button("<", GUILayout.Width(36)))
                        m.SetTick(m.CurrentTick - 1);

                    int newTick = EditorGUILayout.IntSlider(m.CurrentTick, 0, total - 1);
                    if (newTick != m.CurrentTick)
                        m.SetTick(newTick);

                    if (GUILayout.Button(">", GUILayout.Width(36)))
                        m.SetTick(m.CurrentTick + 1);
                    if (GUILayout.Button(">>", GUILayout.Width(36)))
                        m.SetTick(total - 1);

                    GUILayout.Space(8);

                    float time = m.CurrentTimeSeconds(tps);
                    GUILayout.Label(
                        $"Tick {m.CurrentTick}/{total - 1}  ({time:0.000}s @ {tps}Hz)",
                        EditorStyles.miniLabel,
                        GUILayout.Width(220)
                    );
                }
            }
        }

        private void DrawControls(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            if (GUILayout.Button("Add Hurtbox (Shift A)"))
                m.AddBox(HitboxKind.Hurtbox);
            if (GUILayout.Button("Add Hitbox (A)"))
                m.AddBox(HitboxKind.Hitbox);
            using (new EditorGUI.DisabledScope(m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count))
            {
                if (GUILayout.Button("Duplicate Selected (Ctrl D)"))
                    m.DuplicateSelected();
                if (GUILayout.Button("Delete Selected (Backspace/Delete)"))
                    m.DeleteSelected();
            }
            using (new EditorGUI.DisabledScope(m.CurrentTick <= 0))
            {
                if (GUILayout.Button("Set Hitboxes from Previous Frame (Ctrl F)"))
                    m.SetBoxesFromPreviousFrame();
            }

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count))
            {
                if (GUILayout.Button("Copy Box Props (Ctrl C)"))
                    m.CopySelectedBoxProps();
                using (new EditorGUI.DisabledScope(!m.HasCopiedBoxProps))
                {
                    if (GUILayout.Button("Paste Box Props (Ctrl V)"))
                        m.PasteBoxPropsToSelected();
                }
            }
            if (GUILayout.Button("Copy Frame (Ctrl Shift C)"))
                m.CopyCurrentFrameData();

            using (new EditorGUI.DisabledScope(!m.HasCopiedFrame))
            {
                if (GUILayout.Button("Paste Frame (Ctrl Shift V)"))
                    m.PasteFrameDataToCurrentFrame();
            }
        }

        private void DrawBoxList(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            for (int i = 0; i < frame.Boxes.Count; i++)
            {
                var b = frame.Boxes[i];
                bool sel = i == m.SelectedBoxIndex;
                string label = $"{i}: {b.Props.Kind} - {b.Name}";

                if (GUILayout.Toggle(sel, label, "Button") && !sel)
                {
                    m.SelectBox(i);
                }
            }
        }

        private void DrawSelectedBoxInspector(MoveBuilderModel m)
        {
            var frame = m.GetCurrentFrame();
            if (frame == null)
                return;

            if (m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count)
            {
                EditorGUILayout.HelpBox("Select a box to edit. Drag in preview to move/resize.", MessageType.None);
                return;
            }

            var box = frame.Boxes[m.SelectedBoxIndex];

            box.Name = EditorGUILayout.TextField("Name", box.Name);
            box.CenterLocal = SFloatGUI.Field("Center (Local)", box.CenterLocal);

            box.SizeLocal = SFloatGUI.Field("Size (Local)", box.SizeLocal);
            box.SizeLocal.x = Mathsf.Max((sfloat)0.001f, box.SizeLocal.x);
            box.SizeLocal.y = Mathsf.Max((sfloat)0.001f, box.SizeLocal.y);

            var p = box.Props;
            p.Kind = (HitboxKind)EditorGUILayout.EnumPopup("Kind", p.Kind);

            using (new EditorGUI.DisabledScope(p.Kind != HitboxKind.Hitbox))
            {
                p.Damage = EditorGUILayout.IntField("Damage", p.Damage);
                p.HitstunTicks = EditorGUILayout.IntField("Hitstun (ticks)", p.HitstunTicks);
                p.BlockstunTicks = EditorGUILayout.IntField("Blockstun (ticks)", p.BlockstunTicks);
                p.Knockback = SFloatGUI.Field("Knockback", p.Knockback);
                p.StartsRhythmCombo = EditorGUILayout.Toggle("Starts rhythm combo", p.StartsRhythmCombo);
            }
            box.Props = p;

            m.SetBox(m.SelectedBoxIndex, box);
        }

        public void DrawVisibilityPanelHeader()
        {
            EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
        }

        public void DrawToolbar(MoveBuilderModel m)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Move Builder", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                m.VisibilityModel.ShowVisibilityPanel = GUILayout.Toggle(
                    m.VisibilityModel.ShowVisibilityPanel,
                    "Visibility",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(80)
                );

                using (new EditorGUI.DisabledScope(m == null || !m.HasUnsavedChanges))
                {
                    if (
                        GUILayout.Button(
                            m.HasUnsavedChanges ? "Apply*" : "Apply",
                            EditorStyles.toolbarButton,
                            GUILayout.Width(60)
                        )
                    )
                    {
                        m.SaveAsset();
                        GUI.FocusControl(null);
                    }
                }
            }
        }
    }
}
