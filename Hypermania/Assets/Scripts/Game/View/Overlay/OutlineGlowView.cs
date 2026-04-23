using System;
using Game.Sim;
using UnityEngine;

namespace Game.View.Overlay
{
    public class OutlineGlowView : MonoBehaviour
    {
        [Serializable]
        public struct ColorSet
        {
            [ColorUsage(true, true)]
            public Color Hype;

            [ColorUsage(true, true)]
            public Color Mania;

            [ColorUsage(true, true)]
            public Color Burst;
        }

        [SerializeField]
        private ColorSet _colors;

        [SerializeField]
        [Tooltip("Exponential lerp rate toward the target color. Higher = snappier.")]
        private float _colorLerpSpeed = 10f;

        [SerializeField]
        [Tooltip("How much brighter than baseline the outline flashes on each beat.")]
        private float _beatPulseMagnitude = 0.5f;

        [SerializeField]
        [Tooltip("Exponential attack rate of the beat pulse. Much higher than decay → fast ramp-in to the peak.")]
        private float _beatPulseAttack = 60f;

        [SerializeField]
        [Tooltip("Exponential decay rate of the beat pulse back to baseline. Higher = snappier fall-off.")]
        private float _beatPulseDecay = 8f;

        private Color[] _currentColors;
        private int _lastBeatIndex = -1;
        private float _secondsSinceBeat;

        public void Render(float deltaTime, in GameState state, GameOptions options)
        {
            if (OutlineFeature.Instance == null)
                return;

            int count = state.Fighters.Length;
            if (_currentColors == null || _currentColors.Length != count)
            {
                _currentColors = new Color[count];
                for (int i = 0; i < count; i++)
                    _currentColors[i] = _colors.Hype;
            }

            float hypeRatio = (float)(state.HypeMeter / options.Global.MaxHype);
            float t = 1f - Mathf.Exp(-_colorLerpSpeed * deltaTime);

            // Beat pulse: spike brightness each quarter note, exponentially decay
            // back to baseline before the next beat. Shared across both players
            // since they hear the same track.
            int framesPerBeat = options.Global.Audio.FramesPerBeat;
            int beatIndex = 0;
            if (framesPerBeat > 0)
            {
                long offsetFrames = Math.Max(
                    0L,
                    (long)state.RealFrame.No - (long)options.Global.Audio.FirstMusicalBeat.No
                );
                beatIndex = (int)(offsetFrames / framesPerBeat);
            }
            if (beatIndex != _lastBeatIndex)
            {
                _lastBeatIndex = beatIndex;
                _secondsSinceBeat = 0f;
            }
            else
            {
                _secondsSinceBeat += deltaTime;
            }
            // Pulse ramps in fast (1 - e^-attack*t), then decays slower (e^-decay*t).
            // Attack rate ≫ decay rate → brief rise into the peak, gentle fall back.
            float attack = 1f - Mathf.Exp(-_beatPulseAttack * _secondsSinceBeat);
            float decay = Mathf.Exp(-_beatPulseDecay * _secondsSinceBeat);
            float pulseEnvelope = attack * decay;
            float pulseMultiplier = 1f + _beatPulseMagnitude * pulseEnvelope;

            for (int i = 0; i < count; i++)
            {
                bool inBurst = state.Fighters[i].State == CharacterState.Burst;
                bool inMania = state.Manias[i].Enabled(state.RealFrame) || state.Fighters[i].FreestyleActive;

                Color targetColor;
                float glow;
                if (inBurst)
                {
                    targetColor = _colors.Burst;
                    glow = 1f;
                }
                else if (inMania)
                {
                    targetColor = _colors.Mania;
                    glow = 1f;
                }
                else
                {
                    targetColor = _colors.Hype;
                    float share = i == 0 ? hypeRatio : -hypeRatio;
                    glow = Mathf.Max(0f, share);
                }

                _currentColors[i] = Color.Lerp(_currentColors[i], targetColor, t);
                OutlineFeature.Instance.SetPlayerColor(i, _currentColors[i]);
                OutlineFeature.Instance.SetPlayerGlow(i, glow * pulseMultiplier);
            }
        }
    }
}
