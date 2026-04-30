using System;
using System.Buffers.Binary;
using System.IO;
using Game.Sim;
using Hypermania.CPU.Featurization;
using Hypermania.CPU.Training;
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
        public const byte SnapshotVersion = 1;

        // 1 (version) + 8 (schema hash) + 1 (action space version) + 8 (step) + 8 (utc ticks).
        const int HeaderSize = 1 + 8 + 1 + 8 + 8;

        public PolicyNet Net { get; }
        public Device Device { get; }

        // Mutable so the trainer can stamp the latest training step / time
        // into snapshots without rebuilding the policy.
        public long TrainingStep;
        public long WallclockUtcTicks;

        readonly float[] _obsBuf;

        public NeuralPolicy(int obsDim, Device device, int hidden = 256)
        {
            Device = device ?? torch.CPU;
            Net = new PolicyNet(obsDim, hidden);
            Net.to(Device);
            _obsBuf = new float[obsDim];
            WallclockUtcTicks = DateTime.UtcNow.Ticks;
        }

        public InputFlags Act(in GameState state, SimOptions options, int fighterIndex)
        {
            Featurizer.Encode(state, options, fighterIndex, _obsBuf);
            using Tensor obs = tensor(_obsBuf, new long[] { _obsBuf.Length }, ScalarType.Float32)
                .to(Device);
            var (dir, btn, _) = Net.ActGreedy(obs);
            return ActionSpace.Decode(dir, btn);
        }

        public InputFlags ActSample(in GameState state, SimOptions options, int fighterIndex)
        {
            Featurizer.Encode(state, options, fighterIndex, _obsBuf);
            using Tensor obs = tensor(_obsBuf, new long[] { _obsBuf.Length }, ScalarType.Float32)
                .to(Device);
            var (dir, btn, _, _) = Net.Sample(obs);
            return ActionSpace.Decode(dir, btn);
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
            s.Write(header);

            // TorchSharp's Module.save writes the parameter tensors as a
            // self-describing blob. Length is implicit in the file - we read
            // until EOF after the header. BinaryWriter wrapper is the stable
            // overload across TorchSharp versions.
            using BinaryWriter bw = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
            Net.save(bw);
        }

        public void Load(Stream s)
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
            TrainingStep = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(o));
            o += 8;
            WallclockUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(o));
            o += 8;

            using BinaryReader br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            Net.load(br);
            Net.to(Device);
        }
    }
}
