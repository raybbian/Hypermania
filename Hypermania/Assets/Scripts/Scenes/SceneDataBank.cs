using System.Collections.Generic;

namespace Scenes
{
    /// <summary>
    /// Scene identifiers stored here.
    /// <summary>
    public enum SceneID
    {
        Session,
        MenuBase,
        MainMenu,
        InputSelect,
        Battle,
        BattleEnd,
        Online,
        LiveConnection,
    }

    //Hard-typed bleh
    public static class SceneDatabase
    {
        public const string SESSION = "Session";
        public const string MENU_BASE = "MenuBase";
        public const string MAIN_MENU = "MainMenu";
        public const string INPUT_SELECT = "InputSelect";
        public const string BATTLE = "Hypermania";
        public const string ONLINE = "Online";
        public const string LIVE_CONNECTION = "LiveConnection";
        public const string BATTLE_END = "BattleEnd";

        public static readonly Dictionary<string, SceneID> NameToID = new()
        {
            { SESSION, SceneID.Session },
            { MENU_BASE, SceneID.MenuBase },
            { MAIN_MENU, SceneID.MainMenu },
            { INPUT_SELECT, SceneID.InputSelect },
            { BATTLE, SceneID.Battle },
            { ONLINE, SceneID.Online },
            { LIVE_CONNECTION, SceneID.LiveConnection },
            { BATTLE_END, SceneID.BattleEnd },
        };
    }
}
