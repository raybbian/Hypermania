using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Utils.SoftFloat;

namespace Design.Configs.Editor
{
    /// <summary>
    /// Minimal Standard MIDI File parser that extracts note_on events from a
    /// named track and converts them to frame indices at a target framerate.
    /// Mirrors the logic of the former Hypermania.Utils/midi_to_frames.py.
    /// </summary>
    public static class MidiParser
    {
        public static int[] ParseTrackToFrames(byte[] midiData, string trackName, int fps = 60)
        {
            using var stream = new MemoryStream(midiData);
            using var reader = new BinaryReader(stream);

            // --- Header chunk: MThd ---
            ReadChunkId(reader, "MThd");
            int headerLen = ReadInt32BE(reader);
            /*format*/ReadInt16BE(reader);
            int numTracks = ReadInt16BE(reader);
            int ticksPerBeat = ReadInt16BE(reader);
            if (headerLen > 6)
                stream.Seek(headerLen - 6, SeekOrigin.Current);

            // --- Read all tracks ---
            var tracks = new List<List<MidiEvent>>(numTracks);
            for (int i = 0; i < numTracks; i++)
            {
                ReadChunkId(reader, "MTrk");
                int trackLen = ReadInt32BE(reader);
                long trackEnd = stream.Position + trackLen;
                tracks.Add(ReadTrackEvents(reader, trackEnd));
            }

            // Seed tempo with the first set_tempo found in any track (same as mido).
            int initialTempo = 500_000; // 120 BPM default per MIDI spec
            foreach (var track in tracks)
            {
                foreach (var evt in track)
                {
                    if (evt.Type == EventType.SetTempo)
                    {
                        initialTempo = evt.Tempo;
                        goto tempoFound;
                    }
                }
            }
            tempoFound:

            // Find target track by name.
            List<MidiEvent> targetTrack = null;
            var available = new List<string>();
            foreach (var track in tracks)
            {
                string name = null;
                foreach (var evt in track)
                {
                    if (evt.Type == EventType.TrackName)
                    {
                        name = evt.Text;
                        break;
                    }
                }
                if (name != null)
                    available.Add(name);
                if (name == trackName)
                    targetTrack = track;
            }

            if (targetTrack == null)
                throw new InvalidOperationException(
                    $"Track '{trackName}' not found. Available tracks: [{string.Join(", ", available)}]"
                );

            // Convert to frames.
            sfloat elapsedSeconds = (sfloat)0.0;
            int tempo = initialTempo;
            var frames = new List<int>();

            foreach (var evt in targetTrack)
            {
                if (evt.DeltaTicks > 0)
                    elapsedSeconds += evt.DeltaTicks * (tempo / (sfloat)1_000_000.0) / ticksPerBeat;

                if (evt.Type == EventType.SetTempo)
                {
                    tempo = evt.Tempo;
                    continue;
                }

                if (evt.Type == EventType.NoteOn && evt.Velocity > 0)
                    frames.Add((int)Mathsf.Round(elapsedSeconds * fps));
            }

            return frames.ToArray();
        }

        // -------------------------------------------------------------------
        // Internal types
        // -------------------------------------------------------------------

        private enum EventType
        {
            Other,
            TrackName,
            SetTempo,
            NoteOn,
        }

        private struct MidiEvent
        {
            public int DeltaTicks;
            public EventType Type;
            public int Tempo; // only for SetTempo
            public int Velocity; // only for NoteOn
            public string Text; // only for TrackName
        }

        // -------------------------------------------------------------------
        // Chunk / primitive readers
        // -------------------------------------------------------------------

        private static void ReadChunkId(BinaryReader r, string expected)
        {
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            if (id != expected)
                throw new InvalidOperationException($"Expected chunk '{expected}', got '{id}'");
        }

        private static int ReadInt32BE(BinaryReader r)
        {
            byte a = r.ReadByte(),
                b = r.ReadByte(),
                c = r.ReadByte(),
                d = r.ReadByte();
            return (a << 24) | (b << 16) | (c << 8) | d;
        }

        private static int ReadInt16BE(BinaryReader r)
        {
            byte a = r.ReadByte(),
                b = r.ReadByte();
            return (a << 8) | b;
        }

        private static int ReadVariableLength(BinaryReader r)
        {
            int value = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
        }

        // -------------------------------------------------------------------
        // Track event parser
        // -------------------------------------------------------------------

        private static List<MidiEvent> ReadTrackEvents(BinaryReader r, long trackEnd)
        {
            var events = new List<MidiEvent>();
            byte runningStatus = 0;

            while (r.BaseStream.Position < trackEnd)
            {
                int delta = ReadVariableLength(r);
                byte status = r.ReadByte();

                // Meta event: 0xFF type len data
                if (status == 0xFF)
                {
                    byte metaType = r.ReadByte();
                    int len = ReadVariableLength(r);
                    byte[] data = r.ReadBytes(len);

                    var evt = new MidiEvent { DeltaTicks = delta, Type = EventType.Other };

                    if (metaType == 0x03) // Track Name
                    {
                        evt.Type = EventType.TrackName;
                        evt.Text = Encoding.ASCII.GetString(data);
                    }
                    else if (metaType == 0x51 && data.Length >= 3) // Set Tempo
                    {
                        evt.Type = EventType.SetTempo;
                        evt.Tempo = (data[0] << 16) | (data[1] << 8) | data[2];
                    }

                    events.Add(evt);
                    continue;
                }

                // SysEx: 0xF0 or 0xF7
                if (status == 0xF0 || status == 0xF7)
                {
                    int len = ReadVariableLength(r);
                    r.BaseStream.Seek(len, SeekOrigin.Current);
                    events.Add(new MidiEvent { DeltaTicks = delta, Type = EventType.Other });
                    continue;
                }

                // Channel event (with running status support)
                if (status < 0x80)
                {
                    // Data byte — reuse running status
                    r.BaseStream.Seek(-1, SeekOrigin.Current);
                    status = runningStatus;
                }
                else
                {
                    runningStatus = status;
                }

                int hi = status & 0xF0;
                switch (hi)
                {
                    case 0x80: // Note Off: 2 data bytes
                    case 0xA0: // Poly Aftertouch
                    case 0xB0: // Control Change
                    case 0xE0: // Pitch Bend
                        r.ReadBytes(2);
                        events.Add(new MidiEvent { DeltaTicks = delta, Type = EventType.Other });
                        break;

                    case 0x90: // Note On: 2 data bytes
                    {
                        /*note*/r.ReadByte();
                        byte velocity = r.ReadByte();
                        events.Add(
                            new MidiEvent
                            {
                                DeltaTicks = delta,
                                Type = EventType.NoteOn,
                                Velocity = velocity,
                            }
                        );
                        break;
                    }

                    case 0xC0: // Program Change: 1 data byte
                    case 0xD0: // Channel Aftertouch
                        r.ReadByte();
                        events.Add(new MidiEvent { DeltaTicks = delta, Type = EventType.Other });
                        break;

                    default:
                        // Unknown status — skip to be safe
                        events.Add(new MidiEvent { DeltaTicks = delta, Type = EventType.Other });
                        break;
                }
            }

            return events;
        }
    }
}
