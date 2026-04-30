using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim.Configs
{
    [MemoryPackable]
    [Serializable]
    public partial struct BeatmapNote
    {
        public Frame Tick;
        public int Channel;
    }

    // One .osz difficulty's authored notes. Paired so each mania-difficulty
    // selection (Normal / Hard) has its own chart.
    [Serializable]
    [MemoryPackable]
    public partial struct BeatmapDifficulty
    {
        public string DifficultyName;
        public BeatmapNote[] Notes;
    }

    // Deterministic audio data: tempo, beatmap charts, song layout. The
    // companion AudioPresentation SO (view side) holds the AudioClip and
    // per-character theme map for the Conductor.
    [CreateAssetMenu(menuName = "Hypermania/Sim/Audio Stats")]
    [MemoryPackable]
    public partial class AudioStats : ScriptableObject
    {
        public Frame FirstMusicalBeat = Frame.FirstFrame;
        public sfloat Bpm = 60;
        public int LoopBeat = 0;
        public int SongLengthBeats = 232;
        public int ComboBeatCount = 8;
        public BeatmapDifficulty Normal;
        public BeatmapDifficulty Hard;

        [MemoryPackIgnore]
        public new string name
        {
            get => base.name;
            set => base.name = value;
        }

        [MemoryPackIgnore]
        public new HideFlags hideFlags
        {
            get => base.hideFlags;
            set => base.hideFlags = value;
        }

        public BeatmapNote[] NotesFor(ManiaDifficulty difficulty)
        {
            BeatmapNote[] notes = difficulty == ManiaDifficulty.Hard ? Hard.Notes : Normal.Notes;
            return notes ?? Array.Empty<BeatmapNote>();
        }

        public int FramesPerBeat => Mathsf.RoundToInt((sfloat)60f / Bpm * SimConstants.TPS);

        public int BeatsToFrame(int beats)
        {
            return Mathsf.RoundToInt((sfloat)beats * (sfloat)60f / Bpm * SimConstants.TPS);
        }

        public Frame FirstBeatFrame(int audioStartFrame)
        {
            return FirstMusicalBeat + audioStartFrame;
        }

        public bool IsOnBeat(Frame frame, int audioStartFrame)
        {
            sfloat framesPerBeatExact = (sfloat)60f / Bpm * (sfloat)SimConstants.TPS;
            sfloat beatsF = (sfloat)(frame - FirstBeatFrame(audioStartFrame)) / framesPerBeatExact;
            sfloat driftFrames = (beatsF - (sfloat)Mathsf.RoundToInt(beatsF)) * framesPerBeatExact;
            return driftFrames >= -(sfloat)(2f) && driftFrames <= (sfloat)2f;
        }

        public BeatmapNote[] SliceFrom(
            Frame minStart,
            ManiaDifficulty difficulty,
            int audioStartFrame,
            int comboBeatCount = -1
        )
        {
            BeatmapNote[] notes = NotesFor(difficulty);
            if (notes.Length == 0)
                return Array.Empty<BeatmapNote>();

            if (comboBeatCount < 0)
                comboBeatCount = ComboBeatCount;
            Frame audioMinStart = minStart - audioStartFrame;
            Frame audioEndBound = audioMinStart + BeatsToFrame(comboBeatCount);
            int songEnd = BeatsToFrame(SongLengthBeats);
            int loopStartFrame = BeatsToFrame(LoopBeat);
            int loopBeats = SongLengthBeats - LoopBeat;

            var result = new List<BeatmapNote>();

            int first = LowerBound(notes, audioMinStart);

            for (int i = first; i < notes.Length; i++)
            {
                if (notes[i].Tick.No >= songEnd || notes[i].Tick > audioEndBound)
                    break;
                result.Add(new BeatmapNote { Tick = notes[i].Tick + audioStartFrame, Channel = notes[i].Channel });
            }

            if (audioEndBound.No <= songEnd || loopBeats <= 0)
                return result.ToArray();

            int loopFirst = LowerBound(notes, (Frame)loopStartFrame);
            int loopEnd = LowerBound(notes, (Frame)songEnd);
            if (loopFirst >= loopEnd)
                return result.ToArray();

            int approxLoopLen = songEnd - loopStartFrame;
            int startIter = audioMinStart.No >= songEnd ? Math.Max(1, (audioMinStart.No - songEnd) / approxLoopLen) : 1;

            for (int n = startIter; ; n++)
            {
                int loopShift = BeatsToFrame(n * loopBeats);

                int firstAbsolute = notes[loopFirst].Tick.No + loopShift;
                if (firstAbsolute > audioEndBound.No)
                    break;

                for (int i = loopFirst; i < loopEnd; i++)
                {
                    int absolute = notes[i].Tick.No + loopShift;
                    if (absolute > audioEndBound.No)
                        break;
                    if (absolute >= audioMinStart.No)
                        result.Add(
                            new BeatmapNote { Tick = (Frame)(absolute + audioStartFrame), Channel = notes[i].Channel }
                        );
                }
            }

            return result.ToArray();
        }

        static int LowerBound(BeatmapNote[] arr, Frame target)
        {
            int lo = 0,
                hi = arr.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (arr[mid].Tick < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }
    }
}
