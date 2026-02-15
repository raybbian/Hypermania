using TMPro;
using UnityEngine;

namespace Game.View.Overlay
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ComboCountView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _comboText;

        private void Start()
        {
            SetComboCount(0);
        }

        public void SetComboCount(int Combo)
        {
            gameObject.SetActive(Combo >= 1);
            _comboText.SetText(Combo.ToString());
        }
    }
}
