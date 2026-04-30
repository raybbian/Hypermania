using System.IO;
using Game.Sim;

namespace Hypermania.CPU.Policy
{
    public interface IPolicy
    {
        // Greedy (argmax) action. Deterministic given the same state.
        // Use for evaluation / reproducible benchmarking.
        InputFlags Act(in GameState state, SimOptions options, int fighterIndex);

        // Stochastic action sampled from the policy distribution. Use for
        // self-play training opponents, where a deterministic argmax tends
        // to collapse to a single behavior mode and starve the learner of
        // varied trajectories.
        InputFlags ActSample(in GameState state, SimOptions options, int fighterIndex);

        // Opaque round-trip of policy weights. The trainer writes to a stream
        // owned by the snapshot file; inference reads from one. Schema-compat
        // is the policy's responsibility - bake a version byte if you care.
        void Save(Stream s);
        void Load(Stream s);
    }
}
