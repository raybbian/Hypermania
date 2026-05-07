using TMPro;
using UnityEngine;

namespace Game.View.Overlay
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class UsernamePlateView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _usernameText;

        public void Init(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            _usernameText.SetText(username);
        }
    }
}
