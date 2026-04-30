using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Rendering;
using TorchSharp;

namespace Hypermania.CPU.Training
{
    // Live-updating terminal UI for training. Compact sparklines per metric
    // (block characters), two columns (algorithm metrics, reward components),
    // metrics footer (wins, frames, timing, ETA). Refreshes at ~10 Hz off a
    // shared snapshot the trainer thread publishes via OnUpdate.
    //
    // Falls back to ConsoleReporter behavior on non-interactive terminals
    // (e.g. piped output) where AnsiConsole.Live wouldn't render usefully.
    public sealed class TuiReporter : ITrainingReporter
    {
        const int HistoryCap = 240;
        const int SparkWidth = 40;
        const string Blocks = "▁▂▃▄▅▆▇█";

        readonly object _lock = new object();

        // Aligned series. Length grows by 1 per OnUpdate, capped at HistoryCap.
        readonly List<float> _meanReturn = new List<float>();
        readonly List<float> _policyLoss = new List<float>();
        readonly List<float> _valueLoss = new List<float>();
        readonly List<float> _entropy = new List<float>();
        readonly List<float> _damage = new List<float>();
        readonly List<float> _block = new List<float>();
        readonly List<float> _approach = new List<float>();
        readonly List<float> _step = new List<float>();
        readonly List<float> _whiff = new List<float>();
        readonly List<float> _terminal = new List<float>();

        TrainingMetrics _latest;
        bool _hasUpdate;
        string _runId = "";
        string _runDir = "";
        PpoConfig _cfg;
        int _obsDim;
        torch.Device _device;
        DateTime _startUtc;

        volatile bool _done;
        Thread _renderThread;
        ITrainingReporter _fallback; // populated only on non-interactive terminals

        public void Begin(
            string runId,
            string runDir,
            PpoConfig cfg,
            torch.Device device,
            int obsDim
        )
        {
            _runId = runId;
            _runDir = runDir;
            _cfg = cfg;
            _device = device;
            _obsDim = obsDim;
            _startUtc = DateTime.UtcNow;

            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                // Non-interactive terminal (piped output, CI). Live rendering
                // would just print the same frame repeatedly; fall back to the
                // line-print reporter so logs stay useful.
                _fallback = new ConsoleReporter();
                _fallback.Begin(runId, runDir, cfg, device, obsDim);
                return;
            }

            _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "tui-renderer" };
            _renderThread.Start();
        }

        public void OnUpdate(in TrainingMetrics m)
        {
            if (_fallback != null)
            {
                _fallback.OnUpdate(m);
                return;
            }
            lock (_lock)
            {
                _latest = m;
                _hasUpdate = true;
                Push(_meanReturn, m.MeanReturn);
                Push(_policyLoss, m.PolicyLoss);
                Push(_valueLoss, m.ValueLoss);
                Push(_entropy, m.Entropy);
                Push(_damage, m.AvgBreakdown.Damage);
                Push(_block, m.AvgBreakdown.Block);
                Push(_approach, m.AvgBreakdown.Approach);
                Push(_step, m.AvgBreakdown.Step);
                Push(_whiff, m.AvgBreakdown.Whiff);
                Push(_terminal, m.AvgBreakdown.Terminal);
            }
        }

        public void End()
        {
            if (_fallback != null)
            {
                _fallback.End();
                return;
            }
            _done = true;
            _renderThread?.Join();
        }

        void RenderLoop()
        {
            try
            {
                AnsiConsole
                    .Live(Build())
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .Start(ctx =>
                    {
                        while (!_done)
                        {
                            ctx.UpdateTarget(Build());
                            Thread.Sleep(100);
                        }
                        // One last paint to capture the final update.
                        ctx.UpdateTarget(Build());
                    });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TUI render thread error: {ex.Message}");
            }
        }

        IRenderable Build()
        {
            TrainingMetrics m;
            string[] algoSparks = new string[4];
            string[] algoVals = new string[4];
            string[] rewSparks = new string[6];
            string[] rewVals = new string[6];
            bool hasUpdate;
            lock (_lock)
            {
                m = _latest;
                hasUpdate = _hasUpdate;
                algoSparks[0] = Sparkline(_meanReturn, SparkWidth);
                algoVals[0] = FmtCur(_meanReturn, "F3");
                algoSparks[1] = Sparkline(_policyLoss, SparkWidth);
                algoVals[1] = FmtCur(_policyLoss, "F4");
                algoSparks[2] = Sparkline(_valueLoss, SparkWidth);
                algoVals[2] = FmtCur(_valueLoss, "F4");
                algoSparks[3] = Sparkline(_entropy, SparkWidth);
                algoVals[3] = FmtCur(_entropy, "F3");
                rewSparks[0] = Sparkline(_damage, SparkWidth);
                rewVals[0] = FmtCur(_damage, "F3");
                rewSparks[1] = Sparkline(_block, SparkWidth);
                rewVals[1] = FmtCur(_block, "F3");
                rewSparks[2] = Sparkline(_approach, SparkWidth);
                rewVals[2] = FmtCur(_approach, "F4");
                rewSparks[3] = Sparkline(_step, SparkWidth);
                rewVals[3] = FmtCur(_step, "F3");
                rewSparks[4] = Sparkline(_whiff, SparkWidth);
                rewVals[4] = FmtCur(_whiff, "F4");
                rewSparks[5] = Sparkline(_terminal, SparkWidth);
                rewVals[5] = FmtCur(_terminal, "F3");
            }

            // Title bar.
            string deviceLabel = _device?.type == DeviceType.CUDA ? "CUDA" : "CPU";
            string stepText = hasUpdate ? $"{m.Step}/{m.TotalUpdates}" : $"0/{_cfg.TotalUpdates}";
            TimeSpan elapsed = DateTime.UtcNow - _startUtc;
            TimeSpan eta = EstimateEta(m, hasUpdate);

            Markup title = new Markup(
                $"[bold]Hypermania PPO[/]  "
                    + $"[grey]run[/] {Markup.Escape(_runId)}  "
                    + $"[grey]device[/] {deviceLabel}  "
                    + $"[grey]obs[/] {_obsDim}  "
                    + $"[grey]step[/] {stepText}  "
                    + $"[grey]elapsed[/] {FormatDuration(elapsed)}  "
                    + $"[grey]eta[/] {FormatDuration(eta)}"
            );

            // Two-column metric grid.
            Grid algoGrid = BuildMetricGrid(
                new[] { "mean_return", "policy_loss", "value_loss", "entropy" },
                algoSparks,
                algoVals
            );
            Grid rewGrid = BuildMetricGrid(
                new[] { "damage", "block", "approach", "step", "whiff", "terminal" },
                rewSparks,
                rewVals
            );

            Panel algoPanel = new Panel(algoGrid)
                .Header("[bold green]algorithm[/]")
                .Border(BoxBorder.Rounded);
            Panel rewPanel = new Panel(rewGrid)
                .Header("[bold yellow]reward components (per ep)[/]")
                .Border(BoxBorder.Rounded);

            Grid topRow = new Grid().AddColumn().AddColumn().AddRow(algoPanel, rewPanel);

            // Metrics footer.
            Panel metricsPanel = BuildMetricsPanel(m, hasUpdate, eta);

            return new Rows(title, topRow, metricsPanel);
        }

        static Grid BuildMetricGrid(string[] names, string[] sparks, string[] vals)
        {
            Grid g = new Grid()
                .AddColumn(new GridColumn().Width(12).NoWrap())
                .AddColumn(new GridColumn().Width(SparkWidth + 2).NoWrap())
                .AddColumn(new GridColumn().Width(10).RightAligned().NoWrap());
            for (int i = 0; i < names.Length; i++)
            {
                g.AddRow(
                    new Markup($"[grey]{Markup.Escape(names[i])}[/]"),
                    new Markup($"[cyan]{Markup.Escape(sparks[i])}[/]"),
                    new Markup($"[white]{Markup.Escape(vals[i])}[/]")
                );
            }
            return g;
        }

        Panel BuildMetricsPanel(TrainingMetrics m, bool hasUpdate, TimeSpan eta)
        {
            Grid g = new Grid()
                .AddColumn(new GridColumn().Width(14).NoWrap())
                .AddColumn(new GridColumn().NoWrap());
            if (!hasUpdate)
            {
                g.AddRow(new Markup("[grey]waiting for first update...[/]"), new Markup(""));
            }
            else
            {
                float winPct = m.Rollouts > 0 ? 100f * m.Wins / m.Rollouts : 0f;
                g.AddRow(
                    new Markup("[grey]wins[/]"),
                    new Markup(Markup.Escape($"{m.Wins}/{m.Rollouts}  ({winPct:F0}%)"))
                );
                g.AddRow(
                    new Markup("[grey]frames/ep[/]"),
                    new Markup(
                        Markup.Escape(
                            $"{m.MeanFrames:F0} total, {m.MeanFightingFrames:F0} fighting"
                        )
                    )
                );
                g.AddRow(
                    new Markup("[grey]t/update[/]"),
                    new Markup(
                        Markup.Escape(
                            $"{m.UpdateTime.TotalSeconds:F2}s  "
                                + $"(rollout {m.RolloutTime.TotalSeconds:F2}, "
                                + $"opt {m.OptimizeTime.TotalSeconds:F2})"
                        )
                    )
                );
                float upm = m.UpdateSecondsEma > 0f ? 60f / m.UpdateSecondsEma : 0f;
                g.AddRow(
                    new Markup("[grey]updates/min[/]"),
                    new Markup(Markup.Escape($"{upm:F1}  (ema {m.UpdateSecondsEma:F2}s)"))
                );
                g.AddRow(
                    new Markup("[grey]eta[/]"),
                    new Markup(Markup.Escape(FormatDuration(eta)))
                );
            }
            return new Panel(g).Header("[bold]metrics[/]").Border(BoxBorder.Rounded);
        }

        TimeSpan EstimateEta(in TrainingMetrics m, bool hasUpdate)
        {
            if (!hasUpdate || m.UpdateSecondsEma <= 0f)
                return TimeSpan.Zero;
            long remaining = m.TotalUpdates - m.Step;
            if (remaining <= 0)
                return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remaining * m.UpdateSecondsEma);
        }

        static string FormatDuration(TimeSpan t)
        {
            if (t.TotalSeconds < 1)
                return "0s";
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}h{t.Minutes:D2}m";
            if (t.TotalMinutes >= 1)
                return $"{(int)t.TotalMinutes}m{t.Seconds:D2}s";
            return $"{(int)t.TotalSeconds}s";
        }

        static void Push(List<float> series, float v)
        {
            series.Add(v);
            if (series.Count > HistoryCap)
                series.RemoveRange(0, series.Count - HistoryCap);
        }

        static string FmtCur(IList<float> series, string fmt)
        {
            if (series.Count == 0)
                return "-";
            return series[series.Count - 1].ToString(fmt);
        }

        // Block-character sparkline. Rescales to the min/max of the visible
        // window so trends are readable regardless of absolute magnitude.
        static string Sparkline(IList<float> values, int width)
        {
            int n = values.Count;
            if (n == 0)
                return new string(' ', width);
            int start = Math.Max(0, n - width);
            int len = n - start;
            float min = float.MaxValue,
                max = float.MinValue;
            for (int i = start; i < n; i++)
            {
                float v = values[i];
                if (v < min)
                    min = v;
                if (v > max)
                    max = v;
            }
            float span = max - min;
            if (span < 1e-9f)
                span = 1e-9f;
            StringBuilder sb = new StringBuilder(width);
            for (int i = 0; i < width - len; i++)
                sb.Append(' ');
            for (int i = start; i < n; i++)
            {
                float t = (values[i] - min) / span;
                int idx = (int)MathF.Round(t * (Blocks.Length - 1));
                if (idx < 0)
                    idx = 0;
                if (idx >= Blocks.Length)
                    idx = Blocks.Length - 1;
                sb.Append(Blocks[idx]);
            }
            return sb.ToString();
        }
    }
}
