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

        public void SetPlayer(CSteamID player)
        {
            _text.text = player.m_SteamID.ToString();
        }
    }
}
