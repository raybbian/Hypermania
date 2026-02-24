using TMPro;
using UnityEngine;
using Utils;

namespace Game.View
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class DD_RoundTimerView : MonoBehaviour
    {
        private TMP_Text _roundTimer;
        int time;

        public void Awake()
        {
            _roundTimer = GetComponent<TextMeshProUGUI>();
        }

        public void DisplayRoundTimer(Frame currentFrame, Frame roundEnd)
        {
            time = (roundEnd.No - currentFrame.No) / 60;
            gameObject.SetActive(time >= 0);
            _roundTimer.text = time.ToString();
        }
    }
}
