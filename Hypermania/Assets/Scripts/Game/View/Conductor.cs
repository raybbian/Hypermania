using System;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using Utils;
using Utils.EnumArray;

namespace Game.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class Conductor : MonoBehaviour
    {
        private AudioSource _output;

        private float[][] _pcms;

        private int _channels;
        private int _sampleRate;
        private int _outputSampleRate;
        private int _totalSamples;

        private double _sourceFramesPerOutputFrame;
        private double _sourceFrameCursor;
        private double _loopStartFrame;
        private double _targetSourceFrame;
        private bool _hasStarted;
        private int _startDelayTicks;

        public float t;

        [SerializeField]
        private float _msOffset = -83f;

        [SerializeField]
        private float _maxQueuedSeconds = 0.2f;

        [SerializeField]
        private bool _loopSong = true;

        private float[] _activeTrackPcm;

        private readonly object _lock = new();

        public void Init(GameOptions options)
        {
            _sourceFrameCursor = 0.0;
            _targetSourceFrame = 0.0;
            t = 0.0f;
            _hasStarted = false;
            _startDelayTicks = options.Global.PreGameDelayTicks;

            AudioClip songClip = options.Global.Audio.AudioClip;
            if (songClip == null)
                throw new InvalidOperationException("songClip not assigned.");

            _output = GetComponent<AudioSource>();
            if (_output == null)
                throw new InvalidOperationException("output AudioSource missing.");

            _channels = songClip.channels;
            _sampleRate = songClip.frequency;
            _outputSampleRate = AudioSettings.outputSampleRate;
            _totalSamples = songClip.samples;

            if (_outputSampleRate <= 0)
                throw new InvalidOperationException("Invalid output sample rate.");

            _sourceFramesPerOutputFrame = (double)_sampleRate / _outputSampleRate;

            _pcms = new float[3][];
            AudioClip[] clips = new[]
            {
                options.Global.Audio.CharacterThemes[options.Players[0].Character.Character],
                songClip,
                options.Global.Audio.CharacterThemes[options.Players[1].Character.Character],
            };
            for (int i = 0; i < 3; i++)
            {
                _pcms[i] = new float[_totalSamples * _channels];
                AudioClip themeClip = clips[i];
                if (themeClip == null)
                    throw new InvalidOperationException($"CharacterThemes entry is null.");

                if (themeClip.channels != _channels)
                {
                    throw new InvalidOperationException(
                        $"Character theme channel count mismatch. Expected {_channels}, got {themeClip.channels}."
                    );
                }

                if (themeClip.frequency != _sampleRate)
                {
                    throw new InvalidOperationException(
                        $"Character theme sample rate mismatch. Expected {_sampleRate}, got {themeClip.frequency}."
                    );
                }

                if (themeClip.samples != _totalSamples)
                {
                    throw new InvalidOperationException(
                        $"Character theme sample count mismatch. Expected {_totalSamples}, got {themeClip.samples}."
                    );
                }

                if (!themeClip.GetData(_pcms[i], 0))
                {
                    throw new InvalidOperationException(
                        $"GetData failed for character theme. Set clip Load Type to Decompress On Load."
                    );
                }
            }

            _loopStartFrame = ComputeLoopStartFrame(options.Global.Audio);

            _output.Stop();
            _output.clip = null;
            _output.loop = false;
            _output.Play();
        }

        public void SetFrame(Frame frame)
        {
            SetFrame((long)frame.No);
        }

        public void SetFrame(long frameNo)
        {
            long effectiveFrame = frameNo - _startDelayTicks;

            lock (_lock)
            {
                if (effectiveFrame < 0)
                {
                    _sourceFrameCursor = 0.0;
                    _targetSourceFrame = 0.0;
                    _hasStarted = false;
                    return;
                }

                double seconds = (double)effectiveFrame / GameManager.TPS - _msOffset / 1000.0;
                double sourceFrames = seconds * _sampleRate;

                _sourceFrameCursor = ClampOrWrapSourceFrame(sourceFrames);
                _targetSourceFrame = _sourceFrameCursor;
                _hasStarted = true;
            }
        }

        public void PublishTick(Frame frame, double deltaTime)
        {
            if (frame.No < _startDelayTicks)
                return;

            if (!_hasStarted)
                SetFrame(frame);

            PublishTick(deltaTime);
        }

        public void PublishTick(double deltaTime)
        {
            if (_pcms == null || deltaTime <= 0.0)
                return;

            double framesToPublish = deltaTime * _sampleRate;
            if (framesToPublish <= 0.0)
                return;

            lock (_lock)
            {
                if (!_hasStarted)
                    return;

                double maxLeadFrames = Math.Max(1.0, _maxQueuedSeconds * _sampleRate);
                double newTarget = AdvancePlaybackFrame(_targetSourceFrame, framesToPublish);

                double lead = GetForwardDistanceInPlaybackSpace(_sourceFrameCursor, newTarget);
                if (lead > maxLeadFrames)
                    newTarget = AdvancePlaybackFrame(_sourceFrameCursor, maxLeadFrames);

                _targetSourceFrame = newTarget;
            }
        }

        private void OnAudioFilterRead(float[] data, int outputChannels)
        {
            if (_pcms == null || _channels <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            lock (_lock)
            {
                int outputFrames = data.Length / outputChannels;

                for (int frame = 0; frame < outputFrames; frame++)
                {
                    int dstBase = frame * outputChannels;

                    if (!_hasStarted)
                    {
                        WriteSilenceFrame(data, dstBase, outputChannels);
                        continue;
                    }

                    double playbackFrame = ClampOrWrapSourceFrame(_sourceFrameCursor);
                    WriteCurrentPlaybackFrame(data, dstBase, outputChannels, playbackFrame);

                    double remaining = GetForwardDistanceInPlaybackSpace(_sourceFrameCursor, _targetSourceFrame);

                    double baseStep = _sourceFramesPerOutputFrame;

                    // Small bounded correction, not a snap.
                    double maxStep = baseStep * 1.02;
                    double step = Math.Min(Math.Max(baseStep, remaining), maxStep);

                    _sourceFrameCursor = AdvancePlaybackFrame(_sourceFrameCursor, step);
                }

                _sourceFrameCursor = ClampOrWrapSourceFrame(_sourceFrameCursor);
            }
        }

        private void WriteCurrentPlaybackFrame(float[] data, int dstBase, int outputChannels, double playbackFrame)
        {
            WriteCrossfadedFrame(data, dstBase, outputChannels, playbackFrame, t);
        }

        private void WriteCrossfadedFrame(float[] data, int dstBase, int outputChannels, double sourceFrame, float t)
        {
            if (_pcms == null)
            {
                WriteSilenceFrame(data, dstBase, outputChannels);
                return;
            }
            float[] fromTrackPcm = _pcms[1];
            float[] toTrackPcm = t <= 0f ? _pcms[0] : _pcms[2];
            float newT = Math.Abs(t);
            if (fromTrackPcm == null || toTrackPcm == null)
            {
                WriteSilenceFrame(data, dstBase, outputChannels);
                return;
            }

            float gainFrom = Mathf.Cos(newT * Mathf.PI * 0.5f);
            float gainTo = Mathf.Sin(newT * Mathf.PI * 0.5f);

            for (int ch = 0; ch < outputChannels; ch++)
            {
                int srcCh = GetMappedChannel(ch);
                float a = SampleTrackLinear(fromTrackPcm, sourceFrame, srcCh);
                float b = SampleTrackLinear(toTrackPcm, sourceFrame, srcCh);
                data[dstBase + ch] = a * gainFrom + b * gainTo;
            }
        }

        private float SampleTrackLinear(float[] trackPcm, double sourceFrame, int channel)
        {
            if (trackPcm == null || _totalSamples <= 0)
                return 0f;

            double wrappedFrame = ClampOrWrapSourceFrame(sourceFrame);

            int i0 = GetReadableSourceFrameIndex(wrappedFrame);
            if (i0 < 0)
                return 0f;

            double frac = wrappedFrame - Math.Floor(wrappedFrame);

            int i1;
            if (_loopSong)
            {
                i1 = GetReadableSourceFrameIndex(wrappedFrame + 1.0);
                if (i1 < 0)
                    i1 = i0;
            }
            else
            {
                i1 = Math.Min(i0 + 1, _totalSamples - 1);
            }

            float a = trackPcm[i0 * _channels + channel];
            float b = trackPcm[i1 * _channels + channel];
            return a + (float)((b - a) * frac);
        }

        private int GetMappedChannel(int outputChannel)
        {
            if (_channels <= 1)
                return 0;

            return outputChannel % _channels;
        }

        private void WriteSilenceFrame(float[] data, int dstBase, int outputChannels)
        {
            for (int ch = 0; ch < outputChannels; ch++)
                data[dstBase + ch] = 0f;
        }

        private double ComputeLoopStartFrame(AudioConfig audioConfig)
        {
            if (!_loopSong || _totalSamples <= 0)
                return 0.0;

            double bpm = (double)audioConfig.Bpm;
            double loopBeat = Math.Max(0.0, audioConfig.LoopBeat);

            if (bpm <= 0.0)
                return 0.0;

            double loopStartSeconds = loopBeat * 60.0 / bpm;
            double loopStartFrame = loopStartSeconds * _sampleRate;
            return Math.Clamp(loopStartFrame, 0.0, _totalSamples - 1);
        }

        private int GetReadableSourceFrameIndex(double sourceFrame)
        {
            if (_totalSamples <= 0)
                return -1;

            if (_loopSong)
            {
                sourceFrame = WrapLoopingSourceFrame(sourceFrame);
                int frame = (int)Math.Floor(sourceFrame);
                return Math.Clamp(frame, 0, _totalSamples - 1);
            }

            int clamped = (int)Math.Floor(sourceFrame);
            return clamped < 0 || clamped >= _totalSamples ? -1 : clamped;
        }

        private double ClampOrWrapSourceFrame(double sourceFrame)
        {
            if (_totalSamples <= 0)
                return 0.0;

            return _loopSong ? WrapLoopingSourceFrame(sourceFrame) : Math.Clamp(sourceFrame, 0.0, _totalSamples);
        }

        private double WrapLoopingSourceFrame(double sourceFrame)
        {
            if (_totalSamples <= 0)
                return 0.0;

            double endFrame = _totalSamples;
            double loopStart = Math.Clamp(_loopStartFrame, 0.0, Math.Max(0, _totalSamples - 1));

            if (sourceFrame < endFrame)
                return Math.Max(0.0, sourceFrame);

            double loopLength = endFrame - loopStart;
            if (loopLength <= 0.0)
                return loopStart;

            double wrapped = (sourceFrame - loopStart) % loopLength;
            if (wrapped < 0.0)
                wrapped += loopLength;

            return loopStart + wrapped;
        }

        private double GetForwardDistanceInPlaybackSpace(double fromFrame, double toFrame)
        {
            fromFrame = ClampOrWrapSourceFrame(fromFrame);
            toFrame = ClampOrWrapSourceFrame(toFrame);

            if (!_loopSong)
                return Math.Max(0.0, toFrame - fromFrame);

            if (toFrame >= fromFrame)
                return toFrame - fromFrame;

            double endFrame = _totalSamples;
            double loopStart = Math.Clamp(_loopStartFrame, 0.0, Math.Max(0, _totalSamples - 1));
            return (endFrame - fromFrame) + (toFrame - loopStart);
        }

        private double AdvancePlaybackFrame(double sourceFrame, double deltaFrames)
        {
            return ClampOrWrapSourceFrame(sourceFrame + deltaFrames);
        }
    }
}
