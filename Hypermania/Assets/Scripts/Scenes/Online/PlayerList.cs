using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Scenes.Online
{
    public class PlayerList : MonoBehaviour
    {
        [SerializeField]
        private GameObject _playerListPrefab;

        private List<PlayerListItem> _players = new();

        public void UpdatePlayerList(List<CSteamID> players)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                _players[i].gameObject.SetActive(false);
            }
            if (players == null)
            {
                return;
            }
            for (int i = 0; i < players.Count; i++)
            {
                if (i >= _players.Count)
                {
                    GameObject obj = Instantiate(_playerListPrefab, transform, false);
                    _players.Add(obj.GetComponent<PlayerListItem>());
                }
                _players[i].gameObject.SetActive(true);
                _players[i].SetPlayer(players[i]);
            }
        }
    }
}
