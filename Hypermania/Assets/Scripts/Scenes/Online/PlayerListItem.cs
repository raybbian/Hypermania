using Steamworks;
using TMPro;
using UnityEngine;

namespace Scenes.Online
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class PlayerListItem : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _text;

        public void SetPlayer(CSteamID player, bool isHost)
        {
            string name = SteamFriends.GetFriendPersonaName(player);
            _text.text = isHost ? name + " (Host)" : name;
        }
    }
}
