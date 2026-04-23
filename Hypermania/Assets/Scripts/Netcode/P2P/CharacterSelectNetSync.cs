using System;
using Steamworks;
using UnityEngine;

namespace Netcode.P2P
{
    /// <summary>
    /// Steam lobby-data sync for the CharacterSelect screen. The host (lobby
    /// owner) writes the full selection state for both slots under
    /// <see cref="LobbyStateKey"/>; all clients receive <see cref="OnLobbyStateUpdate"/>
    /// when the payload changes. Only the lobby owner can write lobby data via
    /// <c>SteamMatchmaking.SetLobbyData</c>, so authority is enforced at the
    /// transport layer — non-hosts cannot spoof state.
    /// </summary>
    public sealed class CharacterSelectNetSync : IDisposable
    {
        public const string LobbyStateKey = "cs_state";

        private readonly SteamMatchmakingClient _client;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCb;
        private string _lastBroadcast;

        /// <summary>
        /// Fires when the host's <see cref="LobbyStateKey"/> payload changes.
        /// Arg is the raw payload string. Fires on the host as well as clients.
        /// </summary>
        public event Action<string> OnLobbyStateUpdate;

        public CharacterSelectNetSync(SteamMatchmakingClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _lobbyDataUpdateCb = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        }

        /// <summary>
        /// Host-only. Writes <paramref name="payload"/> to lobby data, deduped
        /// against the last broadcast so Steam isn't spammed with identical
        /// writes each frame. No-op for non-hosts (Steam will reject the write
        /// silently, so callers should still gate on their own host flag).
        /// </summary>
        public void BroadcastHostState(string payload)
        {
            if (!_client.InLobby)
                return;
            if (payload == _lastBroadcast)
                return;
            _lastBroadcast = payload;
            SteamMatchmaking.SetLobbyData(_client.CurrentLobby, LobbyStateKey, payload);
        }

        /// <summary>
        /// Returns the host's currently-published state payload, or the empty
        /// string if nothing is set. Used on enter to seed local state with
        /// whatever the host had already broadcast before we subscribed.
        /// </summary>
        public string PeekHostState()
        {
            if (!_client.InLobby)
                return string.Empty;
            return SteamMatchmaking.GetLobbyData(_client.CurrentLobby, LobbyStateKey) ?? string.Empty;
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t data)
        {
            if (!_client.InLobby || data.m_ulSteamIDLobby != _client.CurrentLobby.m_SteamID)
                return;

            // Lobby-wide data updates report member == lobby. Per-member data
            // updates (unused in the new flow) have member != lobby; ignore.
            if (data.m_ulSteamIDMember != data.m_ulSteamIDLobby)
                return;

            string payload = SteamMatchmaking.GetLobbyData(_client.CurrentLobby, LobbyStateKey);
            if (string.IsNullOrEmpty(payload))
                return;

            OnLobbyStateUpdate?.Invoke(payload);
        }

        public void Dispose()
        {
            // Host blanks the state key on back-and-stay so a re-entry doesn't
            // flash stale state at the peer for a frame. Steam auto-clears
            // lobby data when the owner leaves the lobby, so disconnect/quit
            // paths don't need this — only the Back-and-stay case does.
            // Non-hosts cannot write lobby data, so the blank is a no-op for
            // them (SetLobbyData returns false silently).
            if (_client != null && _client.InLobby)
            {
                if (SteamMatchmaking.GetLobbyOwner(_client.CurrentLobby) == SteamUser.GetSteamID())
                {
                    SteamMatchmaking.SetLobbyData(_client.CurrentLobby, LobbyStateKey, string.Empty);
                }
            }
            if (_lobbyDataUpdateCb != null)
            {
                _lobbyDataUpdateCb.Dispose();
                _lobbyDataUpdateCb = null;
            }
            OnLobbyStateUpdate = null;
        }
    }
}
