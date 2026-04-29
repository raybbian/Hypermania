using System;
using Game.Sim;

namespace Hypermania.CPU.Training
{
    // Discrete action space the policy emits over. Two independent categorical
    // heads: one direction, one button. The trainer scores them as factorized
    // log-probs, so the joint distribution is dir x button without enumerating
    // the 9*7 grid as flat logits.
    //
    // Bumping NumDirections / NumButtons is a breaking change for snapshots
    // (.hmpolicy files). Bump Version when you do.
    public static class ActionSpace
    {
        public const byte Version = 1;

        public const int NumDirections = 9;
        public const int NumButtons = 7;
        public const int NumActions = NumDirections * NumButtons;

        // Index 0 is "no input" for both heads, so the network can learn to
        // do nothing. Diagonals included so jumping forward / crouching
        // backwards are single actions.
        public static readonly InputFlags[] Directions =
        {
            InputFlags.None,
            InputFlags.Up,
            InputFlags.Down,
            InputFlags.Left,
            InputFlags.Right,
            InputFlags.Up | InputFlags.Left,
            InputFlags.Up | InputFlags.Right,
            InputFlags.Down | InputFlags.Left,
            InputFlags.Down | InputFlags.Right,
        };

        public static readonly InputFlags[] Buttons =
        {
            InputFlags.None,
            InputFlags.LightAttack,
            InputFlags.MediumAttack,
            InputFlags.HeavyAttack,
            InputFlags.SpecialAttack,
            InputFlags.Dash,
            InputFlags.Grab,
        };

        public static InputFlags Decode(int dirIndex, int btnIndex)
        {
            if ((uint)dirIndex >= (uint)NumDirections)
                throw new ArgumentOutOfRangeException(nameof(dirIndex));
            if ((uint)btnIndex >= (uint)NumButtons)
                throw new ArgumentOutOfRangeException(nameof(btnIndex));
            return Directions[dirIndex] | Buttons[btnIndex];
        }
    }
}
