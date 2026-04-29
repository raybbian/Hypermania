using Game;
using Game.Sim;
using Scenes.Menus.MainMenu;
using Scenes.Session;
using UnityEngine;

namespace Scenes.Menus.InputSelect
{
    [DisallowMultipleComponent]
    public class InputSelectDirectory : MonoBehaviour
    {
        [SerializeField]
        private DeviceManager _deviceManager;

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
            if (!_deviceManager.ValidAssignments(out _, out _))
            {
                return;
            }
            SessionDirectory.Options = BuildScaffoldOptions();
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.OnlineBase, SceneDatabase.ONLINE_BASE)
                .Load(SceneID.Online, SceneDatabase.ONLINE)
                .Unload(SceneID.InputSelect)
                .WithOverlay()
                .Execute();
        }

        private void StartLocal()
        {
            if (!_deviceManager.ValidAssignments(out _, out _))
            {
                return;
            }
            SessionDirectory.Options = BuildScaffoldOptions();
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.CharacterSelect, SceneDatabase.CHARACTER_SELECT)
                .Unload(SceneID.InputSelect)
                .WithOverlay()
                .Execute();
        }

        private GameOptions BuildScaffoldOptions()
        {
            bool training = SessionDirectory.Config == GameConfig.Training;
            return new GameOptions
            {
                Sim = new SimOptions
                {
                    Global = SessionDirectory.GlobalStats,
                    InfoOptions = new InfoOptions
                    {
                        ShowFrameData = training,
                        ShowBoxes = training,
                        VerifyComboPrediction = false,
                    },
                    AlwaysRhythmCancel = false,
                },
                Presentation = new PresentationOptions { Audio = SessionDirectory.AudioPresentation },
                Input = new InputOptions(),
            };
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
