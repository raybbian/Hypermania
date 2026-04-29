using System.Collections.Generic;

namespace Hypermania.CPU.Training
{
    // Per-step rollout buffer for the learning fighter. Aligned arrays - one
    // entry per env step. The PPO trainer flattens these into tensors,
    // computes advantages and returns, and runs minibatch updates.
    public sealed class Trajectory
    {
        public readonly List<float[]> Observations = new List<float[]>();
        public readonly List<int> DirActions = new List<int>();
        public readonly List<int> BtnActions = new List<int>();
        public readonly List<float> LogProbs = new List<float>();
        public readonly List<float> Values = new List<float>();
        public readonly List<float> Rewards = new List<float>();
        public readonly List<bool> Dones = new List<bool>();

        // Bootstrap value estimate at the end of the rollout (V(s_T) for
        // GAE if the episode was truncated, or 0 if it terminated).
        public float TerminalValue;

        public int Length => DirActions.Count;

        public void Clear()
        {
            Observations.Clear();
            DirActions.Clear();
            BtnActions.Clear();
            LogProbs.Clear();
            Values.Clear();
            Rewards.Clear();
            Dones.Clear();
            TerminalValue = 0f;
        }
    }
}
