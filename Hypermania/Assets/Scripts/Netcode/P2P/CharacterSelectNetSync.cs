using System;
using Steamworks;
using UnityEngine;

namespace Netcode.P2P
{
    /// <summary>
    /// Steam lobby member-data sync for the CharacterSelect screen. Writes the
    /// local player's selection slice under the member-data key
    /// <see cref="CSKey"/> and raises <see cref="OnMemberUpdate"/> when any
    /// other lobby member's slice changes.
    /// </summary>
    public sealed class CharacterSelectNetSync : IDisposable
    {
        public const string CSKey = "cs";

        private readonly SteamMatchmakingClient _client;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCb;
        private string _lastBroadcast;

        /// <summary>
        /// Fires on inbound member-data updates for members other than the
        /// local user. Arguments: (memberSteamId, rawPayload).
        /// </summary>
        public event Action<CSteamID, string> OnMemberUpdate;

        public CharacterSelectNetSync(SteamMatchmakingClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _lobbyDataUpdateCb = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        }

        /// <summary>
        /// Writes <paramref name="payload"/> to our lobby member data, deduping
        /// against the last broadcast so Steam isn't spammed with identical
        /// writes each frame.
        /// </summary>
        public void Broadcast(string payload)
        {
            if (!_client.InLobby)
                return;
            if (payload == _lastBroadcast)
                return;
            _lastBroadcast = payload;
            SteamMatchmaking.SetLobbyMemberData(_client.CurrentLobby, CSKey, payload);
        }

        /// <summary>
        /// Returns the current member data for <paramref name="member"/>, or
        /// the empty string if nothing is set. Useful on enter to seed remote
        /// state with whatever the peer has already broadcast.
        /// </summary>
        public string Peek(CSteamID member)
        {
            if (!_client.InLobby)
                return string.Empty;
            return SteamMatchmaking.GetLobbyMemberData(_client.CurrentLobby, member, CSKey) ?? string.Empty;
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t data)
        {
            if (!_client.InLobby || data.m_ulSteamIDLobby != _client.CurrentLobby.m_SteamID)
                return;

            // Lobby-wide data updates report member == lobby; skip those.
            if (data.m_ulSteamIDMember == data.m_ulSteamIDLobby)
                return;

            CSteamID member = new CSteamID(data.m_ulSteamIDMember);
            if (member == SteamUser.GetSteamID())
                return;

            string payload = SteamMatchmaking.GetLobbyMemberData(_client.CurrentLobby, member, CSKey);
            if (string.IsNullOrEmpty(payload))
                return;

            OnMemberUpdate?.Invoke(member, payload);
        }

        public void Dispose()
        {
            // Wipe our own "cs" slot so the next visit to CharacterSelect
            // (if we stay in the lobby and come back) doesn't show our
            // previous selection to the peer for a frame. Steam auto-evicts
            // member data when a user leaves the lobby, so disconnect/quit
            // paths don't need this — only the Back-and-stay case does.
            if (_client != null && _client.InLobby)
            {
                SteamMatchmaking.SetLobbyMemberData(_client.CurrentLobby, CSKey, string.Empty);
            }
            if (_lobbyDataUpdateCb != null)
            {
                _lobbyDataUpdateCb.Dispose();
                _lobbyDataUpdateCb = null;
            }
            OnMemberUpdate = null;
        }
    }
}
