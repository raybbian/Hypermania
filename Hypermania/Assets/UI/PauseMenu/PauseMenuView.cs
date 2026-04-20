using Game.Runners;
using Scenes;
using Scenes.Session;
using UnityEngine;

public class PauseMenuView : MonoBehaviour
{
    private LocalRunner _runner;

    private void Awake()
    {
        _runner = UnityEngine.Object.FindFirstObjectByType<LocalRunner>();
    }

    public void OnResume()
    {
        _runner.ResumeGame();
        gameObject.SetActive(false); // hide UI
    }

    public void OnMainMenu()
    {
        Time.timeScale = 1f;
        Destroy(gameObject);
        SceneLoader
            .Instance.LoadNewScene()
            .Load(SceneID.MenuBase, SceneDatabase.MENU_BASE)
            .Load(SceneID.MainMenu, SceneDatabase.MAIN_MENU)
            .Unload(SceneID.Battle)
            .Unload(SceneID.LiveConnection)
            .Unload(SceneID.Online)
            .WithOverlay()
            .Execute();
    }
}
