using System.IO;
using Game.Sim;

namespace Hypermania.CPU.Policy
{
    public interface IPolicy
    {
        // Pure read of the current sim state. Must be deterministic across
        // runs so replays of self-play are exactly reproducible.
        InputFlags Act(in GameState state, int fighterIndex);

        // Opaque round-trip of policy weights. The trainer writes to a stream
        // owned by the snapshot file; inference reads from one. Schema-compat
        // is the policy's responsibility - bake a version byte if you care.
        void Save(Stream s);
        void Load(Stream s);
    }
}
