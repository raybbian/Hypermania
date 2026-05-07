using System;
using System.Buffers.Binary;
using System.IO;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Training;
using Hypermania.Game;
using TorchSharp;
using static TorchSharp.torch;

namespace Hypermania.CPU.Policy
{
    // IPolicy backed by a PolicyNet. Greedy at inference time. Snapshot layout
    // is a fixed-width header followed by the raw module state_dict bytes.
    // Header carries version, schema hash, action-space version, training
    // step count, and a UTC timestamp - enough metadata to reject mismatched
    // snapshots and to attribute replays to a specific run.
    public sealed class NeuralPolicy : IPolicy
    {
        public const byte SnapshotVersion = 2;

        // 1 (version) + 8 (schema hash) + 1 (action space version) + 8 (step)
        // + 8 (utc ticks) + 4 (hidden). Hidden lets eval / opponent loads
        // reconstruct the network with the architecture used at train time
        // instead of guessing the default.
        const int HeaderSize = 1 + 8 + 1 + 8 + 8 + 4;

        public PolicyNet Net { get; }
        public Device Device { get; }

        // Mutable so the trainer can stamp the latest training step / time
        // into snapshots without rebuilding the policy.
        public long TrainingStep;
        public long WallclockUtcTicks;

        readonly float[] _obsBuf;
        // Per-fighter-slot action history. Indexed [fighterIndex][k] with k
        // in [0, ActionHistoryFrames). _historyHead[f] is the next-write index
        // for fighter slot f. Callers running multi-episode inference should
        // invoke ResetHistory() at the start of each episode.
        readonly int[][] _dirHistory;
        readonly int[][] _btnHistory;
        readonly int[] _historyHead;
        readonly int[] _orderedDir;
        readonly int[] _orderedBtn;
        // Per-fighter-slot GRU hidden state. Each tensor is [1, 1, hidden].
        // ResetHistory zeros them; Act/ActSample replace the slot with the new
        // hOut after each forward and dispose the previous tensor.
        readonly Tensor[] _hidden;

        public NeuralPolicy(int obsDim, Device device, int hidden = 256)
        {
            Device = device ?? torch.CPU;
            Net = new PolicyNet(obsDim, hidden);
            Net.to(Device);
            _obsBuf = new float[obsDim];
            int hl = Featurizer.ActionHistoryFrames;
            _dirHistory = new int[][] { new int[hl], new int[hl] };
            _btnHistory = new int[][] { new int[hl], new int[hl] };
            _historyHead = new int[2];
            _orderedDir = new int[hl];
            _orderedBtn = new int[hl];
            _hidden = new Tensor[2];
            ResetHistory();
            WallclockUtcTicks = DateTime.UtcNow.Ticks;
        }

        public void ResetHistory()
        {
            Array.Fill(_dirHistory[0], -1);
            Array.Fill(_dirHistory[1], -1);
            Array.Fill(_btnHistory[0], -1);
            Array.Fill(_btnHistory[1], -1);
            _historyHead[0] = 0;
            _historyHead[1] = 0;
            for (int f = 0; f < 2; f++)
            {
                _hidden[f]?.Dispose();
                _hidden[f] = Net.InitHidden(1, Device);
            }
        }

        public InputFlags Act(in GameState state, SimOptions options, int fighterIndex)
        {
            BuildObs(state, options, fighterIndex);
            using Tensor obs = tensor(_obsBuf, new long[] { _obsBuf.Length }, ScalarType.Float32)
                .to(Device);
            var (dir, btn, _, hOut) = Net.ActGreedy(obs, _hidden[fighterIndex]);
            ReplaceHidden(fighterIndex, hOut);
            PushHistory(fighterIndex, dir, btn);
            return ActionSpace.Decode(dir, btn);
        }

        public InputFlags ActSample(in GameState state, SimOptions options, int fighterIndex)
        {
            BuildObs(state, options, fighterIndex);
            using Tensor obs = tensor(_obsBuf, new long[] { _obsBuf.Length }, ScalarType.Float32)
                .to(Device);
            var (dir, btn, _, _, hOut) = Net.Sample(obs, _hidden[fighterIndex]);
            ReplaceHidden(fighterIndex, hOut);
            PushHistory(fighterIndex, dir, btn);
            return ActionSpace.Decode(dir, btn);
        }

        void BuildObs(in GameState state, SimOptions options, int fighterIndex)
        {
            int hl = Featurizer.ActionHistoryFrames;
            // Previous-frame action for both fighter slots, looked up at
            // (head - 1) mod hl. Each slot's ring is initialized to -1 so the
            // first frame in an episode reports "no prev frame" and the
            // featurizer's prev-block flags drop to zero.
            int prevSlot0 = (_historyHead[0] - 1 + hl) % hl;
            int prevSlot1 = (_historyHead[1] - 1 + hl) % hl;
            int prevDir0 = _dirHistory[0][prevSlot0];
            int prevDir1 = _dirHistory[1][prevSlot1];

            Featurizer.Encode(
                state,
                options,
                fighterIndex,
                prevDir0,
                prevDir1,
                _obsBuf.AsSpan(0, Featurizer.StateLength)
            );

            int head = _historyHead[fighterIndex];
            int[] dirRing = _dirHistory[fighterIndex];
            int[] btnRing = _btnHistory[fighterIndex];
            for (int k = 0; k < hl; k++)
            {
                int src = (head + k) % hl;
                _orderedDir[k] = dirRing[src];
                _orderedBtn[k] = btnRing[src];
            }
            Featurizer.WriteHistory(
                _orderedDir,
                _orderedBtn,
                _obsBuf.AsSpan(Featurizer.StateLength, Featurizer.HistoryLength)
            );
        }

        void PushHistory(int fighterIndex, int dir, int btn)
        {
            int head = _historyHead[fighterIndex];
            _dirHistory[fighterIndex][head] = dir;
            _btnHistory[fighterIndex][head] = btn;
            _historyHead[fighterIndex] = (head + 1) % Featurizer.ActionHistoryFrames;
        }

        void ReplaceHidden(int fighterIndex, Tensor hOut)
        {
            _hidden[fighterIndex]?.Dispose();
            _hidden[fighterIndex] = hOut;
        }

        public void Save(Stream s)
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            int o = 0;
            header[o++] = SnapshotVersion;
            BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(o), Featurizer.SchemaHash);
            o += 8;
            header[o++] = ActionSpace.Version;
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(o), TrainingStep);
            o += 8;
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(o), WallclockUtcTicks);
            o += 8;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(o), Net.Hidden);
            o += 4;
            s.Write(header);

            // TorchSharp's Module.save writes the parameter tensors as a
            // self-describing blob. Length is implicit in the file - we read
            // until EOF after the header. BinaryWriter wrapper is the stable
            // overload across TorchSharp versions.
            using BinaryWriter bw = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
            Net.save(bw);
        }

        // In-place load. The caller's PolicyNet must already match the
        // snapshot's architecture - if Hidden differs, throws before touching
        // weights. Use LoadFrom when you don't know the snapshot's hidden
        // size up front.
        public void Load(Stream s)
        {
            SnapshotHeader h = ReadHeader(s);
            if (h.Hidden != Net.Hidden)
                throw new InvalidDataException(
                    $"hidden size mismatch: snapshot {h.Hidden} vs current {Net.Hidden}"
                );
            TrainingStep = h.TrainingStep;
            WallclockUtcTicks = h.WallclockUtcTicks;

            using BinaryReader br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            Net.load(br);
            Net.to(Device);
        }

        // Construct a fresh NeuralPolicy whose architecture matches the
        // snapshot's header. Eval / opponent loads use this so they don't
        // have to know the train-time --hidden choice.
        public static NeuralPolicy LoadFrom(Stream s, Device device)
        {
            SnapshotHeader h = ReadHeader(s);
            NeuralPolicy p = new NeuralPolicy(Featurizer.Length, device, h.Hidden);
            p.TrainingStep = h.TrainingStep;
            p.WallclockUtcTicks = h.WallclockUtcTicks;

            using BinaryReader br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            p.Net.load(br);
            p.Net.to(device);
            return p;
        }

        readonly struct SnapshotHeader
        {
            public readonly long TrainingStep;
            public readonly long WallclockUtcTicks;
            public readonly int Hidden;

            public SnapshotHeader(long step, long ticks, int hidden)
            {
                TrainingStep = step;
                WallclockUtcTicks = ticks;
                Hidden = hidden;
            }
        }

        static SnapshotHeader ReadHeader(Stream s)
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            int read = 0;
            while (read < HeaderSize)
            {
                int n = s.Read(header.Slice(read));
                if (n <= 0)
                    throw new EndOfStreamException("snapshot header truncated");
                read += n;
            }
            int o = 0;
            byte ver = header[o++];
            if (ver != SnapshotVersion)
                throw new InvalidDataException($"unsupported snapshot version: {ver}");
            ulong schemaHash = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(o));
            o += 8;
            if (schemaHash != Featurizer.SchemaHash)
                throw new InvalidDataException(
                    $"observation schema mismatch: snapshot 0x{schemaHash:X16} vs current 0x{Featurizer.SchemaHash:X16}"
                );
            byte asv = header[o++];
            if (asv != ActionSpace.Version)
                throw new InvalidDataException(
                    $"action space version mismatch: snapshot {asv} vs current {ActionSpace.Version}"
                );
            long step = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(o));
            o += 8;
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(o));
            o += 8;
            int hidden = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(o));
            o += 4;
            if (hidden <= 0)
                throw new InvalidDataException($"invalid hidden size in snapshot: {hidden}");
            return new SnapshotHeader(step, ticks, hidden);
        }
    }
}
