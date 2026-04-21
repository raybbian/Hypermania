using Scenes;
using Scenes.Session;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.Menus.MainMenu
{
    public enum GameConfig
    {
        Local,
        Training,
        Manual,
        Online,
    }

    [DisallowMultipleComponent]
    public class MainMenuDirectory : MonoBehaviour
    {
        [SerializeField]
        private Button _onlineButton;

        public void StartLocal()
        {
            SessionDirectory.Config = GameConfig.Local;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                .Unload(SceneID.MainMenu)
                .WithOverlay()
                .Execute();
        }

        public void StartOnline()
        {
            SessionDirectory.Config = GameConfig.Online;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                .Unload(SceneID.MainMenu)
                .WithOverlay()
                .Execute();
        }

        public void StartTraining()
        {
            SessionDirectory.Config = GameConfig.Training;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.InputSelect, SceneDatabase.INPUT_SELECT)
                .Unload(SceneID.MainMenu)
                .WithOverlay()
                .Execute();
        }

        public void Update()
        {
            _onlineButton.interactable = SteamManager.Initialized;
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
