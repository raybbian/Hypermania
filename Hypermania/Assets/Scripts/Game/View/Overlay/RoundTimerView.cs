using Design.Configs;
using Game.Sim;
using TMPro;
using UnityEngine;
using Utils;

namespace Game.View.Overlay
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class RoundTimerView : MonoBehaviour
    {
        private TMP_Text _roundTimer;
        int time;

        public void Awake()
        {
            _roundTimer = GetComponent<TextMeshProUGUI>();
        }

        public void DisplayRoundTimer(Frame currentFrame, Frame roundEnd, GameMode gameMode, GlobalConfig config)
        {
            if (gameMode == GameMode.Countdown)
            {
                time = config.RoundTimeTicks / 60;
            }
            else
            {
                time = (roundEnd.No - currentFrame.No) / 60;
            }
            gameObject.SetActive(time >= 0);
            _roundTimer.text = time.ToString();
        }
    }
}
