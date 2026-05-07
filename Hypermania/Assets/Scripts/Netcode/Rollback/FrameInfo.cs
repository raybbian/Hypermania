using Utils;
using Hypermania.Shared;

namespace Netcode.Rollback
{
    public class GameStateCtx
    {
        public Frame Frame;
        public byte[] Data;
        public ulong Checksum;
    }

    public struct PlayerInput<TInput>
        where TInput : IInput<TInput>
    {
        public Frame Frame;
        public TInput Input;

        public static PlayerInput<TInput> BlankInput(Frame frame) =>
            new PlayerInput<TInput> { Frame = frame, Input = default };

        public bool Equal(in PlayerInput<TInput> other, bool inputOnly) =>
            (inputOnly || Frame == other.Frame) && Input.Equals(other.Input);
    }
}
