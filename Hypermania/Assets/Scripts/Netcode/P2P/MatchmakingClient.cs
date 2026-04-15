using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;

namespace Netcode.P2P
{
    public sealed class SteamMatchmakingClient
    {
        public CSteamID CurrentLobby => _currentLobby;
        public bool InLobby => _currentLobby.IsValid();
        public Action<List<CSteamID>> OnStartWithPlayers;

        /// <summary>
        /// Fires when any lobby member broadcasts a back request (see
        /// <see cref="SendBackRequest"/>). Fires on the sender too — all
        /// peers should treat this as "leave the current in-lobby scene
        /// and return to the Online lobby screen."
        /// </summary>
        public Action OnBackRequested;

        public SteamMatchmakingClient()
        {
            if (!SteamManager.Initialized)
            {
                throw new InvalidOperationException("Steam manager was not initialized");
            }
            RegisterCallbacks();

            Debug.Log("[Matchmaking] SteamMatchmakingClient constructed.");
        }

        public async Task<CSteamID> Create(int maxMembers = 2)
        {
            if (maxMembers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMembers));
            Debug.Log($"[Matchmaking] Create(maxMembers={maxMembers})");
            await Leave();

            _lobbyCreatedTcs = new TaskCompletionSource<CSteamID>(TaskCreationOptions.RunContinuationsAsynchronously);
            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, maxMembers);
            _lobbyCreatedCallResult.Set(call);

            Debug.Log("[Matchmaking] CreateLobby issued. Waiting for LobbyCreated_t...");
            var lobbyId = await _lobbyCreatedTcs.Task;

            _currentLobby = lobbyId;
            Debug.Log($"[Matchmaking] Lobby created: {_currentLobby.m_SteamID}");

            SteamMatchmaking.SetLobbyData(_currentLobby, "version", "1");
            SteamMatchmaking.SetLobbyData(_currentLobby, "game", "Hypermania");

            Debug.Log($"[Matchmaking] Lobby data set.");

            return lobbyId;
        }

        public async Task Join(CSteamID lobbyId)
        {
            if (!lobbyId.IsValid())
                throw new ArgumentException("Invalid lobby id.", nameof(lobbyId));
            Debug.Log($"[Matchmaking] Join(lobbyId={lobbyId.m_SteamID})");
            await Leave();

            _lobbyEnterTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SteamMatchmaking.JoinLobby(lobbyId);

            Debug.Log("[Matchmaking] JoinLobby issued. Waiting for LobbyEnter_t...");
            await _lobbyEnterTcs.Task;

            _currentLobby = lobbyId;
            Debug.Log($"[Matchmaking] Joined lobby: {_currentLobby.m_SteamID}");
        }

        public Task Leave()
        {
            Debug.Log("[Matchmaking] Leave()");
            if (_currentLobby.IsValid())
            {
                if (SteamManager.IsInitialized)
                {
                    Debug.Log($"[Matchmaking] Leaving lobby {_currentLobby.m_SteamID}");
                    SteamMatchmaking.LeaveLobby(_currentLobby);
                }
                _currentLobby = default;
            }
            return Task.CompletedTask;
        }

        public Task StartGame()
        {
            if (!_currentLobby.IsValid())
                throw new InvalidOperationException("Not in a lobby.");

            CSteamID host = SteamMatchmaking.GetLobbyOwner(_currentLobby);

            if (host != SteamUser.GetSteamID())
            {
                throw new InvalidOperationException("Non-host tried to start the game");
            }

            Debug.Log($"[Matchmaking] StartGame(): lobby={_currentLobby.m_SteamID}");

            SendLobbyStartMessage();

            Debug.Log("[Matchmaking] Host sent START message.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Broadcasts a back-to-lobby signal to every member (sender included).
        /// Callers subscribe to <see cref="OnBackRequested"/> to react. Fire-
        /// and-forget: no state is persisted, so there are no stale-signal
        /// issues across sessions.
        /// </summary>
        public Task SendBackRequest()
        {
            if (!_currentLobby.IsValid())
                return Task.CompletedTask;

            Debug.Log($"[Matchmaking] SendBackRequest(): lobby={_currentLobby.m_SteamID}");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(BACK_MSG);
            SteamMatchmaking.SendLobbyChatMsg(_currentLobby, bytes, bytes.Length);
            return Task.CompletedTask;
        }

        private const string START_MSG = "__START";
        private const string BACK_MSG = "__BACK";

        private CSteamID _currentLobby;

        private Callback<LobbyEnter_t> _lobbyEnterCb;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCb;
        private Callback<LobbyChatMsg_t> _lobbyChatMsgCb;
        private Callback<GameLobbyJoinRequested_t> _joinRequestedCb;
        private CallResult<LobbyCreated_t> _lobbyCreatedCallResult;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCb;

        private TaskCompletionSource<CSteamID> _lobbyCreatedTcs;
        private TaskCompletionSource<bool> _lobbyEnterTcs;

        private void RegisterCallbacks()
        {
            Debug.Log("[Matchmaking] RegisterCallbacks()");
            _lobbyEnterCb = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyChatUpdateCb = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _lobbyChatMsgCb = Callback<LobbyChatMsg_t>.Create(OnLobbyChatMessage);
            _joinRequestedCb = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);

            _lobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyDataUpdateCb = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        }

        private void OnLobbyCreated(LobbyCreated_t data, bool ioFailure)
        {
            if (_lobbyCreatedTcs == null)
            {
                Debug.Log("[Matchmaking] OnLobbyCreated: no TCS (ignored).");
                return;
            }

            Debug.Log(
                $"[Matchmaking] OnLobbyCreated: ioFailure={ioFailure}, result={data.m_eResult}, lobby={data.m_ulSteamIDLobby}"
            );

            if (ioFailure || data.m_eResult != EResult.k_EResultOK)
            {
                _lobbyCreatedTcs.TrySetException(
                    new InvalidOperationException($"CreateLobby failed: ioFailure={ioFailure}, result={data.m_eResult}")
                );
                return;
            }

            _lobbyCreatedTcs.TrySetResult(new CSteamID(data.m_ulSteamIDLobby));
        }

        private void OnLobbyEnter(LobbyEnter_t data)
        {
            Debug.Log(
                $"[Matchmaking] OnLobbyEnter: lobby={data.m_ulSteamIDLobby}, response={data.m_EChatRoomEnterResponse}"
            );

            bool ok = data.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            if (!ok)
            {
                _lobbyEnterTcs?.TrySetException(
                    new InvalidOperationException(
                        $"JoinLobby failed: EChatRoomEnterResponse={data.m_EChatRoomEnterResponse}"
                    )
                );
                return;
            }

            _lobbyEnterTcs?.TrySetResult(true);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t data)
        {
            if (!_currentLobby.IsValid() || data.m_ulSteamIDLobby != _currentLobby.m_SteamID)
                return;
            Debug.Log(
                $"[Matchmaking] OnLobbyChatUpdate: lobby={data.m_ulSteamIDLobby}, userChanged={data.m_ulSteamIDUserChanged}, makingChange={data.m_ulSteamIDMakingChange}, stateChange={data.m_rgfChatMemberStateChange}"
            );
        }

        private void OnLobbyChatMessage(LobbyChatMsg_t data)
        {
            if (!_currentLobby.IsValid() || data.m_ulSteamIDLobby != _currentLobby.m_SteamID)
                return;

            CSteamID user;
            EChatEntryType type;
            byte[] buffer = new byte[1024];

            int len = SteamMatchmaking.GetLobbyChatEntry(
                new CSteamID(data.m_ulSteamIDLobby),
                (int)data.m_iChatID,
                out user,
                buffer,
                buffer.Length,
                out type
            );

            if (len <= 0)
                return;

            string text = System.Text.Encoding.UTF8.GetString(buffer, 0, len).TrimEnd('\0');
            Debug.Log($"[Matchmaking] OnLobbyChatMessage: from={user.m_SteamID}, type={type}, text='{text}'");

            if (text == BACK_MSG)
            {
                Debug.Log($"[Matchmaking] Received BACK from={user.m_SteamID}, me={SteamUser.GetSteamID()}");
                OnBackRequested?.Invoke();
                return;
            }

            var players = new List<CSteamID>();
            bool startPresent = TryParseStartMessage(text, players);

            if (startPresent)
            {
                CSteamID host = SteamMatchmaking.GetLobbyOwner(_currentLobby);
                Debug.Log(
                    $"[Matchmaking] Received START. host={host.m_SteamID}, me={SteamUser.GetSteamID()} players={string.Join(", ", players.Select(player => player.m_SteamID.ToString()))}"
                );

                OnStartWithPlayers?.Invoke(players);
            }
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t data)
        {
            Debug.Log($"[Matchmaking] OnGameLobbyJoinRequested: lobby={data.m_steamIDLobby.m_SteamID}");
            // SteamMatchmaking.JoinLobby(data.m_steamIDLobby);
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t data)
        {
            if (!_currentLobby.IsValid() || data.m_ulSteamIDLobby != _currentLobby.m_SteamID)
                return;
        }

        public List<CSteamID> PlayersInLobby()
        {
            if (!InLobby)
                return null;
            int count = SteamMatchmaking.GetNumLobbyMembers(_currentLobby);
            if (count <= 0)
                return null;
            var players = new List<CSteamID>(count);
            for (int i = 0; i < count; i++)
            {
                var m = SteamMatchmaking.GetLobbyMemberByIndex(_currentLobby, i);
                if (!m.IsValid())
                    continue;
                players.Add(m);
            }
            players.Sort((a, b) => a.m_SteamID.CompareTo(b.m_SteamID));
            return players;
        }

        private void SendLobbyStartMessage()
        {
            List<CSteamID> players = PlayersInLobby();

            string builtStartMsg = START_MSG + "|" + string.Join("|", players);

            Debug.Log(
                $"[Matchmaking] Sending START lobby chat message. lobby={_currentLobby.m_SteamID}, message={builtStartMsg}"
            );
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(builtStartMsg);
            SteamMatchmaking.SendLobbyChatMsg(_currentLobby, bytes, bytes.Length);
        }

        private bool TryParseStartMessage(string text, List<CSteamID> players)
        {
            // Parses a given string and outputs to a list of CSteamIDs if the message begins with __START.
            // The boolean returned reflects whether the message began with __START or not.
            string[] playerIDStrings = text.Split("|");
            if (playerIDStrings[0] == START_MSG)
            {
                for (int i = 1; i < playerIDStrings.Length; i++)
                {
                    try
                    {
                        players.Add(new CSteamID(ulong.Parse(playerIDStrings[i])));
                    }
                    catch (FormatException)
                    {
                        Debug.Log("SteamID " + playerIDStrings[i] + " could not be parsed as a ulong");
                    }
                    catch (OverflowException)
                    {
                        Debug.Log("SteamID " + playerIDStrings[i] + " was out of range for ulong");
                    }
                    catch (ArgumentException)
                    {
                        Debug.Log("SteamID " + playerIDStrings[i] + " could not be parsed as a ulong");
                    }
                }
                return true;
            }
            else
            {
                Debug.Log("Start message did not begin with __START.");
                return false;
            }
        }
    }
}
