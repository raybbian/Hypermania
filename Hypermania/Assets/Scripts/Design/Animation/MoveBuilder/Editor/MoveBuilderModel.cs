using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation.MoveBuilder.Editors
{
    public static class MoveBuilderModelStore
    {
        private static readonly Dictionary<string, MoveBuilderModel> _states = new();

        public static string KeyFor(UnityEngine.Object o)
        {
            if (!o)
                return "null";
            // GlobalObjectId is stable for assets and scene objects.
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(o);
            return gid.ToString();
        }

        public static MoveBuilderModel Get(UnityEngine.Object owner)
        {
            string key = KeyFor(owner);
            if (!_states.TryGetValue(key, out var s))
            {
                s = new MoveBuilderModel();
                _states[key] = s;
            }
            return s;
        }

        public static void Remove(UnityEngine.Object owner)
        {
            _states.Remove(KeyFor(owner));
        }
    }

    [Serializable]
    public sealed class MoveBuilderModel
    {
        public int SelectedBoxIndex = -1;
        private HitboxData _lastData;
        private int _savedValueHash;

        public bool HasUnsavedChanges(MoveBuilderAnimationState state)
        {
            if (!state.Data)
                return false;

            if (!ReferenceEquals(state.Data, _lastData))
            {
                _lastData = state.Data;
                _savedValueHash = state.Data.GetHashCode();
                return false;
            }

            return state.Data.GetHashCode() != _savedValueHash;
        }

        public MoveBuilderModel()
        {
            SelectedBoxIndex = -1;
        }

        #region Modifications

        public void BindDataToClip(MoveBuilderAnimationState state)
        {
            RecordUndo(state, "Bind Data to Clip");

            if (state.Data.BindToClip(state.Clip))
            {
                MarkDirty(state);
            }
        }

        public FrameData GetCurrentFrame(MoveBuilderAnimationState state)
        {
            if (!state.Data)
                return null;
            return state.Data.GetFrame(state.Tick);
        }

        public void SelectBox(MoveBuilderAnimationState state, int index)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;

            int max = frame.Boxes == null ? frame.Boxes.Count - 1 : -1;
            if (index > max || index < -1)
                SelectedBoxIndex = -1;
            else
                SelectedBoxIndex = index;
        }

        public void AddBox(MoveBuilderAnimationState state, HitboxKind kind)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;

            RecordUndo(state, "Add Box");

            var b = new BoxData
            {
                CenterLocal = SVector2.zero,
                SizeLocal = new SVector2((sfloat)0.5f, (sfloat)0.5f),
                Props = new BoxProps
                {
                    Kind = kind,
                    HitstunTicks = kind == HitboxKind.Hitbox ? 12 : 0,
                    Knockback = new SVector2(1, 0),
                    StartsRhythmCombo = false,
                },
            };

            frame.Boxes.Add(b);
            SelectedBoxIndex = frame.Boxes.Count - 1;

            MarkDirty(state);
        }

        public void DuplicateSelected(MoveBuilderAnimationState state)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;
            if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
                return;

            RecordUndo(state, "Duplicate Box");

            var copy = frame.Boxes[SelectedBoxIndex];
            frame.Boxes.Add(copy);
            SelectedBoxIndex = frame.Boxes.Count - 1;

            MarkDirty(state);
        }

        public void DeleteSelected(MoveBuilderAnimationState state)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;
            if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
                return;

            RecordUndo(state, "Delete Box");

            frame.Boxes.RemoveAt(SelectedBoxIndex);
            SelectedBoxIndex = -1;

            MarkDirty(state);
        }

        public void SetBox(MoveBuilderAnimationState state, int index, BoxData updated)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;
            if (index < 0 || index >= frame.Boxes.Count)
                return;

            var cur = frame.Boxes[index];
            if (cur == updated)
                return;

            RecordUndo(state, "Edit Box");

            frame.Boxes[index] = updated;

            MarkDirty(state);
        }

        private bool _hasCopiedBoxProps;
        private BoxProps _copiedBoxProps;
        public bool HasCopiedBoxProps => _hasCopiedBoxProps;

        public void CopySelectedBoxProps(MoveBuilderAnimationState state)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;
            if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
                return;

            _copiedBoxProps = frame.Boxes[SelectedBoxIndex].Props;
            _hasCopiedBoxProps = true;
        }

        public void PasteBoxPropsToSelected(MoveBuilderAnimationState state)
        {
            if (!_hasCopiedBoxProps)
                return;

            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;
            if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
                return;

            var cur = frame.Boxes[SelectedBoxIndex];
            if (cur.Props == _copiedBoxProps)
                return;

            RecordUndo(state, "Paste Box Props");

            cur.Props = _copiedBoxProps;
            frame.Boxes[SelectedBoxIndex] = cur;

            MarkDirty(state);
        }

        private bool _hasCopiedFrame;
        private FrameData _copiedFrame;

        public bool HasCopiedFrame => _hasCopiedFrame;

        public void CopyCurrentFrameData(MoveBuilderAnimationState state)
        {
            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;

            _copiedFrame = frame.Clone();
            _hasCopiedFrame = true;
        }

        public void PasteFrameDataToCurrentFrame(MoveBuilderAnimationState state)
        {
            if (!_hasCopiedFrame)
                return;

            var frame = GetCurrentFrame(state);
            if (frame == null)
                return;

            RecordUndo(state, "Paste Frame Data");

            frame.CopyFrom(_copiedFrame);

            if (SelectedBoxIndex >= frame.Boxes.Count)
                SelectedBoxIndex = frame.Boxes.Count - 1;
            if (frame.Boxes.Count == 0)
                SelectedBoxIndex = -1;

            MarkDirty(state);
        }
        #endregion


        #region Helpers
        public void SaveAsset(MoveBuilderAnimationState state)
        {
            if (!state.Data)
                return;

            EditorUtility.SetDirty(state.Data);
            AssetDatabase.SaveAssets();

            _lastData = state.Data;
            _savedValueHash = state.Data.GetHashCode();
        }

        private void MarkDirty(MoveBuilderAnimationState state)
        {
            if (state.Data)
            {
                EditorUtility.SetDirty(state.Data);
            }
        }

        private void RecordUndo(MoveBuilderAnimationState state, string label)
        {
            if (state.Data)
                Undo.RecordObject(state.Data, label);
        }
        #endregion
    }
}
