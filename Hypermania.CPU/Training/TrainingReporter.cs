using System;
using TorchSharp;

namespace Hypermania.CPU.Training
{
    // Snapshot of one PPO update's telemetry. The trainer fills this once per
    // call to ITrainingReporter.OnUpdate; reporters are responsible for any
    // history they want to retain.
    public struct TrainingMetrics
    {
        public long Step;
        public long TotalUpdates;

        public int Rollouts;
        public int Wins;
        public float MeanReturn;
        public float MeanFrames;
        public float MeanFightingFrames;

        // Training step of the opponent snapshot sampled from the pool for
        // this update's rollouts. 0 = freshly-initialized snapshot from the
        // start of training.
        public long OpponentStep;

        // Cumulative learner win-rate against the sampled opponent over the
        // entire time that pool entry has been alive (inclusive of this
        // update). Drives PFSP weighting; useful telemetry under uniform too.
        public float OpponentWinRate;

        public float PolicyLoss;
        public float ValueLoss;
        public float Entropy;

        // Average per-episode reward decomposition across the rollouts. Sum
        // of the fields equals MeanReturn.
        public RewardBreakdown AvgBreakdown;

        // Wall-clock per phase of this update.
        public TimeSpan RolloutTime;
        public TimeSpan OptimizeTime;
        public TimeSpan UpdateTime; // total

        // Rollout sub-phase breakdown summed over all sim steps in this update.
        // FeaturizeTime + ForwardTime + StepTime accounts for most of RolloutTime;
        // any remainder is bookkeeping in the rollout outer loop.
        public TimeSpan FeaturizeTime;
        public TimeSpan ForwardTime;
        public TimeSpan StepTime;

        // Rolling estimates of seconds-per-update; the TUI uses these to
        // produce a stable ETA. Trainer fills them with EMA/avg.
        public float UpdateSecondsEma;
    }

    // Sink for per-update training telemetry. Lifecycle: Begin once → OnUpdate
    // per training step → End once. Implementations must tolerate End being
    // called even if Begin failed or training was aborted.
    public interface ITrainingReporter
    {
        void Begin(string runId, string runDir, PpoConfig cfg, torch.Device device, int obsDim);
        void OnUpdate(in TrainingMetrics m);
        void End();
    }

    // Default reporter: one line per update. Matches the original Console
    // output so logs look the same as before the TUI work.
    public sealed class ConsoleReporter : ITrainingReporter
    {
        public void Begin(
            string runId,
            string runDir,
            PpoConfig cfg,
            torch.Device device,
            int obsDim
        )
        {
            Console.WriteLine($"run dir: {runDir}");
            Console.WriteLine(
                $"device: {(device.type == DeviceType.CUDA ? "CUDA" : "CPU")} obs={obsDim}"
            );
        }

        public void OnUpdate(in TrainingMetrics m)
        {
            Console.WriteLine(
                $"[update {m.Step, 6}] mean_return={m.MeanReturn, 7:F3} "
                    + $"wins={m.Wins}/{m.Rollouts} "
                    + $"opp@{m.OpponentStep} wr={m.OpponentWinRate, 4:F2} "
                    + $"policy_loss={m.PolicyLoss, 7:F4} value_loss={m.ValueLoss, 7:F4} "
                    + $"entropy={m.Entropy, 5:F3} "
                    + $"t={m.UpdateTime.TotalSeconds:F2}s "
                    + $"(roll {m.RolloutTime.TotalSeconds:F2}s "
                    + $"[feat {m.FeaturizeTime.TotalSeconds:F2} "
                    + $"fwd {m.ForwardTime.TotalSeconds:F2} "
                    + $"step {m.StepTime.TotalSeconds:F2}] "
                    + $"opt {m.OptimizeTime.TotalSeconds:F2}s)"
            );
        }

        public void End() { }
    }
}
