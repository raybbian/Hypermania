using System;
using System.Collections.Generic;

namespace Hypermania.CPU.Training
{
    // Per-step rollout buffer for the learning fighter. Aligned arrays - one
    // entry per env step. The PPO trainer flattens these into tensors,
    // computes advantages and returns, and runs minibatch updates.
    //
    // Observations are kept in a single contiguous float[] grown like a List<T>:
    // appending is one Span.CopyTo, and OptimizeOn can BlockCopy the whole
    // trajectory in one shot per env instead of per-step.
    public sealed class Trajectory
    {
        readonly int _obsDim;
        float[] _obsBuf;
        int _obsRows;

        public readonly List<int> DirActions = new List<int>();
        public readonly List<int> BtnActions = new List<int>();
        public readonly List<float> LogProbs = new List<float>();
        public readonly List<float> Values = new List<float>();
        public readonly List<float> Rewards = new List<float>();
        public readonly List<bool> Dones = new List<bool>();

        // Bootstrap value estimate at the end of the rollout (V(s_T) for
        // GAE if the episode was truncated, or 0 if it terminated).
        public float TerminalValue;

        public Trajectory(int obsDim)
        {
            if (obsDim <= 0)
                throw new ArgumentOutOfRangeException(nameof(obsDim));
            _obsDim = obsDim;
            _obsBuf = Array.Empty<float>();
        }

        public int ObsDim => _obsDim;
        public int Length => DirActions.Count;
        public int ObsRows => _obsRows;

        // Backing buffer is at least _obsRows * _obsDim long, but may be
        // larger after growth doubling. Slice via ObsFlat for the live region.
        public ReadOnlySpan<float> ObsFlat => _obsBuf.AsSpan(0, _obsRows * _obsDim);

        public float[] ObsBuffer => _obsBuf;

        public void AppendObservation(ReadOnlySpan<float> row)
        {
            if (row.Length != _obsDim)
                throw new ArgumentException(
                    $"observation row must be {_obsDim} floats, got {row.Length}",
                    nameof(row)
                );
            int needed = (_obsRows + 1) * _obsDim;
            if (_obsBuf.Length < needed)
            {
                int newCap = _obsBuf.Length == 0 ? 64 * _obsDim : _obsBuf.Length * 2;
                while (newCap < needed)
                    newCap *= 2;
                Array.Resize(ref _obsBuf, newCap);
            }
            row.CopyTo(_obsBuf.AsSpan(_obsRows * _obsDim, _obsDim));
            _obsRows++;
        }

        public void Clear()
        {
            _obsRows = 0;
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
