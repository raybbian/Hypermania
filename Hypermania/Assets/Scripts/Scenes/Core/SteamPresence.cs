using Scenes.Menus.MainMenu;
using Steamworks;
using UnityEngine;

namespace Scenes.Core
{
    // What friends see under our name in Steam. Screens say what the player
    // is up to, and this passes it on whenever it changes.
    public static class SteamPresence
    {
        // Has to exist in the app's rich presence localization on Steamworks,
        // as "%status%". See steam_rich_presence.vdf next to steam_appid.txt.
        private const string DisplayToken = "#Status";

        private static string _status;
        private static CSteamID _group;
        private static int _groupSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _status = null;
            _group = default;
            _groupSize = 0;
        }

        // How each way of playing reads at the front of a status.
        public static string ModeName(GameConfig config)
        {
            switch (config)
            {
                case GameConfig.Online:
                    return "Online match";
                case GameConfig.Training:
                    return "Training";
                default:
                    return "Local match";
            }
        }

        public static void Set(string status)
        {
            if (status == _status || !SteamManager.IsInitialized)
                return;
            _status = status;
            SteamFriends.SetRichPresence("status", status);
            SteamFriends.SetRichPresence("steam_display", DisplayToken);
        }

        // Steam lists friends who share a group together. Ours is the lobby.
        public static void SetGroup(CSteamID lobby, int size)
        {
            if ((lobby == _group && size == _groupSize) || !SteamManager.IsInitialized)
                return;
            _group = lobby;
            _groupSize = size;
            bool grouped = lobby.IsValid();
            // An empty value removes the key.
            SteamFriends.SetRichPresence("steam_player_group", grouped ? lobby.m_SteamID.ToString() : "");
            SteamFriends.SetRichPresence("steam_player_group_size", grouped ? size.ToString() : "");
        }
    }
}
