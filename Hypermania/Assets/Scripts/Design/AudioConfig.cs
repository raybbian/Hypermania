using Game;
using UnityEngine;
using Utils;

namespace Design
{
    [CreateAssetMenu(menuName = "Hypermania/Audio Config")]
    public class AudioConfig : ScriptableObject
    {
        public Frame FirstMusicalBeat = Frame.FirstFrame;
        public float BPM = 60;
        public AudioClip AudioClip;

        //Convert BPM to seconds per frame, then seconds to frames
        public int FramesPerBeat => Mathf.RoundToInt(60f / BPM * GameManager.TPS);

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

        public Frame NextBeat(Frame frame, BeatSubdivision subdivision)
        {
            float framesPerSubdivision = FramesPerBeat * ((float)BeatSubdivision.QuarterNote / (float)subdivision);
            int framesSinceFirstBeat = frame - FirstMusicalBeat;

            return new Frame(
                Mathf.RoundToInt(Mathf.CeilToInt(framesSinceFirstBeat / framesPerSubdivision) * framesPerSubdivision)
            );
        }

        public bool BeatWithinWindow(Frame frame, BeatSubdivision subdivision, int windowFrames)
        {
            float framesPerSubdivision = FramesPerBeat * ((float)BeatSubdivision.QuarterNote / (float)subdivision);
            int framesSinceFirstBeat = frame - FirstMusicalBeat;
            // if we havent gotten to first beat yet, then no alignment so far
            if (framesSinceFirstBeat < 0)
            {
                return false;
            }

            int nearestBeatOffset = Mathf.RoundToInt(
                Mathf.RoundToInt(framesSinceFirstBeat / framesPerSubdivision) * framesPerSubdivision
            );

            int distanceToNearestBeat = Mathf.Abs(framesSinceFirstBeat - nearestBeatOffset);

            bool isInWindow = distanceToNearestBeat <= windowFrames;
            return isInWindow;
        }
    }
}
