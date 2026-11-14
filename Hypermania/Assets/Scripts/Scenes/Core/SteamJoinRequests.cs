using System;
using System.Collections;
using Scenes.Menus.MainMenu;
using Scenes.Session;
using Steamworks;
using UnityEngine;

namespace Scenes.Core
{
    // Hears Steam asking us to join a lobby: an accepted invite, Join Game
    // in the friends list, or the launch arguments when one of those
    // started the game. It lives for the whole run so it works from any
    // screen. The lobby screen does the actual joining.
    [DisallowMultipleComponent]
    public class SteamJoinRequests : MonoBehaviour
    {
        // What Steam puts ahead of the lobby id when it launches the game to join one.
        private const string LaunchArgument = "+connect_lobby";

        private static CSteamID _pending;

        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Coroutine _routing;

        // Hands over the lobby Steam asked for, once.
        public static bool TryTake(out CSteamID lobby)
        {
            lobby = _pending;
            _pending = default;
            return lobby.IsValid();
        }

        // For when the player backs out on the way to the lobby screen.
        public static void Drop()
        {
            _pending = default;
        }

        // Start and not Awake, so the SteamManager next to us is up first.
        private void Start()
        {
            if (!SteamManager.Initialized)
                return;

            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);

            if (TryReadLaunchLobby(out CSteamID lobby))
                Request(lobby);
        }

        private void OnDestroy()
        {
            _joinRequested?.Dispose();
            _pending = default;
        }

        private static bool TryReadLaunchLobby(out CSteamID lobby)
        {
            lobby = default;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == LaunchArgument && ulong.TryParse(args[i + 1], out ulong id))
                {
                    lobby = new CSteamID(id);
                    return lobby.IsValid();
                }
            }
            return false;
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t data)
        {
            Debug.Log($"[Steam] Asked to join lobby {data.m_steamIDLobby.m_SteamID}");
            Request(data.m_steamIDLobby);
        }

        private void Request(CSteamID lobby)
        {
            _pending = lobby;
            if (_routing == null)
                _routing = StartCoroutine(RouteToLobbyScreen());
        }

        private IEnumerator RouteToLobbyScreen()
        {
            // Let whatever is loading finish, so we know where the player really is.
            yield return null;
            SceneLoader loader = SceneLoader.Instance;
            while (loader.Busy)
                yield return null;
            _routing = null;

            // In an online session the lobby screen comes back around on its
            // own, and it picks the request up when it does.
            if (!_pending.IsValid() || loader.IsLoaded(SceneID.OnlineBase))
                yield break;

            // Otherwise drop what they're doing and head online. That goes by
            // way of input select, since online needs its own device setup.
            SessionDirectory.Config = GameConfig.Online;
            if (loader.IsLoaded(SceneID.InputSelect))
                yield break;

            SceneLoader.SceneLoadPlan plan = loader.LoadNewScene();
            if (!loader.IsLoaded(SceneID.MenuBase))
                plan.Load(SceneID.MenuBase, SceneDatabase.MENU_BASE);
            plan.Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                .Unload(SceneID.MainMenu)
                .Unload(SceneID.CharacterSelect)
                .Unload(SceneID.BattleEnd)
                .Unload(SceneID.Battle)
                .WithOverlay()
                .Execute();
        }
    }
}
