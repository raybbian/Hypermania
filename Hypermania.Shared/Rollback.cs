using System;

namespace Hypermania.Shared
{
    public enum InputStatus
    {
        Confirmed,
        Predicted,
        Disconnected,
    }

    public interface IInput<TSelf> : IEquatable<TSelf>, ISerializable { }

    public interface IState<TSelf> { }
}
