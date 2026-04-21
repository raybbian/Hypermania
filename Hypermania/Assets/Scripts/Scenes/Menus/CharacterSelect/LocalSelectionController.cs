using System;
using Game.Sim;
using UnityEngine.InputSystem;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Polls a single <see cref="InputDevice"/> for nav edges (L/R/U/D/Confirm/Back)
    /// and applies them to the per-player <see cref="PlayerSelectionState"/>.
    /// Input is polled directly from the device (no <see cref="Design.Configs.ControlsConfig"/>
    /// binding) because CharacterSelect is where the player is still choosing
    /// their controls preset — same rationale as
    /// <see cref="InputSelect.DeviceManager.HandlePlayerInputSelect"/>.
    /// </summary>
    public class LocalSelectionController
    {
        private const float AxisThreshold = 0.75f;

        private readonly InputDevice _device;
        private bool _prevLeft;
        private bool _prevRight;
        private bool _prevUp;
        private bool _prevDown;

        public LocalSelectionController(InputDevice device)
        {
            _device = device;
        }

        public EdgeSet PollEdges()
        {
            PollEdges(out bool left, out bool right, out bool up, out bool down, out bool confirm, out bool back);
            return new EdgeSet(left, right, up, down, confirm, back);
        }

        public void Apply(PlayerSelectionState state, in SelectionContext ctx, in EdgeSet edges)
        {
            switch (state.Phase)
            {
                case SelectPhase.Character:
                    HandleCharacter(state, ctx, edges.Left, edges.Right, edges.Confirm);
                    break;
                case SelectPhase.Options:
                    HandleOptions(state, ctx, edges.Left, edges.Right, edges.Up, edges.Down, edges.Confirm, edges.Back);
                    break;
                case SelectPhase.Confirmed:
                    if (edges.Back)
                    {
                        state.Phase = SelectPhase.Options;
                    }
                    break;
            }
        }

        private static void HandleCharacter(
            PlayerSelectionState state,
            in SelectionContext ctx,
            bool left,
            bool right,
            bool confirm
        )
        {
            int totalSlots = ctx.CharacterCount + (ctx.HasRandomSlot ? 1 : 0);
            int randomIdx = ctx.CharacterCount;

            if (totalSlots > 0 && (left || right))
            {
                int dir = left ? -1 : 1;
                for (int step = 1; step <= totalSlots; step++)
                {
                    int c = ((state.CharacterIndex + dir * step) % totalSlots + totalSlots) % totalSlots;
                    if (ctx.HasRandomSlot && c == randomIdx)
                    {
                        state.CharacterIndex = c;
                        state.SkinIndex = 0;
                        break;
                    }
                    int skin = FirstFreeSkin(c, ctx);
                    if (skin >= 0)
                    {
                        state.CharacterIndex = c;
                        state.SkinIndex = skin;
                        break;
                    }
                }
            }

            if (confirm && totalSlots > 0)
            {
                state.Phase = SelectPhase.Options;
                state.OptionsRow = 0;
                ClampSkin(state, ctx);
                ClampControls(state, ctx);
                // Both slots in Character phase don't block each other, so
                // they can share a (char, skin). If the other slot already
                // confirmed onto our skin while we were sitting on it, bump
                // off so we never enter Options on a colliding slot.
                if (state.CharacterIndex < ctx.CharacterCount && ctx.IsTaken(state.CharacterIndex, state.SkinIndex))
                {
                    int free = FirstFreeSkin(state.CharacterIndex, ctx);
                    if (free >= 0)
                        state.SkinIndex = free;
                }
            }
        }

        private static void HandleOptions(
            PlayerSelectionState state,
            in SelectionContext ctx,
            bool left,
            bool right,
            bool up,
            bool down,
            bool confirm,
            bool back
        )
        {
            if (up)
            {
                state.OptionsRow = PrevVisibleRow(state.OptionsRow, ctx);
            }
            else if (down)
            {
                state.OptionsRow = NextVisibleRow(state.OptionsRow, ctx);
            }

            if ((left || right) && ctx.IsRowInteractable(state.OptionsRow))
            {
                int delta = left ? -1 : 1;
                CycleRow(state, ctx, state.OptionsRow, delta);
                ClampSkin(state, ctx);
                ClampControls(state, ctx);
            }

            if (confirm)
            {
                state.Phase = SelectPhase.Confirmed;
            }
            else if (back)
            {
                state.Phase = SelectPhase.Character;
            }
        }

        private static void CycleRow(PlayerSelectionState state, in SelectionContext ctx, int row, int delta)
        {
            switch (row)
            {
                case OptionsRows.ComboMode:
                    state.ComboMode = state.ComboMode == ComboMode.Assisted ? ComboMode.Freestyle : ComboMode.Assisted;
                    break;
                case OptionsRows.Skin:
                    if (ctx.SkinCount > 0)
                    {
                        for (int step = 1; step <= ctx.SkinCount; step++)
                        {
                            int s = ((state.SkinIndex + delta * step) % ctx.SkinCount + ctx.SkinCount) % ctx.SkinCount;
                            if (!ctx.IsTaken(state.CharacterIndex, s))
                            {
                                state.SkinIndex = s;
                                break;
                            }
                        }
                    }
                    break;
                case OptionsRows.ManiaDifficulty:
                    state.ManiaDifficulty =
                        state.ManiaDifficulty == ManiaDifficulty.Normal ? ManiaDifficulty.Hard : ManiaDifficulty.Normal;
                    break;
                case OptionsRows.BeatCancel:
                    state.BeatCancelWindow = CycleBeatCancel(state.BeatCancelWindow, delta);
                    break;
                case OptionsRows.ControlsPreset:
                    if (ctx.ControlsPresetCount > 0)
                    {
                        state.ControlsIndex =
                            ((state.ControlsIndex + delta) % ctx.ControlsPresetCount + ctx.ControlsPresetCount)
                            % ctx.ControlsPresetCount;
                    }
                    break;
            }
        }

        private static BeatCancelWindow CycleBeatCancel(BeatCancelWindow current, int delta)
        {
            // Order by intended difficulty progression: Easy → Medium → Hard.
            BeatCancelWindow[] order = { BeatCancelWindow.Medium, BeatCancelWindow.Hard };
            int idx = Array.IndexOf(order, current);
            if (idx < 0)
                idx = 1;
            int next = ((idx + delta) % order.Length + order.Length) % order.Length;
            return order[next];
        }

        private static int NextVisibleRow(int current, in SelectionContext ctx)
        {
            for (int step = 1; step <= OptionsRows.Count; step++)
            {
                int candidate = (current + step) % OptionsRows.Count;
                if (ctx.IsRowInteractable(candidate))
                    return candidate;
            }
            return current;
        }

        private static int PrevVisibleRow(int current, in SelectionContext ctx)
        {
            for (int step = 1; step <= OptionsRows.Count; step++)
            {
                int candidate = (current - step + OptionsRows.Count) % OptionsRows.Count;
                if (ctx.IsRowInteractable(candidate))
                    return candidate;
            }
            return current;
        }

        private static int FirstFreeSkin(int charIdx, in SelectionContext ctx)
        {
            int count = ctx.SkinCountForChar != null ? ctx.SkinCountForChar(charIdx) : 0;
            if (count <= 0)
                return -1;
            for (int s = 0; s < count; s++)
            {
                if (!ctx.IsTaken(charIdx, s))
                    return s;
            }
            return -1;
        }

        private static void ClampSkin(PlayerSelectionState state, in SelectionContext ctx)
        {
            if (ctx.SkinCount <= 0)
            {
                state.SkinIndex = 0;
                return;
            }
            if (state.SkinIndex < 0 || state.SkinIndex >= ctx.SkinCount)
            {
                state.SkinIndex = 0;
            }
        }

        private static void ClampControls(PlayerSelectionState state, in SelectionContext ctx)
        {
            if (ctx.ControlsPresetCount <= 0)
            {
                state.ControlsIndex = 0;
                return;
            }
            if (state.ControlsIndex < 0 || state.ControlsIndex >= ctx.ControlsPresetCount)
            {
                state.ControlsIndex = 0;
            }
        }

        private void PollEdges(
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            left = right = up = down = confirm = back = false;

            switch (_device)
            {
                case Gamepad gp:
                    PollGamepad(gp, out left, out right, out up, out down, out confirm, out back);
                    break;
                case Keyboard kb:
                    PollKeyboard(kb, out left, out right, out up, out down, out confirm, out back);
                    break;
            }
        }

        private void PollGamepad(
            Gamepad gp,
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            // DPad edges are clean button presses; stick edges need previous-frame latching.
            bool leftStickLeft = gp.leftStick.x.value < -AxisThreshold;
            bool leftStickRight = gp.leftStick.x.value > AxisThreshold;
            bool leftStickUp = gp.leftStick.y.value > AxisThreshold;
            bool leftStickDown = gp.leftStick.y.value < -AxisThreshold;

            left =
                gp.dpad.left.wasPressedThisFrame
                || gp.leftShoulder.wasPressedThisFrame
                || gp.leftTrigger.wasPressedThisFrame
                || (leftStickLeft && !_prevLeft);
            right =
                gp.dpad.right.wasPressedThisFrame
                || gp.rightShoulder.wasPressedThisFrame
                || gp.rightTrigger.wasPressedThisFrame
                || (leftStickRight && !_prevRight);
            up = gp.dpad.up.wasPressedThisFrame || (leftStickUp && !_prevUp);
            down = gp.dpad.down.wasPressedThisFrame || (leftStickDown && !_prevDown);

            _prevLeft = leftStickLeft;
            _prevRight = leftStickRight;
            _prevUp = leftStickUp;
            _prevDown = leftStickDown;

            confirm = gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame;
            back = gp.buttonEast.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame;
        }

        private void PollKeyboard(
            Keyboard kb,
            out bool left,
            out bool right,
            out bool up,
            out bool down,
            out bool confirm,
            out bool back
        )
        {
            left = kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
            right = kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
            up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            confirm = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            back = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;

            _prevLeft = _prevRight = _prevUp = _prevDown = false;
        }
    }

    public readonly struct EdgeSet
    {
        public readonly bool Left;
        public readonly bool Right;
        public readonly bool Up;
        public readonly bool Down;
        public readonly bool Confirm;
        public readonly bool Back;

        public EdgeSet(bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            Left = left;
            Right = right;
            Up = up;
            Down = down;
            Confirm = confirm;
            Back = back;
        }
    }

    public readonly struct SelectionContext
    {
        public readonly int CharacterCount;
        public readonly int ControlsPresetCount;
        public readonly int SkinCount;
        public readonly Func<int, bool> IsRowInteractable;

        /// <summary>Char/skin combo locked by the other player, or (-1, -1) if none.</summary>
        public readonly int OtherCharacter;
        public readonly int OtherSkin;

        /// <summary>Skin count for a given character index — used when cycling characters to find a non-colliding skin.</summary>
        public readonly Func<int, int> SkinCountForChar;

        /// <summary>
        /// When true, the grid exposes an extra "Random" slot at index
        /// <see cref="CharacterCount"/> that cycles into the navigation. The
        /// slot remains as "random" through Options/Confirmed and is resolved
        /// to a concrete character at commit time by the directory.
        /// </summary>
        public readonly bool HasRandomSlot;

        public SelectionContext(
            int characterCount,
            int controlsPresetCount,
            int skinCount,
            Func<int, bool> isRowInteractable,
            int otherCharacter,
            int otherSkin,
            Func<int, int> skinCountForChar,
            bool hasRandomSlot
        )
        {
            CharacterCount = characterCount;
            ControlsPresetCount = controlsPresetCount;
            SkinCount = skinCount;
            IsRowInteractable = isRowInteractable;
            OtherCharacter = otherCharacter;
            OtherSkin = otherSkin;
            SkinCountForChar = skinCountForChar;
            HasRandomSlot = hasRandomSlot;
        }

        public bool IsTaken(int charIdx, int skinIdx) =>
            OtherCharacter >= 0 && charIdx == OtherCharacter && skinIdx == OtherSkin;
    }

    public static class OptionsRows
    {
        public const int ComboMode = 0;
        public const int Skin = 1;
        public const int ManiaDifficulty = 2;
        public const int BeatCancel = 3;
        public const int ControlsPreset = 4;
        public const int Count = 5;

        /// <summary>
        /// Whether the row is rendered at all. All rows are always rendered;
        /// non-interactable rows are grayed out rather than hidden.
        /// </summary>
        public static bool IsVisible(PlayerSelectionState state, bool isLocal, int row)
        {
            return true;
        }

        /// <summary>
        /// Whether the row is selectable / editable. Non-interactable rows
        /// remain visible but grayed out; nav skips them and L/R is a no-op.
        /// ManiaDifficulty and BeatCancel are non-interactable when ComboMode
        /// is <see cref="Game.Sim.ComboMode.Freestyle"/>. ControlsPreset is
        /// non-interactable on the remote player's mirrored panel since the
        /// value is never synced.
        /// </summary>
        public static bool IsInteractable(
            PlayerSelectionState state,
            bool isLocal,
            int row,
            int rosterLength = int.MaxValue
        )
        {
            if (!IsVisible(state, isLocal, row))
                return false;
            if (row == ControlsPreset && !isLocal)
                return false;
            if (state.ComboMode == Game.Sim.ComboMode.Freestyle && (row == ManiaDifficulty || row == BeatCancel))
                return false;
            // Skin row is meaningless while the slot is on the Random tile —
            // the concrete skin isn't picked until commit.
            if (row == Skin && state.CharacterIndex >= rosterLength)
                return false;
            return true;
        }
    }
}
