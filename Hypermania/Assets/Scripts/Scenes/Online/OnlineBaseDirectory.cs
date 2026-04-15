using Netcode.P2P;
using Steamworks;
using UnityEngine;

namespace Scenes.Online
{
    /// <summary>
    /// Persistent scene container that owns the <see cref="SteamMatchmakingClient"/>
    /// across the online flow (Online lobby → CharacterSelect → LiveConnection).
    /// Loaded before <see cref="Scenes.SceneID.Online"/> and stays loaded while
    /// any online-context scene is active, so the lobby survives scene swaps
    /// like Online → CharacterSelect.
    /// </summary>
    [DisallowMultipleComponent]
    public class OnlineBaseDirectory : MonoBehaviour
    {
        public static SteamMatchmakingClient Matchmaking { get; private set; }

        public void Awake()
        {
            if (Matchmaking != null)
            {
                return;
            }
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("[OnlineBase] SteamManager not initialized; matchmaking unavailable.");
                return;
            }
            Matchmaking = new SteamMatchmakingClient();
        }

        public void OnDestroy()
        {
            if (Matchmaking != null)
            {
                Matchmaking.Leave();
                Matchmaking = null;
            }
        }
    }
}
