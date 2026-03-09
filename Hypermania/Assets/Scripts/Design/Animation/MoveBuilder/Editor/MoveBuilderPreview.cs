using System.Collections.Generic;
using Game.View.Fighters;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation.MoveBuilder.Editors
{
    [EditorTool("MoveBuilder Previow", typeof(FighterView))]
    public sealed class MoveBuilderPreview : EditorTool
    {
        public override void OnToolGUI(EditorWindow window)
        {
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

            HandleKeybinds(fighter, m, state);

            FrameData curFrame = m.GetCurrentFrame(state);
            if (curFrame == null)
            {
                ConsumeScenePicking();
                return;
            }

            for (int i = 0; i < curFrame.Boxes.Count; i++)
            {
                DrawAndEditBox(fighter, m, state, curFrame, i);
            }
            HandleBoxSelectionClick(fighter, m, curFrame);
            ConsumeScenePicking();
        }

        private static void HandleKeybinds(FighterView fighter, MoveBuilderModel m, MoveBuilderAnimationState state)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;

            // Don’t steal typing if Unity has a text field focused (rare in SceneView, but safe)
            if (EditorGUIUtility.editingTextField)
                return;

            bool actionKey = EditorGUI.actionKey; // Ctrl on Win/Linux, Cmd on macOS
            bool shift = e.shift;

            bool HasSelection()
            {
                var frame = m.GetCurrentFrame(state);
                return frame != null && m.SelectedBoxIndex >= 0 && m.SelectedBoxIndex < frame.Boxes.Count;
            }

            // Add Hitbox (A), Add Hurtbox (Shift+A)
            if (e.keyCode == KeyCode.A && !actionKey)
            {
                m.AddBox(state, shift ? HitboxKind.Hurtbox : HitboxKind.Hitbox);
                GUI.changed = true;
                e.Use();
                return;
            }

            // Duplicate Selected (Ctrl/Cmd + D)
            if (e.keyCode == KeyCode.D && actionKey)
            {
                if (HasSelection())
                {
                    m.DuplicateSelected(state);
                    GUI.changed = true;
                }
                e.Use();
                return;
            }

            // Delete Selected (Backspace/Delete)
            if (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete)
            {
                if (HasSelection())
                {
                    m.DeleteSelected(state);
                    GUI.changed = true;
                }
                e.Use();
                return;
            }

            // Copy Box Props (Ctrl/Cmd + C)
            if (e.keyCode == KeyCode.C && actionKey && !shift)
            {
                if (HasSelection())
                {
                    m.CopySelectedBoxProps(state);
                    GUI.changed = true;
                }
                e.Use();
                return;
            }

            // Paste Box Props (Ctrl/Cmd + V)
            if (e.keyCode == KeyCode.V && actionKey && !shift)
            {
                if (HasSelection() && m.HasCopiedBoxProps)
                {
                    m.PasteBoxPropsToSelected(state);
                    GUI.changed = true;
                }
                e.Use();
                return;
            }

            // Copy Frame (Ctrl/Cmd + Shift + C)
            if (e.keyCode == KeyCode.C && actionKey && shift)
            {
                m.CopyCurrentFrameData(state);
                GUI.changed = true;
                e.Use();
                return;
            }

            // Paste Frame (Ctrl/Cmd + Shift + V)
            if (e.keyCode == KeyCode.V && actionKey && shift)
            {
                if (m.HasCopiedFrame)
                {
                    m.PasteFrameDataToCurrentFrame(state);
                    GUI.changed = true;
                }
                e.Use();
                return;
            }
        }

        private static void ConsumeScenePicking()
        {
            var e = Event.current;

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
        }

        private void HandleBoxSelectionClick(FighterView fighter, MoveBuilderModel m, FrameData frame)
        {
            var e = Event.current;

            if (e.type != EventType.MouseDown || e.button != 0 || e.alt)
                return;

            int hit = PickBoxIndexUnderMouse(fighter, m, frame, e.mousePosition);

            m.SelectedBoxIndex = hit;
            GUI.changed = true;

            e.Use();
        }

        private int PickBoxIndexUnderMouse(FighterView fighter, MoveBuilderModel m, FrameData frame, Vector2 mousePos)
        {
            Transform root = fighter.transform;

            // Intersect mouse ray with the plane of the hitboxes (root's XY plane).
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);
            Plane plane = new Plane(root.forward, root.position);

            if (!plane.Raycast(ray, out float enter))
                return -1;

            Vector3 hitW = ray.GetPoint(enter);
            Vector3 hitL3 = root.InverseTransformPoint(hitW);
            Vector2 hitL = new Vector2(hitL3.x, hitL3.y);

            // If multiple overlap, pick the last one (not the one currently selected)
            for (int i = frame.Boxes.Count - 1; i >= 0; i--)
            {
                if (m.SelectedBoxIndex == i)
                    continue;
                var box = frame.Boxes[i];
                Vector2 cL = (Vector2)box.CenterLocal;
                Vector2 sL = (Vector2)box.SizeLocal;

                float hx = sL.x * 0.5f;
                float hy = sL.y * 0.5f;

                if (Mathf.Abs(hitL.x - cL.x) <= hx && Mathf.Abs(hitL.y - cL.y) <= hy)
                    return i;
            }

            return -1;
        }

        private void DrawAndEditBox(
            FighterView fighter,
            MoveBuilderModel m,
            MoveBuilderAnimationState state,
            FrameData frame,
            int i
        )
        {
            Transform root = fighter.transform;

            var box = frame.Boxes[i];
            Vector2 cL = (Vector2)box.CenterLocal;
            Vector2 sL = (Vector2)box.SizeLocal;

            Vector3 p0L = new Vector3(cL.x - sL.x * 0.5f, cL.y - sL.y * 0.5f, 0);
            Vector3 p1L = new Vector3(cL.x - sL.x * 0.5f, cL.y + sL.y * 0.5f, 0);
            Vector3 p2L = new Vector3(cL.x + sL.x * 0.5f, cL.y + sL.y * 0.5f, 0);
            Vector3 p3L = new Vector3(cL.x + sL.x * 0.5f, cL.y - sL.y * 0.5f, 0);

            Vector3 p0 = root.TransformPoint(p0L);
            Vector3 p1 = root.TransformPoint(p1L);
            Vector3 p2 = root.TransformPoint(p2L);
            Vector3 p3 = root.TransformPoint(p3L);

            // Outline
            var prev = Handles.color;
            Handles.color = box.Props.Kind == HitboxKind.Hurtbox ? Color.green : Color.red;
            Handles.DrawAAPolyLine(2f, p0, p1, p2, p3, p0);

            // If selected: show move + scale handles
            if (m.SelectedBoxIndex == i)
            {
                EditorGUI.BeginChangeCheck();

                Vector3 centerW = root.TransformPoint(new Vector3(cL.x, cL.y, 0));
                Vector3 newCenterW = Handles.PositionHandle(centerW, Quaternion.identity);

                Vector3 newCenterL3 = root.InverseTransformPoint(newCenterW);
                Vector2 newCenterL = new Vector2(newCenterL3.x, newCenterL3.y);

                Vector2 resizeCenterL = newCenterL;
                Vector2 resizeSizeL = sL;

                DrawRectHandles8AndResize(root, ref resizeCenterL, ref resizeSizeL, minSize: 0.001f);

                if (EditorGUI.EndChangeCheck())
                {
                    box.CenterLocal = (SVector2)resizeCenterL;
                    box.SizeLocal = (SVector2)resizeSizeL;
                    m.SetBox(state, i, box);
                }

                if (box.Props.Kind == HitboxKind.Hitbox)
                {
                    DrawAndEditKnockbackArrow(fighter, m, state, frame, i);
                }
            }

            Handles.color = prev;
        }

        private void DrawAndEditKnockbackArrow(
            FighterView fighter,
            MoveBuilderModel m,
            MoveBuilderAnimationState state,
            FrameData frame,
            int index
        )
        {
            Transform root = fighter.transform;

            var box = frame.Boxes[index];
            if (box.Props.Kind != HitboxKind.Hitbox)
                return;

            var props = box.Props;

            Vector2 centerL = (Vector2)box.CenterLocal;
            Vector2 kbL = (Vector2)props.Knockback;
            Vector2 tipL = centerL + kbL;

            Vector3 centerW = root.TransformPoint(new Vector3(centerL.x, centerL.y, 0f));
            Vector3 tipW = root.TransformPoint(new Vector3(tipL.x, tipL.y, 0f));

            // Render arrow
            var prev = Handles.color;
            Handles.color = Color.red;
            Handles.DrawAAPolyLine(2f, centerW, tipW);
            Handles.ArrowHandleCap(
                0,
                tipW,
                Quaternion.LookRotation(Vector3.forward, (tipW - centerW).normalized),
                HandleUtility.GetHandleSize(tipW) * 0.4f,
                EventType.Repaint
            );

            EditorGUI.BeginChangeCheck();
            Vector3 newTipW = Handles.Slider2D(
                tipW,
                root.forward, // plane normal
                root.right, // plane X axis
                root.up, // plane Y axis
                HandleUtility.GetHandleSize(tipW) * 0.08f,
                Handles.DotHandleCap,
                snap: Vector2.zero
            );

            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newTipL3 = root.InverseTransformPoint(newTipW);
                Vector2 newTipL = new Vector2(newTipL3.x, newTipL3.y);

                box.Props.Knockback = (SVector2)(newTipL - centerL);

                m.SetBox(state, index, box);
            }

            Handles.color = prev;
        }

        private enum RectHandle8
        {
            None,
            L,
            R,
            B,
            T,
            BL,
            BR,
            TL,
            TR,
        }

        private static RectHandle8 DrawRectHandles8AndResize(
            Transform root,
            ref Vector2 centerL,
            ref Vector2 sizeL,
            float minSize = 0.001f
        )
        {
            float hx = sizeL.x * 0.5f;
            float hy = sizeL.y * 0.5f;

            float left = centerL.x - hx;
            float right = centerL.x + hx;
            float bottom = centerL.y - hy;
            float top = centerL.y + hy;

            Vector2 pBL = new Vector2(left, bottom);
            Vector2 pBR = new Vector2(right, bottom);
            Vector2 pTL = new Vector2(left, top);
            Vector2 pTR = new Vector2(right, top);

            Vector2 pL = new Vector2(left, centerL.y);
            Vector2 pR = new Vector2(right, centerL.y);
            Vector2 pB = new Vector2(centerL.x, bottom);
            Vector2 pT = new Vector2(centerL.x, top);

            Vector3 wBL = root.TransformPoint(new Vector3(pBL.x, pBL.y, 0));
            Vector3 wBR = root.TransformPoint(new Vector3(pBR.x, pBR.y, 0));
            Vector3 wTL = root.TransformPoint(new Vector3(pTL.x, pTL.y, 0));
            Vector3 wTR = root.TransformPoint(new Vector3(pTR.x, pTR.y, 0));

            Vector3 wL = root.TransformPoint(new Vector3(pL.x, pL.y, 0));
            Vector3 wR = root.TransformPoint(new Vector3(pR.x, pR.y, 0));
            Vector3 wB = root.TransformPoint(new Vector3(pB.x, pB.y, 0));
            Vector3 wT = root.TransformPoint(new Vector3(pT.x, pT.y, 0));

            Vector3 planeNormal = root.forward;
            Vector3 axisX = root.right;
            Vector3 axisY = root.up;

            float HandleSizeAt(Vector3 w) => HandleUtility.GetHandleSize(w) * 0.07f;

            bool fromCenter = Event.current.alt; // Alt: symmetric resize about center
            bool keepAspect = Event.current.shift; // Shift: preserve aspect ratio (approx)

            // Draw / drag one handle; return new world pos if changed.
            static Vector3 Drag2D(Vector3 posW, Vector3 n, Vector3 x, Vector3 y, float size)
            {
                return Handles.Slider2D(posW, n, x, y, size, Handles.DotHandleCap, snap: Vector2.zero);
            }

            RectHandle8 changed = RectHandle8.None;
            Vector3 oBL = wBL,
                oBR = wBR,
                oTL = wTL,
                oTR = wTR,
                oL = wL,
                oR = wR,
                oB = wB,
                oT = wT;

            EditorGUI.BeginChangeCheck();

            wBL = Drag2D(wBL, planeNormal, axisX, axisY, HandleSizeAt(wBL));
            wBR = Drag2D(wBR, planeNormal, axisX, axisY, HandleSizeAt(wBR));
            wTL = Drag2D(wTL, planeNormal, axisX, axisY, HandleSizeAt(wTL));
            wTR = Drag2D(wTR, planeNormal, axisX, axisY, HandleSizeAt(wTR));

            wL = Drag2D(wL, planeNormal, axisX, axisY, HandleSizeAt(wL));
            wR = Drag2D(wR, planeNormal, axisX, axisY, HandleSizeAt(wR));
            wB = Drag2D(wB, planeNormal, axisX, axisY, HandleSizeAt(wB));
            wT = Drag2D(wT, planeNormal, axisX, axisY, HandleSizeAt(wT));

            bool any = EditorGUI.EndChangeCheck();
            if (!any)
                return RectHandle8.None;

            bool Moved(Vector3 a, Vector3 b) => (a - b).sqrMagnitude > 1e-10f;

            if (Moved(wBL, oBL))
                changed = RectHandle8.BL;
            else if (Moved(wBR, oBR))
                changed = RectHandle8.BR;
            else if (Moved(wTL, oTL))
                changed = RectHandle8.TL;
            else if (Moved(wTR, oTR))
                changed = RectHandle8.TR;
            else if (Moved(wL, oL))
                changed = RectHandle8.L;
            else if (Moved(wR, oR))
                changed = RectHandle8.R;
            else if (Moved(wB, oB))
                changed = RectHandle8.B;
            else if (Moved(wT, oT))
                changed = RectHandle8.T;

            Vector2 ToLocal2(Vector3 w)
            {
                Vector3 l = root.InverseTransformPoint(w);
                return new Vector2(l.x, l.y);
            }

            float newLeft = left,
                newRight = right,
                newBottom = bottom,
                newTop = top;

            switch (changed)
            {
                case RectHandle8.L:
                    newLeft = ToLocal2(wL).x;
                    break;
                case RectHandle8.R:
                    newRight = ToLocal2(wR).x;
                    break;
                case RectHandle8.B:
                    newBottom = ToLocal2(wB).y;
                    break;
                case RectHandle8.T:
                    newTop = ToLocal2(wT).y;
                    break;

                case RectHandle8.BL:
                {
                    var p = ToLocal2(wBL);
                    newLeft = p.x;
                    newBottom = p.y;
                    break;
                }
                case RectHandle8.BR:
                {
                    var p = ToLocal2(wBR);
                    newRight = p.x;
                    newBottom = p.y;
                    break;
                }
                case RectHandle8.TL:
                {
                    var p = ToLocal2(wTL);
                    newLeft = p.x;
                    newTop = p.y;
                    break;
                }
                case RectHandle8.TR:
                {
                    var p = ToLocal2(wTR);
                    newRight = p.x;
                    newTop = p.y;
                    break;
                }
            }

            if (fromCenter)
            {
                if (
                    changed
                    is RectHandle8.L
                        or RectHandle8.R
                        or RectHandle8.BL
                        or RectHandle8.TL
                        or RectHandle8.BR
                        or RectHandle8.TR
                )
                {
                    float halfX =
                        (changed == RectHandle8.L || changed == RectHandle8.BL || changed == RectHandle8.TL)
                            ? (centerL.x - newLeft)
                            : (newRight - centerL.x);

                    halfX = Mathf.Abs(halfX);
                    newLeft = centerL.x - halfX;
                    newRight = centerL.x + halfX;
                }

                if (
                    changed
                    is RectHandle8.B
                        or RectHandle8.T
                        or RectHandle8.BL
                        or RectHandle8.BR
                        or RectHandle8.TL
                        or RectHandle8.TR
                )
                {
                    float halfY =
                        (changed == RectHandle8.B || changed == RectHandle8.BL || changed == RectHandle8.BR)
                            ? (centerL.y - newBottom)
                            : (newTop - centerL.y);

                    halfY = Mathf.Abs(halfY);
                    newBottom = centerL.y - halfY;
                    newTop = centerL.y + halfY;
                }
            }

            if (newLeft > newRight)
                (newLeft, newRight) = (newRight, newLeft);
            if (newBottom > newTop)
                (newBottom, newTop) = (newTop, newBottom);

            if (keepAspect)
            {
                float aspect = (sizeL.y <= 1e-6f) ? 1f : (sizeL.x / sizeL.y);
                float w = Mathf.Max(minSize, newRight - newLeft);
                float h = Mathf.Max(minSize, newTop - newBottom);

                // If user is primarily changing X (left/right/corner), derive Y; else derive X.
                bool drivingX =
                    changed
                    is RectHandle8.L
                        or RectHandle8.R
                        or RectHandle8.BL
                        or RectHandle8.BR
                        or RectHandle8.TL
                        or RectHandle8.TR;

                if (drivingX)
                {
                    float targetH = Mathf.Max(minSize, w / Mathf.Max(1e-6f, aspect));
                    float cy = 0.5f * (newBottom + newTop);
                    newBottom = cy - targetH * 0.5f;
                    newTop = cy + targetH * 0.5f;
                }
                else
                {
                    float targetW = Mathf.Max(minSize, h * aspect);
                    float cx = 0.5f * (newLeft + newRight);
                    newLeft = cx - targetW * 0.5f;
                    newRight = cx + targetW * 0.5f;
                }
            }

            // Enforce min size.
            float finalW = Mathf.Max(minSize, newRight - newLeft);
            float finalH = Mathf.Max(minSize, newTop - newBottom);

            // Recompute center/size.
            centerL = new Vector2((newLeft + newRight) * 0.5f, (newBottom + newTop) * 0.5f);
            sizeL = new Vector2(finalW, finalH);

            return changed;
        }
    }
}
