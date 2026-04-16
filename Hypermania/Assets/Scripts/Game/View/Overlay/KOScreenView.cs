using Game.Sim;
using TMPro;
using UnityEngine;

namespace Game.View.Overlay
{
    public class KOScreenView : MonoBehaviour
    {
        public void Render(in GameState state)
        {
            if (state.GameMode == GameMode.RoundEnd && state.RealFrame - state.ModeStart > GameManager.ROLLBACK_FRAMES)
            {
                gameObject.SetActive(true);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
