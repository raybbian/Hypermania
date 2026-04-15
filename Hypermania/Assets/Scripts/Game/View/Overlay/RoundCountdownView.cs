using Design.Configs;
using Game.Sim;
using TMPro;
using UnityEngine;
using Utils;

// Round Countdown View, designed to countdown at the start of a round.
namespace Game.View.Overlay
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    [RequireComponent(typeof(AudioSource))]
    public class RoundCountdownView : MonoBehaviour
    {
        [SerializeField]
        private AudioClip _countdownSfx;

        private TMP_Text _roundCD;
        private AudioSource _audioSource;
        private int _lastBeatIndex;

        public void Awake()
        {
            _roundCD = GetComponent<TextMeshProUGUI>();
            _audioSource = GetComponent<AudioSource>();
            _lastBeatIndex = -1;
        }

        public void DisplayRoundCD(Frame currentFrame, Frame roundStart, GameOptions options)
        {
            var audio = options.Global.Audio;
            int elapsed = currentFrame.No - roundStart.No;
            int totalCountdown = audio.BeatsToFrame(8);

            bool visible = elapsed >= 0 && elapsed <= totalCountdown + audio.FramesPerBeat / 2;
            gameObject.SetActive(visible);
            if (!visible)
            {
                _lastBeatIndex = -1;
                return;
            }

            int beatIndex;
            if (elapsed >= totalCountdown)
            {
                beatIndex = 6;
                _roundCD.SetText("Go!");
            }
            else if (elapsed < audio.BeatsToFrame(2))
            {
                beatIndex = 0;
                _roundCD.SetText("1");
            }
            else if (elapsed < audio.BeatsToFrame(4))
            {
                beatIndex = 1;
                _roundCD.SetText("2");
            }
            else if (elapsed < audio.BeatsToFrame(5))
            {
                beatIndex = 2;
                _roundCD.SetText("1");
            }
            else if (elapsed < audio.BeatsToFrame(6))
            {
                beatIndex = 3;
                _roundCD.SetText("2");
            }
            else if (elapsed < audio.BeatsToFrame(7))
            {
                beatIndex = 4;
                _roundCD.SetText("3");
            }
            else
            {
                beatIndex = 5;
                _roundCD.SetText("4");
            }

            if (beatIndex != _lastBeatIndex)
            {
                _lastBeatIndex = beatIndex;
                if (_countdownSfx != null)
                    _audioSource.PlayOneShot(_countdownSfx);
            }
        }
    }
}
