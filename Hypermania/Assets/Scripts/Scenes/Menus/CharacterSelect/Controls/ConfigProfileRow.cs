using TMPro;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect.Controls
{
    /// <summary>Display-only row in the profile list. Stateless.</summary>
    public class ConfigProfileRow : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _label;

        [Tooltip("Shown while the cursor is on this row.")]
        [SerializeField]
        private GameObject _focusIndicator;

        [Tooltip("Optional — shown on the \"+ New Profile\" dummy so it can be styled differently.")]
        [SerializeField]
        private GameObject _newDummyBadge;

        [Tooltip("Optional — shown when this profile is currently being edited by the other player's menu.")]
        [SerializeField]
        private GameObject _takenByOtherIndicator;

        public void SetDisplay(string label, bool isNewDummy, bool focused, bool takenByOther)
        {
            if (_label != null)
                _label.text = label;
            if (_focusIndicator != null)
                _focusIndicator.SetActive(focused);
            if (_newDummyBadge != null)
                _newDummyBadge.SetActive(isNewDummy);
            if (_takenByOtherIndicator != null)
                _takenByOtherIndicator.SetActive(takenByOther);
        }
    }
}
