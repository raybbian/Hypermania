using System;
using Netcode.P2P;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Applies inbound host broadcasts to the local
    /// <see cref="CharacterSelectState"/>. Runs on every client (host and
    /// non-host) because the host's own <see cref="OnLobbyStateUpdate"/>
    /// callback fires against its own write too — but the apply is
    /// idempotent so the host sees no visible effect.
    ///
    /// Input polling is not this controller's job — that's
    /// <see cref="LocalSelectionController"/>.
    /// </summary>
    public class RemoteSelectionController
    {
        private readonly CharacterSelectNetSync _sync;
        private readonly CharacterSelectState _target;

        /// <summary>
        /// Fires when a broadcast payload fails to parse. The build-hash gate
        /// at lobby-join time makes this path effectively unreachable in
        /// production, so this signal is treated as a protocol-level error —
        /// the owning directory should abort the session (back to the Online
        /// lobby) rather than try to recover.
        /// </summary>
        public event Action OnProtocolError;

        public RemoteSelectionController(CharacterSelectNetSync sync, CharacterSelectState target)
        {
            _sync = sync;
            _target = target;
            _sync.OnLobbyStateUpdate += OnLobbyStateUpdate;

            // Seed from any payload the host already published before we
            // subscribed — guarantees fresh clients see the same state on
            // enter without waiting for the next tick.
            string existing = _sync.PeekHostState();
            if (!string.IsNullOrEmpty(existing))
            {
                ApplyPayload(existing);
            }
        }

        public void Dispose()
        {
            if (_sync != null)
            {
                _sync.OnLobbyStateUpdate -= OnLobbyStateUpdate;
            }
            OnProtocolError = null;
        }

        private void OnLobbyStateUpdate(string payload)
        {
            ApplyPayload(payload);
        }

        private void ApplyPayload(string payload)
        {
            if (!CharacterSelectBroadcastPayload.TryParse(payload, out CharacterSelectBroadcastPayload parsed))
            {
                Debug.LogError($"[CharacterSelect] Malformed host broadcast — treating as protocol error: {payload}");
                OnProtocolError?.Invoke();
                return;
            }
            _target.ApplyBroadcast(parsed);
        }
    }
}
