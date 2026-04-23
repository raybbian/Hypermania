using System;
using System.Collections.Generic;
using Game;
using Game.Sim;
using UnityEngine;
using UnityEngine.Serialization;
using Utils;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Configs
{
    /// <summary>
    /// One .osz difficulty's authored notes. Paired in <see cref="AudioConfig"/>
    /// so each mania-difficulty selection (Normal / Hard) has its own chart.
    /// </summary>
    [Serializable]
    public struct BeatmapDifficulty
    {
        public string DifficultyName;
        public BeatmapNote[] Notes;
    }

    [CreateAssetMenu(menuName = "Hypermania/Audio Config")]
    public class AudioConfig : ScriptableObject
    {
        [Header("Beatmap Source")]
        public UnityEngine.Object OszFile;

        public Frame FirstMusicalBeat = Frame.FirstFrame;

        [FormerlySerializedAs("BPM")]
        public sfloat Bpm = 60;
        public AudioClip AudioClip;
        public EnumArray<Character, AudioClip> CharacterThemes;
        public int LoopBeat = 0;

        /// <summary>Total song length in quarter-note beats. Used with
        /// <see cref="LoopBeat"/> to determine where the note chart wraps.</summary>
        public int SongLengthBeats = 232;

        /// <summary>Number of quarter-note beats a single combo spans.</summary>
        public int ComboBeatCount = 8;

        /// <summary>
        /// Authored notes for <see cref="ManiaDifficulty.Normal"/>.
        /// Populated from the .osz via "Generate Notes from Beatmap". Sorted
        /// by <see cref="BeatmapNote.Tick"/> ascending; multiple notes may
        /// share a frame on different channels.
        /// </summary>
        public BeatmapDifficulty Normal = new BeatmapDifficulty { Notes = Array.Empty<BeatmapNote>() };

        /// <summary>
        /// Authored notes for <see cref="ManiaDifficulty.Hard"/>. Same shape
        /// as <see cref="Normal"/>; the active set is chosen at combo start
        /// from <see cref="PlayerOptions.ManiaDifficulty"/>.
        /// </summary>
        public BeatmapDifficulty Hard = new BeatmapDifficulty { Notes = Array.Empty<BeatmapNote>() };

        public BeatmapNote[] NotesFor(ManiaDifficulty difficulty)
        {
            BeatmapNote[] notes = difficulty == ManiaDifficulty.Hard ? Hard.Notes : Normal.Notes;
            return notes ?? Array.Empty<BeatmapNote>();
        }

        //Convert BPM to seconds per frame, then seconds to frames
        public int FramesPerBeat => Mathsf.RoundToInt((sfloat)60f / Bpm * GameManager.TPS);

        public enum BeatSubdivision
        {
            WholeNote = 1,
            HalfNote = 2,
            Triplet = 3,

            // Quarter note is the beat
            QuarterNote = 4,
            EighthNote = 8,
            SixteenthNote = 16,
            Quartertriplet = 12,
        }

        /// <summary>
        /// Convert a beat number to a sim-frame using continuous math,
        /// rounding once at the end. This matches the Python MIDI script's
        /// approach and avoids the cumulative rounding error of
        /// <c>beats * FramesPerBeat</c> (where FramesPerBeat is pre-rounded).
        /// </summary>
        public int BeatsToFrame(int beats)
        {
            return Mathsf.RoundToInt((sfloat)beats * (sfloat)60f / Bpm * GameManager.TPS);
        }

        /// <summary>
        /// True when <paramref name="frame"/> sits on a quarter-note grid
        /// position relative to <see cref="FirstMusicalBeat"/>, within ±2
        /// frames. Computes drift continuously: one Round to pick the nearest
        /// integer beat, then measures how far <paramref name="frame"/> is
        /// from that beat directly in frames — no roundtrip through
        /// <see cref="BeatsToFrame"/>. Tighter than the old
        /// <c>Round(beats) → BeatsToFrame</c> path, which stacked an extra
        /// ±0.5-frame error on top of the already-rounded <c>delta</c>.
        /// </summary>
        public bool IsOnBeat(Frame frame)
        {
            sfloat framesPerBeatExact = (sfloat)60f / Bpm * (sfloat)GameManager.TPS;
            sfloat beatsF = (sfloat)(frame - FirstMusicalBeat) / framesPerBeatExact;
            sfloat driftFrames = (beatsF - (sfloat)Mathsf.RoundToInt(beatsF)) * framesPerBeatExact;
            return driftFrames >= -(sfloat)(2f) && driftFrames <= (sfloat)2f;
        }

        /// <summary>
        /// Return a slice of the note chart spanning <see cref="ComboBeatCount"/>
        /// beats starting at or after <paramref name="minStart"/>. When the song
        /// loops, notes from the loop section are re-emitted at correct absolute
        /// frame positions. Loop iteration offsets are computed via continuous math
        /// (<see cref="BeatsToFrame"/>) to avoid cumulative rounding drift.
        /// </summary>
        public BeatmapNote[] SliceFrom(Frame minStart, ManiaDifficulty difficulty, int comboBeatCount = -1)
        {
            BeatmapNote[] notes = NotesFor(difficulty);
            if (notes.Length == 0)
                return Array.Empty<BeatmapNote>();

            if (comboBeatCount < 0)
                comboBeatCount = ComboBeatCount;
            Frame endBound = minStart + BeatsToFrame(comboBeatCount);
            int songEnd = BeatsToFrame(SongLengthBeats);
            int loopStartFrame = BeatsToFrame(LoopBeat);
            int loopBeats = SongLengthBeats - LoopBeat;

            var result = new List<BeatmapNote>();

            int first = LowerBound(notes, minStart);

            // First playthrough: notes in [minStart, min(endBound, songEnd))
            for (int i = first; i < notes.Length; i++)
            {
                if (notes[i].Tick.No >= songEnd || notes[i].Tick > endBound)
                    break;
                result.Add(notes[i]);
            }

            if (endBound.No <= songEnd || loopBeats <= 0)
                return result.ToArray();

            // Loop-section note indices via binary search
            int loopFirst = LowerBound(notes, (Frame)loopStartFrame);
            int loopEnd = LowerBound(notes, (Frame)songEnd);
            if (loopFirst >= loopEnd)
                return result.ToArray();

            int approxLoopLen = songEnd - loopStartFrame;
            int startIter = minStart.No >= songEnd ? Math.Max(1, (minStart.No - songEnd) / approxLoopLen) : 1;

            for (int n = startIter; ; n++)
            {
                // Shift this iteration by exactly n·loopBeats beats — one Round,
                // applied to the already-rounded Tick. Previously this was
                // `Tick - Round(LoopBeat·Y) + Round((LoopBeat+n·L)·Y)` (three
                // rounded operands), which drifted by up to 1.5 frames. The
                // shift-only form is mathematically equivalent and drifts by
                // at most 1 frame.
                int loopShift = BeatsToFrame(n * loopBeats);

                int firstAbsolute = notes[loopFirst].Tick.No + loopShift;
                if (firstAbsolute > endBound.No)
                    break;

                for (int i = loopFirst; i < loopEnd; i++)
                {
                    int absolute = notes[i].Tick.No + loopShift;
                    if (absolute > endBound.No)
                        break;
                    if (absolute >= minStart.No)
                        result.Add(new BeatmapNote { Tick = (Frame)absolute, Channel = notes[i].Channel });
                }
            }

            return result.ToArray();
        }

        private static int LowerBound(BeatmapNote[] arr, Frame target)
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
