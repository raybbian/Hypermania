// using System;
// using System.Collections.Generic;
// using Game;
// using Game.View;
// using UnityEditor;
// using UnityEngine;
// using Hypermania.Game;
// using Hypermania.Game.Configs;
// using Hypermania.Shared.SoftFloat;
// using Hypermania.Shared;
//
// namespace Design.Animation.MoveBuilder.Editor
// {
//     public static class MoveBuilderModelStore
//     {
//         private static readonly Dictionary<string, MoveBuilderModel> States = new();
//
//         private static string KeyFor(UnityEngine.Object o)
//         {
//             if (!o)
//                 return "null";
//             // GlobalObjectId is stable for assets and scene objects.
//             var gid = GlobalObjectId.GetGlobalObjectIdSlow(o);
//             return gid.ToString();
//         }
//
//         public static MoveBuilderModel Get(UnityEngine.Object owner)
//         {
//             string key = KeyFor(owner);
//             if (!States.TryGetValue(key, out var s))
//             {
//                 s = new MoveBuilderModel();
//                 States[key] = s;
//             }
//             return s;
//         }
//
//         public static void Remove(UnityEngine.Object owner)
//         {
//             States.Remove(KeyFor(owner));
//         }
//     }
//
//     [Serializable]
//     public sealed class MoveBuilderModel
//     {
//         public int SelectedBoxIndex;
//         public Transform RootMotionSource;
//         private HitboxData _lastData;
//         private int _savedValueHash;
//
//         public bool HasUnsavedChanges(MoveBuilderAnimationState state)
//         {
//             if (!state.Data)
//                 return false;
//
//             if (!ReferenceEquals(state.Data, _lastData))
//             {
//                 _lastData = state.Data;
//                 _savedValueHash = ComputeValueHash(state.Data);
//                 return false;
//             }
//
//             return ComputeValueHash(state.Data) != _savedValueHash;
//         }
//
//         // Editor-only "is dirty" hash. Compares the values that the inspector
//         // can mutate so we know when to flag the asset dirty.
//         static int ComputeValueHash(HitboxData data)
//         {
//             var hc = new HashCode();
//             hc.Add(data.ClipName);
//             hc.Add(data.AnimLoops);
//             hc.Add(data.IgnoreOwner);
//             hc.Add(data.ApplyRootMotion);
//             hc.Add(data.Frames != null ? data.Frames.Count : 0);
//             if (data.Frames != null)
//             {
//                 for (int i = 0; i < data.Frames.Count; i++)
//                 {
//                     var f = data.Frames[i];
//                     hc.Add(f.Boxes != null ? f.Boxes.Count : 0);
//                     if (f.Boxes != null)
//                     {
//                         for (int j = 0; j < f.Boxes.Count; j++)
//                             hc.Add(f.Boxes[j]);
//                     }
//                     hc.Add(f.FrameType);
//                     hc.Add(f.Floating);
//                     hc.Add(f.ShouldApplyVel);
//                     hc.Add(f.ApplyVelocity);
//                     hc.Add(f.ShouldTeleport);
//                     hc.Add(f.TeleportLocation);
//                     hc.Add(f.GravityEnabled);
//                     hc.Add(f.RootMotionOffset);
//                 }
//             }
//             return hc.ToHashCode();
//         }
//
//         public MoveBuilderModel()
//         {
//             SelectedBoxIndex = -1;
//         }
//
//         #region Modifications
//
//         public void BindDataToClip(MoveBuilderAnimationState state, EntityView fighter)
//         {
//             RecordUndo(state, "Bind Data to Clip");
//
//             int totalTicks = Mathf.CeilToInt(state.Clip.length * SimConstants.TPS) + 1;
//             bool changed = state.Data.ApplyClipMeta(state.Clip.name, totalTicks, state.Clip.isLooping);
//
//             if (RootMotionSource != null && fighter != null)
//             {
//                 BakeRootMotion(state, fighter);
//                 changed = true;
//             }
//
//             if (changed)
//             {
//                 MarkDirty(state);
//             }
//         }
//
//         private void BakeRootMotion(MoveBuilderAnimationState state, EntityView fighter)
//         {
//             int total = state.Data.TotalTicks;
//             var world = new Vector3[total];
//             for (int i = 0; i < total; i++)
//             {
//                 float time = i / (float)GameManager.TPS;
//                 state.Clip.SampleAnimation(fighter.gameObject, time);
//                 world[i] = RootMotionSource.position;
//             }
//
//             state.Data.Frames[0].RootMotionOffset = SVector2.zero;
//             for (int i = 1; i < total; i++)
//             {
//                 float dx = world[i].x - world[0].x;
//                 float dy = world[i].y - world[0].y;
//                 state.Data.Frames[i].RootMotionOffset = new SVector2((sfloat)dx, (sfloat)dy);
//             }
//
//             float restoreTime = state.Tick / (float)GameManager.TPS;
//             state.Clip.SampleAnimation(fighter.gameObject, restoreTime);
//         }
//
//         public FrameData GetCurrentFrame(MoveBuilderAnimationState state)
//         {
//             if (!state.Data)
//                 return null;
//             if (state.Tick < 0 || state.Tick >= state.Data.TotalTicks)
//                 return null;
//             return state.Data.GetFrame(state.Tick);
//         }
//
//         public void SelectBox(MoveBuilderAnimationState state, int index)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null || frame.Boxes == null)
//                 return;
//
//             int max = frame.Boxes != null ? frame.Boxes.Count - 1 : -1;
//             if (index > max || index < -1)
//                 SelectedBoxIndex = -1;
//             else
//                 SelectedBoxIndex = index;
//         }
//
//         public void AddBox(MoveBuilderAnimationState state, HitboxKind kind)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//
//             RecordUndo(state, "Add Box");
//
//             var b = new BoxData
//             {
//                 CenterLocal = SVector2.zero,
//                 SizeLocal = new SVector2((sfloat)0.5f, (sfloat)0.5f),
//                 Props = new BoxProps
//                 {
//                     Kind = kind,
//                     HitstunTicks = kind == HitboxKind.Hitbox ? 12 : 0,
//                     Knockback = kind == HitboxKind.Hitbox ? new SVector2(1, 0) : SVector2.zero,
//                     GrabPosition = kind == HitboxKind.Grabbox ? new SVector2(1, 0) : SVector2.zero,
//                     GrabsGrounded = kind == HitboxKind.Grabbox,
//                     GrabsAirborne = kind == HitboxKind.Grabbox,
//                 },
//             };
//
//             frame.Boxes.Add(b);
//             SelectedBoxIndex = frame.Boxes.Count - 1;
//
//             MarkDirty(state);
//         }
//
//         public void DuplicateSelected(MoveBuilderAnimationState state)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//             if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
//                 return;
//
//             RecordUndo(state, "Duplicate Box");
//
//             var copy = frame.Boxes[SelectedBoxIndex];
//             frame.Boxes.Add(copy);
//             SelectedBoxIndex = frame.Boxes.Count - 1;
//
//             MarkDirty(state);
//         }
//
//         public void DeleteSelected(MoveBuilderAnimationState state)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//             if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
//                 return;
//
//             RecordUndo(state, "Delete Box");
//
//             frame.Boxes.RemoveAt(SelectedBoxIndex);
//             SelectedBoxIndex = -1;
//
//             MarkDirty(state);
//         }
//
//         public void SetBox(MoveBuilderAnimationState state, int index, BoxData updated)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//             if (index < 0 || index >= frame.Boxes.Count)
//                 return;
//
//             var cur = frame.Boxes[index];
//             if (cur == updated)
//                 return;
//
//             RecordUndo(state, "Edit Box");
//
//             frame.Boxes[index] = updated;
//
//             MarkDirty(state);
//         }
//
//         private bool _hasCopiedBoxProps;
//         private BoxProps _copiedBoxProps;
//         public bool HasCopiedBoxProps => _hasCopiedBoxProps;
//
//         public void CopySelectedBoxProps(MoveBuilderAnimationState state)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//             if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
//                 return;
//
//             _copiedBoxProps = frame.Boxes[SelectedBoxIndex].Props;
//             _hasCopiedBoxProps = true;
//         }
//
//         public void PasteBoxPropsToSelected(MoveBuilderAnimationState state)
//         {
//             if (!_hasCopiedBoxProps)
//                 return;
//
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//             if (SelectedBoxIndex < 0 || SelectedBoxIndex >= frame.Boxes.Count)
//                 return;
//
//             var cur = frame.Boxes[SelectedBoxIndex];
//             if (cur.Props == _copiedBoxProps)
//                 return;
//
//             RecordUndo(state, "Paste Box Props");
//
//             cur.Props = _copiedBoxProps;
//             frame.Boxes[SelectedBoxIndex] = cur;
//
//             MarkDirty(state);
//         }
//
//         private bool _hasCopiedFrame;
//         private FrameData _copiedFrame;
//
//         public bool HasCopiedFrame => _hasCopiedFrame;
//
//         public void CopyCurrentFrameData(MoveBuilderAnimationState state)
//         {
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//
//             _copiedFrame = frame.Clone();
//             _hasCopiedFrame = true;
//         }
//
//         public void PasteFrameDataToCurrentFrame(MoveBuilderAnimationState state)
//         {
//             if (!_hasCopiedFrame)
//                 return;
//
//             var frame = GetCurrentFrame(state);
//             if (frame == null)
//                 return;
//
//             RecordUndo(state, "Paste Frame Data");
//
//             frame.CopyFrom(_copiedFrame);
//
//             if (SelectedBoxIndex >= frame.Boxes.Count)
//                 SelectedBoxIndex = frame.Boxes.Count - 1;
//             if (frame.Boxes.Count == 0)
//                 SelectedBoxIndex = -1;
//
//             MarkDirty(state);
//         }
//         #endregion
//
//
//         #region Helpers
//         public void SaveAsset(MoveBuilderAnimationState state)
//         {
//             if (!state.Data)
//                 return;
//
//             EditorUtility.SetDirty(state.Data);
//             AssetDatabase.SaveAssets();
//
//             _lastData = state.Data;
//             _savedValueHash = ComputeValueHash(state.Data);
//         }
//
//         private void MarkDirty(MoveBuilderAnimationState state)
//         {
//             if (state.Data)
//             {
//                 EditorUtility.SetDirty(state.Data);
//             }
//         }
//
//         private void RecordUndo(MoveBuilderAnimationState state, string label)
//         {
//             if (state.Data)
//                 Undo.RecordObject(state.Data, label);
//         }
//         #endregion
//     }
// }
