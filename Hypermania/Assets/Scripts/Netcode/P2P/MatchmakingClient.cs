using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using UnityEngine;
using Utils.Build;

namespace Netcode.P2P
{
    /// <summary>
    /// Thrown from <see cref="SteamMatchmakingClient.Join"/> when the lobby's
    /// <c>version</c> metadata does not match the local <see cref="BuildInfo.BuildId"/>.
    /// Indicates the two clients are on different commits (or at least one is
    /// dirty) and cannot safely share a session.
    /// </summary>
    public sealed class LobbyVersionMismatchException : Exception
    {
        public string Expected { get; }
        public string Actual { get; }

        public LobbyVersionMismatchException(string expected, string actual)
            : base($"Lobby build version mismatch. Expected '{expected}', lobby is '{actual}'.")
        {
            Expected = expected;
            Actual = actual;
        }
    }

    public sealed class SteamMatchmakingClient
    {
        public const string LobbyVersionKey = "version";
        public const string LobbyGameKey = "game";
        public const string LobbyGameValue = "Hypermania";

        private const string OnlinePresenceKey = "ol_present";
        private const string OnlinePresentValue = "1";

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

        /// <summary>
        /// Fires on every lobby member when a non-host asks the host to
        /// start the match from CharacterSelect (see
        /// <see cref="SendCharacterSelectLaunchRequest"/>). Only the host
        /// should act on this — non-hosts should ignore it.
        /// </summary>
        public Action OnCharacterSelectLaunchRequested;

        /// <summary>
        /// Fires on every lobby member (including the sending host) when
        /// the host authoritatively broadcasts the CharacterSelect launch
        /// signal (see <see cref="SendCharacterSelectLaunch"/>). The args
        /// carry the host's resolved final selections so both peers commit
        /// from a single source of truth (prevents divergence from any
        /// non-deterministic resolution such as the Random tile). Both
        /// clients should commit to the match on receipt, so they
        /// transition together.
        /// </summary>
        public Action<string[]> OnCharacterSelectLaunch;

        /// <summary>
        /// Fires on every lobby member when a non-host forwards a frame's
        /// input edges to the host (see <see cref="SendCharacterSelectInput"/>).
        /// Only the host acts on this; non-hosts ignore it. Args are six
        /// booleans in edge order (left, right, up, down, confirm, back).
        /// Input-forwarding is how the non-host requests state changes on
        /// the authoritative CharacterSelect — the host owns the state and
        /// applies collision logic before broadcasting the result.
        /// </summary>
        public Action<bool, bool, bool, bool, bool, bool> OnCharacterSelectInput;

        /// <summary>
        /// Fires on remaining lobby members when another member departs the
        /// current lobby — whether they explicitly left, disconnected
        /// (e.g. quit the process), were kicked, or were banned. The arg is
        /// the departed user's CSteamID. In-lobby screens (CharacterSelect,
        /// LiveConnection) should treat this as "the other player is gone,
        /// bail back to the Online lobby." Graceful Back sends go through
        /// <see cref="OnBackRequested"/> instead; this event covers the
        /// silent-departure cases where no chat message is sent.
        /// </summary>
        public Action<CSteamID> OnPeerLeft;

        private string _lastPresenceWrite;

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

            SteamMatchmaking.SetLobbyData(_currentLobby, LobbyVersionKey, BuildInfo.BuildId);
            SteamMatchmaking.SetLobbyData(_currentLobby, LobbyGameKey, LobbyGameValue);

            Debug.Log($"[Matchmaking] Lobby data set (version={BuildInfo.BuildId}).");

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

            // Lobby metadata is populated by the time LobbyEnter_t fires, so
            // we can read the version synchronously. If it doesn't match our
            // build id, leave and surface the mismatch to the caller before
            // committing _currentLobby — that keeps chat/data callbacks dormant
            // for a lobby we're about to drop.
            string local = BuildInfo.BuildId;
            string remote = SteamMatchmaking.GetLobbyData(lobbyId, LobbyVersionKey);
            if (remote != local)
            {
                Debug.LogWarning(
                    $"[Matchmaking] Lobby version mismatch on join: expected '{local}', lobby is '{remote}'. Leaving."
                );
                SteamMatchmaking.LeaveLobby(lobbyId);
                throw new LobbyVersionMismatchException(local, remote);
            }

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
            _lastPresenceWrite = null;
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
            SendChat(LobbyChatOpcode.Back);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Non-host asks the host to start the match from CharacterSelect.
        /// Fire-and-forget: the host validates its own view of "both
        /// confirmed" on receipt and responds with
        /// <see cref="SendCharacterSelectLaunch"/> if satisfied.
        /// </summary>
        public Task SendCharacterSelectLaunchRequest()
        {
            if (!_currentLobby.IsValid())
                return Task.CompletedTask;

            Debug.Log($"[Matchmaking] SendCharacterSelectLaunchRequest(): lobby={_currentLobby.m_SteamID}");
            SendChat(LobbyChatOpcode.CsLaunchReq);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Host authoritatively tells all clients (including itself via
        /// chat echo) to transition out of CharacterSelect into the match.
        /// <paramref name="args"/> carries the host's resolved final
        /// selections (Random tiles already rolled), so every peer commits
        /// from an identical snapshot. Both clients must act on this
        /// message to stay in lockstep.
        /// </summary>
        public Task SendCharacterSelectLaunch(params string[] args)
        {
            if (!_currentLobby.IsValid())
                return Task.CompletedTask;

            Debug.Log(
                $"[Matchmaking] SendCharacterSelectLaunch(): lobby={_currentLobby.m_SteamID}, args=[{string.Join(",", args ?? Array.Empty<string>())}]"
            );
            SendChat(LobbyChatOpcode.CsLaunch, args);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Non-host forwards a frame's input edges to the host. Encoded as
        /// six decimal "0"/"1" args in edge order (left, right, up, down,
        /// confirm, back). Only the host's
        /// <see cref="OnCharacterSelectInput"/> subscriber should act on the
        /// resulting event; non-hosts ignore it.
        /// </summary>
        public Task SendCharacterSelectInput(bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            if (!_currentLobby.IsValid())
                return Task.CompletedTask;

            string[] args = new[]
            {
                left ? "1" : "0",
                right ? "1" : "0",
                up ? "1" : "0",
                down ? "1" : "0",
                confirm ? "1" : "0",
                back ? "1" : "0",
            };
            SendChat(LobbyChatOpcode.CsInput, args);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Advertises whether the local client is actively showing the Online
        /// lobby screen. Writes to this client's per-member lobby data slot
        /// (<see cref="OnlinePresenceKey"/>) so peers can poll via
        /// <see cref="AllMembersInOnlineLobby"/> before relying on the Online
        /// lobby's chat subscribers being active on the far side. Deduped
        /// against the last write so idempotent calls from the per-frame
        /// Update loop don't spam Steam.
        /// </summary>
        public void SetSelfInOnlineLobby(bool present)
        {
            if (!SteamManager.Initialized || !InLobby)
                return;
            string value = present ? OnlinePresentValue : string.Empty;
            if (value == _lastPresenceWrite)
                return;
            _lastPresenceWrite = value;
            SteamMatchmaking.SetLobbyMemberData(_currentLobby, OnlinePresenceKey, value);
        }

        /// <summary>
        /// Host-side gate for starting a match: returns true only when every
        /// current lobby member has advertised themselves as present in the
        /// Online lobby scene via <see cref="SetSelfInOnlineLobby"/>. Guards
        /// against the host sending a START chat message before a peer
        /// (e.g. returning from BattleEnd) has remounted OnlineDirectory and
        /// subscribed to it.
        /// </summary>
        public bool AllMembersInOnlineLobby()
        {
            if (!InLobby)
                return false;
            int count = SteamMatchmaking.GetNumLobbyMembers(_currentLobby);
            if (count <= 0)
                return false;
            for (int i = 0; i < count; i++)
            {
                CSteamID m = SteamMatchmaking.GetLobbyMemberByIndex(_currentLobby, i);
                if (!m.IsValid())
                    return false;
                string value = SteamMatchmaking.GetLobbyMemberData(_currentLobby, m, OnlinePresenceKey);
                if (value != OnlinePresentValue)
                    return false;
            }
            return true;
        }

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

            const uint departureMask = (uint)(
                EChatMemberStateChange.k_EChatMemberStateChangeLeft
                | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                | EChatMemberStateChange.k_EChatMemberStateChangeKicked
                | EChatMemberStateChange.k_EChatMemberStateChangeBanned
            );
            if ((data.m_rgfChatMemberStateChange & departureMask) != 0)
            {
                CSteamID departed = new CSteamID(data.m_ulSteamIDUserChanged);
                OnPeerLeft?.Invoke(departed);
            }
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

            if (!LobbyChatProtocol.TryDecode(text, out LobbyChatOpcode op, out string[] args))
            {
                Debug.LogWarning(
                    $"[Matchmaking] Dropping malformed/unknown lobby chat message from={user.m_SteamID}: '{text}'"
                );
                return;
            }

            switch (op)
            {
                case LobbyChatOpcode.Back:
                    Debug.Log($"[Matchmaking] Received BACK from={user.m_SteamID}, me={SteamUser.GetSteamID()}");
                    OnBackRequested?.Invoke();
                    return;

                case LobbyChatOpcode.CsLaunchReq:
                    Debug.Log(
                        $"[Matchmaking] Received CS_LAUNCH_REQ from={user.m_SteamID}, me={SteamUser.GetSteamID()}"
                    );
                    OnCharacterSelectLaunchRequested?.Invoke();
                    return;

                case LobbyChatOpcode.CsLaunch:
                    Debug.Log(
                        $"[Matchmaking] Received CS_LAUNCH from={user.m_SteamID}, me={SteamUser.GetSteamID()}, args=[{string.Join(",", args)}]"
                    );
                    OnCharacterSelectLaunch?.Invoke(args);
                    return;

                case LobbyChatOpcode.CsInput:
                    if (args.Length != 6)
                    {
                        Debug.LogWarning(
                            $"[Matchmaking] Dropping CS_INPUT with unexpected arg count {args.Length}: [{string.Join(",", args)}]"
                        );
                        return;
                    }
                    OnCharacterSelectInput?.Invoke(
                        args[0] == "1",
                        args[1] == "1",
                        args[2] == "1",
                        args[3] == "1",
                        args[4] == "1",
                        args[5] == "1"
                    );
                    return;

                case LobbyChatOpcode.Start:
                    HandleStartMessage(user, args);
                    return;
            }
        }

        private void HandleStartMessage(CSteamID sender, string[] args)
        {
            var players = new List<CSteamID>(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                if (!ulong.TryParse(args[i], out ulong id))
                {
                    Debug.LogWarning(
                        $"[Matchmaking] Dropping START: player id arg {i} ('{args[i]}') not a valid ulong."
                    );
                    return;
                }
                players.Add(new CSteamID(id));
            }

            CSteamID host = SteamMatchmaking.GetLobbyOwner(_currentLobby);
            Debug.Log(
                $"[Matchmaking] Received START. host={host.m_SteamID}, me={SteamUser.GetSteamID()} players={string.Join(", ", players.Select(player => player.m_SteamID.ToString()))}"
            );

            OnStartWithPlayers?.Invoke(players);
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

        private void SendChat(LobbyChatOpcode op, params string[] args)
        {
            byte[] bytes = LobbyChatProtocol.Encode(op, args);
            SteamMatchmaking.SendLobbyChatMsg(_currentLobby, bytes, bytes.Length);
        }

        private void SendLobbyStartMessage()
        {
            List<CSteamID> players = PlayersInLobby();
            string[] args =
                players != null ? players.Select(p => p.m_SteamID.ToString()).ToArray() : Array.Empty<string>();

            Debug.Log(
                $"[Matchmaking] Sending START lobby chat message. lobby={_currentLobby.m_SteamID}, players={string.Join(",", args)}"
            );
            SendChat(LobbyChatOpcode.Start, args);
        }
    }
}
