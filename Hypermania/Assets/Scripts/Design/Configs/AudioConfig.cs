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
        /// first playthrough of the song. Populated from MIDI via
        /// Hypermania.Utils/midi_to_frames.py.
        /// </summary>
        public Frame[] NoteFrames =
        {
            0, 11, 23, 34, 57, 68, 80, 103, 114, 125, 137, 159, 182, 194, 205, 216, 239, 251, 262, 285, 296, 308, 319, 342, 365, 376, 387, 399, 422, 433, 444, 467, 478, 490, 501, 524, 547, 558, 570, 581, 604, 615, 627, 638, 649, 661, 672, 684, 695, 706, 718, 729, 752, 775, 797, 809, 820, 843, 866, 889, 900, 911, 934, 957, 980, 991, 1003, 1025, 1048, 1071, 1082, 1094, 1116, 1139, 1162, 1173, 1185, 1208, 1230, 1253, 1265, 1276, 1299, 1322, 1344, 1356, 1367, 1390, 1413, 1435, 1447, 1458, 1481, 1504, 1527, 1549, 1572, 1595, 1618, 1641, 1652, 1675, 1697, 1720, 1743, 1766, 1789, 1811, 1823, 1834, 1857, 1880, 1903, 1925, 1948, 1971, 1994, 2005, 2016, 2039, 2062, 2085, 2108, 2130, 2153, 2176, 2187, 2199, 2222, 2244, 2267, 2290, 2313, 2335, 2358, 2370, 2381, 2392, 2404, 2427, 2438, 2449, 2472, 2484, 2495, 2506, 2529, 2552, 2563, 2575, 2586, 2609, 2620, 2632, 2654, 2666, 2677, 2689, 2711, 2734, 2746, 2757, 2768, 2791, 2803, 2814, 2837, 2848, 2859, 2871, 2894, 2916, 2928, 2939, 2951, 2973, 2985, 2996, 3008, 3030, 3053, 3076, 3099, 3122, 3144, 3156, 3167, 3178, 3190, 3213, 3235, 3258, 3281, 3304, 3327, 3349, 3372, 3384, 3406, 3429, 3452, 3463, 3486, 3509, 3532, 3543, 3554, 3577, 3600, 3623, 3634, 3646, 3668, 3680, 3691, 3714, 3725, 3737, 3759, 3782, 3805, 3816, 3828, 3851, 3873, 3896, 3919, 3942, 3965, 3987, 3999, 4010, 4033, 4056, 4078, 4101, 4124, 4147, 4170, 4181, 4192, 4215, 4238, 4261, 4272, 4284, 4306, 4329, 4352, 4363, 4375, 4397, 4420, 4443, 4454, 4466, 4489, 4511, 4534, 4557, 4580, 4591, 4603, 4625, 4648, 4659, 4671, 4694, 4716, 4739, 4762, 4773, 4785, 4808, 4830, 4842, 4853, 4876, 4899, 4922, 4944, 4956, 4967, 4990, 5013, 5024, 5035, 5058, 5081, 5104, 5127, 5149, 5172, 5195, 5218, 5240, 5263
        };

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
        /// Return a slice of the note chart spanning <see cref="ComboBeatCount"/>
        /// beats starting at or after <paramref name="minStart"/>. When the song
        /// loops, notes from the loop section are re-emitted at correct absolute
        /// frame positions. Loop iteration offsets are computed via continuous math
        /// (<see cref="BeatsToFrame"/>) to avoid cumulative rounding drift.
        /// </summary>
        public Frame[] SliceFrom(Frame minStart)
        {
            if (NoteFrames == null || NoteFrames.Length == 0)
                return Array.Empty<Frame>();

            Frame endBound = minStart + BeatsToFrame(ComboBeatCount);
            int songEnd = BeatsToFrame(SongLengthBeats);
            int loopStartFrame = BeatsToFrame(LoopBeat);
            int loopBeats = SongLengthBeats - LoopBeat;

            var result = new List<Frame>();

            int first = LowerBound(NoteFrames, minStart);

            // First playthrough: notes in [minStart, min(endBound, songEnd))
            for (int i = first; i < NoteFrames.Length; i++)
            {
                if (NoteFrames[i].No >= songEnd || NoteFrames[i] > endBound)
                    break;
                result.Add(NoteFrames[i]);
            }

            if (endBound.No <= songEnd || loopBeats <= 0)
                return result.ToArray();

            // Loop-section note indices via binary search
            int loopFirst = LowerBound(NoteFrames, (Frame)loopStartFrame);
            int loopEnd = LowerBound(NoteFrames, (Frame)songEnd);
            if (loopFirst >= loopEnd)
                return result.ToArray();

            int approxLoopLen = songEnd - loopStartFrame;
            int startIter = minStart.No >= songEnd
                ? Math.Max(1, (minStart.No - songEnd) / approxLoopLen)
                : 1;

            for (int n = startIter; ; n++)
            {
                // Continuous math: rounds once per iteration, no cumulative drift.
                int iterStart = BeatsToFrame(LoopBeat + n * loopBeats);

                int firstAbsolute = NoteFrames[loopFirst].No - loopStartFrame + iterStart;
                if (firstAbsolute > endBound.No)
                    break;

                for (int i = loopFirst; i < loopEnd; i++)
                {
                    int absolute = NoteFrames[i].No - loopStartFrame + iterStart;
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
            int lo = 0, hi = arr.Length;
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
