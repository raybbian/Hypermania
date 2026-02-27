using Game;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Design.Configs
{
    [CreateAssetMenu(menuName = "Hypermania/Audio Config")]
    public class AudioConfig : ScriptableObject
    {
        public Frame FirstMusicalBeat = Frame.FirstFrame;
        public sfloat BPM = 60;
        public AudioClip AudioClip;

        //Convert BPM to seconds per frame, then seconds to frames
        public int FramesPerBeat => Mathsf.RoundToInt((sfloat)60f / BPM * GameManager.TPS);

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
            sfloat framesPerSubdivision =
                FramesPerBeat * ((sfloat)(int)BeatSubdivision.QuarterNote / (sfloat)(int)subdivision);
            int framesSinceFirstBeat = frame - FirstMusicalBeat;

            return new Frame(
                Mathsf.RoundToInt(Mathsf.CeilToInt(framesSinceFirstBeat / framesPerSubdivision) * framesPerSubdivision)
            );
        }

        public Frame ClosestBeat(Frame frame, BeatSubdivision subdivision)
        {
            int framesSinceFirstBeat = frame - FirstMusicalBeat;
            sfloat framesPerSubdivision =
                FramesPerBeat * ((sfloat)(int)BeatSubdivision.QuarterNote / (sfloat)(int)subdivision);
            return new Frame(
                Mathsf.RoundToInt(Mathsf.RoundToInt(framesSinceFirstBeat / framesPerSubdivision) * framesPerSubdivision)
            );
        }

        public bool BeatWithinWindow(Frame frame, BeatSubdivision subdivision, int windowFrames)
        {
            Frame beatFrame = ClosestBeat(frame, subdivision);
            int distanceToNearestBeat = Mathsf.Abs(frame - beatFrame);
            bool isInWindow = distanceToNearestBeat <= windowFrames;
            return isInWindow;
        }
    }
}
