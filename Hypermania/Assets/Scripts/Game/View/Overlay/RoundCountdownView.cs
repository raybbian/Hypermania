using Design.Configs;
using TMPro;
using UnityEngine;
using Utils;

// Round Countdown View, designed to countdown at the start of a round.
namespace Game.View.Overlay
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class RoundCountdownView : MonoBehaviour
    {
        private TMP_Text _roundCD;
        float time;

        public void Awake()
        {
            _roundCD = GetComponent<TextMeshProUGUI>();
        }

        public void DisplayRoundCD(Frame currentFrame, Frame roundStart, GlobalConfig config)
        {
            time = (currentFrame.No - roundStart.No) / 60;
            gameObject.SetActive(time <= config.RoundCountdownTicks / 60 + 0.5);
            if (time < config.RoundCountdownTicks / 60)
            {
                _roundCD.SetText((3 - Mathf.FloorToInt(time)).ToString());
            }
            else
            {
                _roundCD.SetText("Go!");
            }
        }
    }
}
