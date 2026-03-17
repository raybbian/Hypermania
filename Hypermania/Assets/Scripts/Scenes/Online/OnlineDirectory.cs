using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Netcode.P2P;
using Netcode.Rollback;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.Online
{
    [DisallowMultipleComponent]
    public class OnlineDirectory : MonoBehaviour
    {
        private static SteamMatchmakingClient _matchmakingClient;
        private static List<CSteamID> _players;
        public static IReadOnlyList<CSteamID> Players => _players;

        [SerializeField]
        private Button _createLobbyButton;

        [SerializeField]
        private Button _joinLobbyButton;

        [SerializeField]
        private Button _leaveLobbyButton;

        [SerializeField]
        private Button _startGameButton;

        [SerializeField]
        private PlayerList _playerList;

        [SerializeField]
        private TMP_InputField _joinLobbyText;

        [SerializeField]
        private TMP_InputField _createLobbyText;

        public static bool InLobby => _matchmakingClient.InLobby;

        public void Awake()
        {
            _players = new List<CSteamID>();
            _matchmakingClient = new();
        }

        public void OnEnable()
        {
            _matchmakingClient.OnStartWithPlayers += OnStartWithPlayers;
        }

        public void OnDisable()
        {
            // sometimes the online scene gets unloaded, in which case we should leave the lobby
            _matchmakingClient.Leave();
            _matchmakingClient.OnStartWithPlayers -= OnStartWithPlayers;
        }

        public void CreateLobby() => StartCoroutine(CreateLobbyRoutine());

        IEnumerator CreateLobbyRoutine()
        {
            var task = _matchmakingClient.Create();
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }
        }

        public void JoinLobby()
        {
            string txt = new string(_joinLobbyText.text.Where(char.IsDigit).ToArray());

            if (string.IsNullOrWhiteSpace(txt))
            {
                Debug.LogError("Lobby ID is empty.");
                return;
            }

            if (!ulong.TryParse(txt, out ulong id))
            {
                Debug.LogError($"Invalid lobby ID: '{txt}'. Must be a valid ulong.");
                return;
            }

            CSteamID lobbyId = new CSteamID(id);

            if (!lobbyId.IsValid())
            {
                Debug.LogError($"Steam lobby ID {id} is not valid.");
                return;
            }

            StartCoroutine(JoinLobbyRoutine(lobbyId));
        }

        IEnumerator JoinLobbyRoutine(CSteamID lobbyId)
        {
            var task = _matchmakingClient.Join(lobbyId);
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }
        }

        public void LeaveLobby() => StartCoroutine(LeaveLobbyRoutine());

        IEnumerator LeaveLobbyRoutine()
        {
            var task = _matchmakingClient.Leave();
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }
        }

        public void StartGame() => StartCoroutine(StartGameRoutine());

        IEnumerator StartGameRoutine()
        {
            var task = _matchmakingClient.StartGame();
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }
        }

        public void Back()
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                .Unload(SceneID.Online)
                .WithOverlay()
                .Execute();
        }

        void Update()
        {
            _createLobbyButton.interactable = !InLobby;
            _joinLobbyButton.interactable = !InLobby;
            _leaveLobbyButton.interactable = InLobby;
            if (InLobby)
            {
                _createLobbyText.text = _matchmakingClient.CurrentLobby.ToString();
            }

            var players = _matchmakingClient.PlayersInLobby();
            _playerList.UpdatePlayerList(players);

            CSteamID host = SteamMatchmaking.GetLobbyOwner(_matchmakingClient.CurrentLobby);
            _startGameButton.interactable = players != null && players.Count == 2 && host == SteamUser.GetSteamID();
        }

        void OnStartWithPlayers(List<CSteamID> players)
        {
            _players = new List<CSteamID>(players);
            // go to live connection directory
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.LiveConnection, SceneDatabase.LIVE_CONNECTION)
                .Unload(SceneID.MenuBase)
                .WithOverlay()
                .Execute();
        }
    }
}
