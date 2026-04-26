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

        public static bool InLobby =>
            OnlineBaseDirectory.Matchmaking != null && OnlineBaseDirectory.Matchmaking.InLobby;

        private SteamMatchmakingClient Matchmaking => OnlineBaseDirectory.Matchmaking;

        public void Awake()
        {
            _players ??= new List<CSteamID>();
        }

        public void OnEnable()
        {
            if (Matchmaking != null)
                Matchmaking.OnStartWithPlayers += OnStartWithPlayers;
        }

        public void OnDisable()
        {
            if (Matchmaking != null)
            {
                Matchmaking.OnStartWithPlayers -= OnStartWithPlayers;
                Matchmaking.SetSelfInOnlineLobby(false);
            }
        }

        public void CreateLobby() => StartCoroutine(CreateLobbyRoutine());

        IEnumerator CreateLobbyRoutine()
        {
            if (Matchmaking == null)
            {
                Debug.LogError("[Online] Matchmaking unavailable; OnlineBase scene missing or Steam not initialized.");
                yield break;
            }
            var task = Matchmaking.Create();
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
            if (Matchmaking == null)
            {
                Debug.LogError("[Online] Matchmaking unavailable; OnlineBase scene missing or Steam not initialized.");
                return;
            }
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
            var task = Matchmaking.Join(lobbyId);
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                LobbyVersionMismatchException mismatch = task
                    .Exception?.Flatten()
                    .InnerExceptions.OfType<LobbyVersionMismatchException>()
                    .FirstOrDefault();
                if (mismatch != null)
                {
                    Debug.LogError(
                        $"[Online] Cannot join lobby: build mismatch. Your build is '{mismatch.Expected}', host is '{mismatch.Actual}'."
                    );
                    yield break;
                }
                Debug.LogException(task.Exception);
                yield break;
            }
        }

        public void LeaveLobby() => StartCoroutine(LeaveLobbyRoutine());

        IEnumerator LeaveLobbyRoutine()
        {
            if (Matchmaking == null)
                yield break;
            var task = Matchmaking.Leave();
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
            if (Matchmaking == null)
                yield break;
            var task = Matchmaking.StartGame();
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
                .Unload(SceneID.OnlineBase)
                .WithOverlay()
                .Execute();
        }

        void Update()
        {
            if (!SteamManager.IsInitialized || Matchmaking == null)
                return;

            Matchmaking.SetSelfInOnlineLobby(true);

            _createLobbyButton.interactable = !InLobby;
            _joinLobbyButton.interactable = !InLobby;
            _leaveLobbyButton.interactable = InLobby;
            if (InLobby)
            {
                _createLobbyText.text = Matchmaking.CurrentLobby.ToString();
            }

            var players = Matchmaking.PlayersInLobby();
            _playerList.UpdatePlayerList(players);

            CSteamID host = SteamMatchmaking.GetLobbyOwner(Matchmaking.CurrentLobby);
            _startGameButton.interactable =
                players != null
                && players.Count == 2
                && host == SteamUser.GetSteamID()
                && Matchmaking.AllMembersInOnlineLobby();
        }

        void OnStartWithPlayers(List<CSteamID> players)
        {
            _players = new List<CSteamID>(players);
            // Transition from Online lobby to CharacterSelect. OnlineBase stays
            // loaded so the SteamMatchmakingClient (and its lobby) survive the
            // scene swap.
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.CharacterSelect, SceneDatabase.CHARACTER_SELECT)
                .Unload(SceneID.Online)
                .Unload(SceneID.MenuBase)
                .WithOverlay()
                .Execute();
        }
    }
}
