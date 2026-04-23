using System;
using System.Collections.Generic;
using Game;
using UnityEngine;
using UnityEngine.Serialization;
using Utils;
using Utils.EnumArray;
using Utils.SoftFloat;

namespace Design.Configs
{
    [CreateAssetMenu(menuName = "Hypermania/Audio Config")]
    public class AudioConfig : ScriptableObject
    {
        [Header("MIDI Source")]
        public UnityEngine.Object MidiFile;
        public string MidiTrackName;

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
        /// Authored note positions as absolute sim-frame indices for the
        /// first playthrough of the song. Generated from MIDI via the
        /// editor "Generate Notes from MIDI" button.
        /// </summary>
        public Frame[] Notes = Array.Empty<Frame>();

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
        /// position relative to <see cref="FirstMusicalBeat"/>. Uses a
        /// ±1-frame tolerance to absorb <see cref="BeatsToFrame"/>'s
        /// rounding drift (the round-trip <c>Round(beats) → BeatsToFrame</c>
        /// can differ from the authored frame by up to one frame at certain
        /// BPMs like 140).
        /// </summary>
        public bool IsOnBeat(Frame frame)
        {
            int delta = frame - FirstMusicalBeat;
            sfloat beatsF = (sfloat)delta * Bpm / (sfloat)60f / (sfloat)GameManager.TPS;
            int nearest = Mathsf.RoundToInt(beatsF);
            int nearestFrame = FirstMusicalBeat.No + BeatsToFrame(nearest);
            int diff = frame.No - nearestFrame;
            return diff <= 1 && diff >= -1;
        }

        /// <summary>
        /// Return a slice of the note chart spanning <see cref="ComboBeatCount"/>
        /// beats starting at or after <paramref name="minStart"/>. When the song
        /// loops, notes from the loop section are re-emitted at correct absolute
        /// frame positions. Loop iteration offsets are computed via continuous math
        /// (<see cref="BeatsToFrame"/>) to avoid cumulative rounding drift.
        /// </summary>
        public Frame[] SliceFrom(Frame minStart, int comboBeatCount = -1)
        {
            if (Notes == null || Notes.Length == 0)
                return Array.Empty<Frame>();

            if (comboBeatCount < 0)
                comboBeatCount = ComboBeatCount;
            Frame endBound = minStart + BeatsToFrame(comboBeatCount);
            int songEnd = BeatsToFrame(SongLengthBeats);
            int loopStartFrame = BeatsToFrame(LoopBeat);
            int loopBeats = SongLengthBeats - LoopBeat;

            var result = new List<Frame>();

            int first = LowerBound(Notes, minStart);

            // First playthrough: notes in [minStart, min(endBound, songEnd))
            for (int i = first; i < Notes.Length; i++)
            {
                if (Notes[i].No >= songEnd || Notes[i] > endBound)
                    break;
                result.Add(Notes[i]);
            }

            if (endBound.No <= songEnd || loopBeats <= 0)
                return result.ToArray();

            // Loop-section note indices via binary search
            int loopFirst = LowerBound(Notes, (Frame)loopStartFrame);
            int loopEnd = LowerBound(Notes, (Frame)songEnd);
            if (loopFirst >= loopEnd)
                return result.ToArray();

            int approxLoopLen = songEnd - loopStartFrame;
            int startIter = minStart.No >= songEnd ? Math.Max(1, (minStart.No - songEnd) / approxLoopLen) : 1;

            for (int n = startIter; ; n++)
            {
                // Continuous math: rounds once per iteration, no cumulative drift.
                int iterStart = BeatsToFrame(LoopBeat + n * loopBeats);

                int firstAbsolute = Notes[loopFirst].No - loopStartFrame + iterStart;
                if (firstAbsolute > endBound.No)
                    break;

                for (int i = loopFirst; i < loopEnd; i++)
                {
                    int absolute = Notes[i].No - loopStartFrame + iterStart;
                    if (absolute > endBound.No)
                        break;
                    if (absolute >= minStart.No)
                        result.Add((Frame)absolute);
                }
            }

            return result.ToArray();
        }

        private static int LowerBound(Frame[] arr, Frame target)
        {
            int lo = 0,
                hi = arr.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (arr[mid] < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }
    }
}
