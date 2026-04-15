using Netcode.P2P;
using Steamworks;
using UnityEngine;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// Applies inbound Steam member-data updates to the remote player's
    /// <see cref="PlayerSelectionState"/>. Input polling is not this
    /// controller's job — that's <see cref="LocalSelectionController"/>.
    /// </summary>
    public class RemoteSelectionController
    {
        private readonly CharacterSelectNetSync _sync;
        private readonly CSteamID _remoteId;
        private readonly PlayerSelectionState _target;

        public RemoteSelectionController(CharacterSelectNetSync sync, CSteamID remoteId, PlayerSelectionState target)
        {
            _sync = sync;
            _remoteId = remoteId;
            _target = target;
            _sync.OnMemberUpdate += OnMemberUpdate;

            // Seed from any payload that was already broadcast before we attached.
            string existing = _sync.Peek(_remoteId);
            if (!string.IsNullOrEmpty(existing))
            {
                ApplyPayload(existing);
            }
        }

        public void Dispose()
        {
            if (_sync != null)
            {
                _sync.OnMemberUpdate -= OnMemberUpdate;
            }
        }

        private void OnMemberUpdate(CSteamID member, string payload)
        {
            if (member != _remoteId)
                return;
            ApplyPayload(payload);
        }

        private void ApplyPayload(string payload)
        {
            if (!CharacterSelectPayload.TryParse(payload, out CharacterSelectPayload parsed))
            {
                Debug.LogWarning($"[CharacterSelect] Ignoring malformed remote payload: {payload}");
                return;
            }
            _target.ApplyPayload(parsed);
        }
    }
}
