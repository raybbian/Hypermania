using System;
using System.Collections.Generic;
using System.Threading;
using Design;
using Game;
using UnityEngine;
using Utils;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class Conductor : MonoBehaviour
{
    private AudioSource _output;

    private float[] _pcm;
    private int _channels;
    private int _sampleRate;
    private int _totalSamples;
    private int _sliceFrames;
    private AudioClip _streamClip;
    private int _audioStartedFlag;

    private readonly object _qLock = new();
    private readonly Queue<float> _sampleQueue = new();

    [SerializeField]
    private int _maxQueuedSlices = 2;

    public event Action OnAudioStarted;
    private bool _notified;

    private void Update()
    {
        if (!_notified && Volatile.Read(ref _audioStartedFlag) == 1)
        {
            _notified = true;
            OnAudioStarted?.Invoke();
        }
    }

    public void Init(AudioConfig audioConfig)
    {
        _notified = false;
        _audioStartedFlag = 0;
        AudioClip songClip = audioConfig.AudioClip;

        if (songClip == null)
            throw new InvalidOperationException("songClip not assigned.");

        _output = GetComponent<AudioSource>();
        if (_output == null)
            throw new InvalidOperationException("output AudioSource missing.");

        _channels = songClip.channels;
        _sampleRate = songClip.frequency;
        _totalSamples = songClip.samples;

        _pcm = new float[_totalSamples * _channels];
        if (!songClip.GetData(_pcm, 0))
            throw new InvalidOperationException(
                "GetData failed. Set song clip Load Type to Decompress On Load (or ensure it's loadable)."
            );

        _sliceFrames = Mathf.Max(1, Mathf.RoundToInt((float)_sampleRate / GameManager.TPS));

        AudioSettings.GetDSPBufferSize(out int dspFrames, out _);
        int lengthSamples = dspFrames * 2;

        _streamClip = AudioClip.Create(
            name: "ConductorStream",
            lengthSamples: lengthSamples,
            channels: _channels,
            frequency: _sampleRate,
            stream: true,
            pcmreadercallback: OnAudioRead,
            pcmsetpositioncallback: OnAudioSetPosition
        );

        _output.clip = _streamClip;
        _output.loop = true;

        _output.Play();
    }

    public void RequestSlice(Frame frame)
    {
        double startSeconds = (float)frame.No / GameManager.TPS;
        int startSample = (int)Math.Round(startSeconds * _sampleRate);
        RequestSlice(startSample);
    }

    private void RequestSlice(int startFrame)
    {
        if (_pcm == null)
            return;

        int clampedStart = Mathf.Clamp(startFrame, 0, _totalSamples);

        lock (_qLock)
        {
            int queuedSlicesApprox = _sampleQueue.Count / (_sliceFrames * _channels);
            if (queuedSlicesApprox >= _maxQueuedSlices)
                return;

            int framesToCopy = _sliceFrames;
            int available = _totalSamples - clampedStart;
            int copyFrames = Mathf.Clamp(framesToCopy, 0, available);

            int startIndex = clampedStart * _channels;
            int copyCount = copyFrames * _channels;

            for (int i = 0; i < copyCount; i++)
                _sampleQueue.Enqueue(_pcm[startIndex + i]);

            int padCount = (framesToCopy * _channels) - copyCount;
            for (int i = 0; i < padCount; i++)
                _sampleQueue.Enqueue(0f);
        }
    }

    private void OnAudioRead(float[] data)
    {
        Interlocked.CompareExchange(ref _audioStartedFlag, 1, 0);

        lock (_qLock)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = _sampleQueue.Count > 0 ? _sampleQueue.Dequeue() : 0f;
        }
    }

    private void OnAudioSetPosition(int newPosition) { }
}
