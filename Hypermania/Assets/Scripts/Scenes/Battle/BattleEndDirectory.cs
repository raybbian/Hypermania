using UnityEngine;

namespace Scenes.Battle
{
    public class BattleEndDirectory : MonoBehaviour
    {
        public void Restart()
        {
            // unload the end screen and reset the battle scenne
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.Battle, SceneDatabase.BATTLE)
                .Unload(SceneID.BattleEnd)
                .WithOverlay()
                .Execute();
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
