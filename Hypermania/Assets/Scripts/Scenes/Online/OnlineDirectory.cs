using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Netcode.P2P;
using Netcode.Rollback;
using Scenes.Core;
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

        // Says how creating or joining a lobby went.
        [SerializeField]
        private TextMeshProUGUI _statusText;

        // True while a lobby is being created or joined.
        private bool _busy;

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
            _busy = false;
            _statusText.text = "";
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
            if (_busy)
                yield break;
            _busy = true;
            var task = Matchmaking.Create();
            while (!task.IsCompleted)
                yield return null;
            _busy = false;
            if (task.IsFaulted)
            {
                _statusText.text = "Couldn't create a lobby.";
                Debug.LogException(task.Exception);
                yield break;
            }
            _statusText.text = "";
        }

        // Opens Steam's invite dialog for the lobby.
        public void InviteFriends() => StartCoroutine(InviteFriendsRoutine());

        IEnumerator InviteFriendsRoutine()
        {
            if (Matchmaking == null || !OverlayAvailable())
                yield break;
            // Inviting before there's a lobby opens one to invite them to.
            if (!InLobby)
            {
                yield return CreateLobbyRoutine();
                if (!InLobby)
                    yield break;
            }
            SteamFriends.ActivateGameOverlayInviteDialog(Matchmaking.CurrentLobby);
        }

        // Opens the overlay itself. Asking for its friends dialog pops the
        // desktop friends window instead. Join Game on a friend in a lobby
        // comes back to us through SteamJoinRequests.
        public void JoinFriend()
        {
            if (OverlayAvailable())
                SteamFriends.ActivateGameOverlay("");
        }

        // The overlay only exists when Steam launched the game, so never in the editor.
        private bool OverlayAvailable()
        {
            if (SteamUtils.IsOverlayEnabled())
                return true;
            _statusText.text = "The Steam overlay isn't running. Invite or join from your Steam friends list instead.";
            return false;
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

            Join(lobbyId);
        }

        private void Join(CSteamID lobbyId) => StartCoroutine(JoinLobbyRoutine(lobbyId));

        IEnumerator JoinLobbyRoutine(CSteamID lobbyId)
        {
            if (Matchmaking == null || _busy)
                yield break;
            _busy = true;
            _statusText.text = "Joining...";
            var task = Matchmaking.Join(lobbyId);
            while (!task.IsCompleted)
                yield return null;
            _busy = false;
            if (task.IsFaulted)
            {
                _statusText.text = DescribeJoinFailure(task.Exception);
                Debug.LogWarning($"[Online] Couldn't join lobby {lobbyId.m_SteamID}: {task.Exception}");
                yield break;
            }
            _statusText.text = "";
        }

        private static string DescribeJoinFailure(AggregateException exception)
        {
            foreach (Exception inner in exception.Flatten().InnerExceptions)
            {
                if (inner is LobbyVersionMismatchException)
                    return "That lobby is on a different version of the game.";
                if (inner is LobbyJoinFailedException failed)
                {
                    switch (failed.Response)
                    {
                        case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                            return "That lobby is full.";
                        case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                            return "That lobby has closed.";
                        case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                            return "That lobby is only open to the host's friends.";
                    }
                }
            }
            return "Couldn't join that lobby.";
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

            // A lobby Steam asked us to join, from an invite or the friends list.
            if (!_busy && SteamJoinRequests.TryTake(out CSteamID requested) && requested != Matchmaking.CurrentLobby)
                Join(requested);

            _createLobbyButton.interactable = !InLobby && !_busy;
            _joinLobbyButton.interactable = !InLobby && !_busy;
            _leaveLobbyButton.interactable = InLobby;
            _createLobbyText.text = InLobby ? Matchmaking.CurrentLobby.ToString() : "";

            var players = Matchmaking.PlayersInLobby();
            CSteamID host = SteamMatchmaking.GetLobbyOwner(Matchmaking.CurrentLobby);
            _playerList.UpdatePlayerList(players, host);

            if (players == null)
                SteamPresence.Set("Online: Setting up a match");
            else
                SteamPresence.Set(players.Count < 2 ? "Online: Waiting for an opponent" : "Online: In a lobby");

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
