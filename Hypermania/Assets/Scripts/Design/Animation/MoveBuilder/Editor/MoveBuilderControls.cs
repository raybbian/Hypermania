using Game;
using Game.View.Fighters;
using UnityEditor;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation.MoveBuilder.Editors
{
    [CustomEditor(typeof(FighterView), true)]
    public sealed class MoveBuilderControls : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MoveBuilder Controls", EditorStyles.boldLabel);

            var fighter = (FighterView)target;
            var m = MoveBuilderModelStore.Get(fighter);
            var animState = MoveBuilderAnimationState.GetAnimState();

            if (!animState.HasValue)
            {
                EditorGUILayout.HelpBox(
                    "Open the Animation window and select an object/clip there to drive the MoveBuilder.",
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
                EditorGUILayout.ObjectField("Move Data (auto)", state.Data, typeof(HitboxData), false);
                EditorGUILayout.IntField("Anim Frame (Animation Window)", state.Tick);
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Bind Data to Clip"))
                m.BindDataToClip(state);
            EditorGUILayout.Space(8);
            DrawControls(m, state);
            EditorGUILayout.Space(8);
            DrawBoxList(m, state);
            EditorGUILayout.Space(8);
            DrawSelectedBoxInspector(m, state);
            EditorGUILayout.Space(8);

            if (m.HasUnsavedChanges(state))
                EditorGUILayout.HelpBox("You have unsaved changes.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(m == null || !m.HasUnsavedChanges(state)))
            {
                if (GUILayout.Button(m.HasUnsavedChanges(state) ? "Apply*" : "Apply"))
                {
                    m.SaveAsset(state);
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawControls(MoveBuilderModel m, MoveBuilderAnimationState state)
        {
            var frame = m.GetCurrentFrame(state);
            if (frame == null)
                return;

            EditorGUILayout.LabelField(
                "Controls (For Keybinds, the MoveBuilderPreview tool must be active)",
                EditorStyles.boldLabel
            );

            if (GUILayout.Button("Add Hurtbox (Shift A)"))
                m.AddBox(state, HitboxKind.Hurtbox);
            if (GUILayout.Button("Add Hitbox (A)"))
                m.AddBox(state, HitboxKind.Hitbox);

            using (new EditorGUI.DisabledScope(m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count))
            {
                if (GUILayout.Button("Duplicate Selected (Ctrl D)"))
                    m.DuplicateSelected(state);
                if (GUILayout.Button("Delete Selected (Backspace/Delete)"))
                    m.DeleteSelected(state);
            }

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count))
            {
                if (GUILayout.Button("Copy Box Props (Ctrl C)"))
                    m.CopySelectedBoxProps(state);
                using (new EditorGUI.DisabledScope(!m.HasCopiedBoxProps))
                {
                    if (GUILayout.Button("Paste Box Props (Ctrl V)"))
                        m.PasteBoxPropsToSelected(state);
                }
            }

            if (GUILayout.Button("Copy Frame (Ctrl Shift C)"))
                m.CopyCurrentFrameData(state);

            using (new EditorGUI.DisabledScope(!m.HasCopiedFrame))
            {
                if (GUILayout.Button("Paste Frame (Ctrl Shift V)"))
                    m.PasteFrameDataToCurrentFrame(state);
            }

            EditorGUILayout.Space(6);
            frame.FrameType = (FrameType)EditorGUILayout.EnumPopup("Frame Type", frame.FrameType);
        }

        private void DrawBoxList(MoveBuilderModel m, MoveBuilderAnimationState state)
        {
            var frame = m.GetCurrentFrame(state);
            if (frame == null)
                return;

            EditorGUILayout.LabelField("Box List", EditorStyles.boldLabel);
            for (int i = 0; i < frame.Boxes.Count; i++)
            {
                var b = frame.Boxes[i];
                bool sel = i == m.SelectedBoxIndex;
                string label = $"{i}: {b.Props.Kind}";

                if (GUILayout.Toggle(sel, label, "Button") && !sel)
                {
                    m.SelectBox(state, i);
                }
            }
        }

        private void DrawSelectedBoxInspector(MoveBuilderModel m, MoveBuilderAnimationState state)
        {
            var frame = m.GetCurrentFrame(state);
            if (frame == null)
                return;

            if (m.SelectedBoxIndex < 0 || m.SelectedBoxIndex >= frame.Boxes.Count)
            {
                EditorGUILayout.HelpBox("Select a box to edit. Drag in preview to move/resize.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Selected Box", EditorStyles.boldLabel);

            var box = frame.Boxes[m.SelectedBoxIndex];

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
                p.HitstopTicks = EditorGUILayout.IntField("Hitstop Ticks", p.HitstopTicks);
                p.Knockback = SFloatGUI.Field("Knockback", p.Knockback);
                p.StartsRhythmCombo = EditorGUILayout.Toggle("Starts rhythm combo", p.StartsRhythmCombo);
            }
            box.Props = p;

            m.SetBox(state, m.SelectedBoxIndex, box);
        }
    }
}
