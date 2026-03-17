using Scenes.Menus.MainMenu;
using Scenes.Session;
using UnityEngine;

namespace Scenes.Battle
{
    public class BattleEndDirectory : MonoBehaviour
    {
        public void Restart()
        {
            switch (SessionDirectory.Config)
            {
                case GameConfig.Local:
                case GameConfig.Training:
                    // unload the end screen and reset the battle scenne
                    SceneLoader
                        .Instance.LoadNewScene()
                        .Load(SceneID.Battle, SceneDatabase.BATTLE)
                        .Unload(SceneID.BattleEnd)
                        .WithOverlay()
                        .Execute();
                    break;
                case GameConfig.Online:
                    // go back to online
                    SceneLoader
                        .Instance.LoadNewScene()
                        .Unload(SceneID.BattleEnd)
                        .Unload(SceneID.LiveConnection)
                        .WithOverlay()
                        .Execute();
                    break;
            }
        }

        public void MainMenu()
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.MenuBase, SceneDatabase.MENU_BASE)
                .Load(SceneID.MainMenu, SceneDatabase.MAIN_MENU)
                .Unload(SceneID.BattleEnd)
                .Unload(SceneID.Battle)
                .Unload(SceneID.LiveConnection)
                .Unload(SceneID.Online)
                .WithOverlay()
                .Execute();
        }
    }
}
