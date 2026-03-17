using Design.Configs;
using Game.Sim;
using Scenes.Menus.MainMenu;
using Scenes.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Scenes.Menus.InputSelect
{
    [DisallowMultipleComponent]
    public class InputSelectDirectory : MonoBehaviour
    {
        [SerializeField]
        private DeviceManager _deviceManager;

        [SerializeField]
        private GlobalConfig _globalConfig;

        [SerializeField]
        private CharacterConfig _nytheaConfig;

        public void StartGame()
        {
            switch (SessionDirectory.Config)
            {
                case GameConfig.Training:
                case GameConfig.Local:
                    StartLocal();
                    break;
                case GameConfig.Online:
                    ContinueOnline();
                    break;
            }
        }

        private void ContinueOnline()
        {
            if (!_deviceManager.ValidAssignments(out var p1, out _))
            {
                return;
            }
            GameOptions options = new GameOptions();
            options.Global = _globalConfig;
            options.Players = new PlayerOptions[2];
            options.LocalPlayers = new LocalPlayerOptions[1];
            for (int i = 0; i < 2; i++)
            {
                options.Players[i] = new PlayerOptions
                {
                    SkinIndex = i,
                    Character = _nytheaConfig,
                    HealOnActionable = SessionDirectory.Config == GameConfig.Training,
                };
            }
            options.LocalPlayers[0] = new LocalPlayerOptions { InputDevice = p1 };
            options.InfoOptions = new InfoOptions { ShowFrameData = false };

            SessionDirectory.Options = options;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.Online, SceneDatabase.ONLINE)
                .Unload(SceneID.InputSelect)
                .WithOverlay()
                .Execute();
        }

        private void StartLocal()
        {
            if (!_deviceManager.ValidAssignments(out var p1, out var p2))
            {
                return;
            }
            GameOptions options = new GameOptions();
            options.Global = _globalConfig;
            options.Players = new PlayerOptions[2];
            options.LocalPlayers = new LocalPlayerOptions[2];
            for (int i = 0; i < 2; i++)
            {
                options.Players[i] = new PlayerOptions
                {
                    SkinIndex = i,
                    Character = _nytheaConfig,
                    HealOnActionable = SessionDirectory.Config == GameConfig.Training,
                };
                options.LocalPlayers[i] = new LocalPlayerOptions { InputDevice = i == 0 ? p1 : p2 };
            }
            options.InfoOptions = new InfoOptions { ShowFrameData = SessionDirectory.Config == GameConfig.Training };

            SessionDirectory.Options = options;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.Battle, SceneDatabase.BATTLE)
                .Unload(SceneID.InputSelect)
                .Unload(SceneID.MenuBase)
                .WithOverlay()
                .Execute();
        }

        public void Back()
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.MainMenu, SceneDatabase.MAIN_MENU)
                .Unload(SceneID.InputSelect)
                .WithOverlay()
                .Execute();
        }
    }
}
