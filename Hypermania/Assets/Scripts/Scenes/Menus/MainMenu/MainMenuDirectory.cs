using System;
using Game;
using Game.Sim;
using Game.Sim.Replay;
using Game.View.Configs;
using Scenes;
using Scenes.Session;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Scenes.Menus.MainMenu
{
    public enum GameConfig
    {
        Local,
        Training,
        Manual,
        Online,
        Replay,
    }

    [DisallowMultipleComponent]
    public class MainMenuDirectory : MonoBehaviour
    {
        [SerializeField]
        private Button _onlineButton;

        [SerializeField]
        private PresentationRoster _presentationRoster;

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

        public void PlayReplay()
        {
#if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Choose replay (.hmrep)", "", "hmrep");
            if (string.IsNullOrEmpty(path))
                return;
#else
            Debug.LogError($"{nameof(PlayReplay)}: file picker is editor-only; wire a runtime picker for builds.");
            return;
#endif

            ReplayFile replay;
            try
            {
                replay = ReplayFile.Load(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(PlayReplay)}: failed to load '{path}': {e.Message}");
                return;
            }

            GameOptions options = BuildReplayOptions(replay);
            if (options == null)
                return;

            SessionDirectory.Config = GameConfig.Replay;
            SessionDirectory.Options = options;
            SessionDirectory.Replay = replay;

            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.Battle, SceneDatabase.BATTLE)
                .Unload(SceneID.MainMenu)
                .Unload(SceneID.MenuBase)
                .WithOverlay()
                .Execute();
        }

        private GameOptions BuildReplayOptions(ReplayFile replay)
        {
            if (_presentationRoster == null)
            {
                Debug.LogError($"{nameof(PlayReplay)}: PresentationRoster reference missing on MainMenuDirectory.");
                return null;
            }
            if (SessionDirectory.AudioPresentation == null || SessionDirectory.GlobalStats == null)
            {
                Debug.LogError(
                    $"{nameof(PlayReplay)}: SessionDirectory not initialized (GlobalStats/AudioPresentation null)."
                );
                return null;
            }

            // Non-stats sim fields (InfoOptions, AlwaysRhythmCancel, per-player
            // training toggles, ComboMode/ManiaDifficulty/SuperInputMode) ride
            // inside the replay. Stats (GlobalStats + CharacterStats) are not
            // serialized — we reattach the editor's live SOs and verify their
            // content hash matches what the recording saw.
            SimOptions sim = replay.DeserializeSimOptions();
            if (sim == null)
            {
                Debug.LogError(
                    $"{nameof(PlayReplay)}: replay does not embed SimOptions (legacy file). Re-record with current trainer/eval."
                );
                return null;
            }

            CharacterPresentation p1 = _presentationRoster.Get(replay.P1Character);
            CharacterPresentation p2 = _presentationRoster.Get(replay.P2Character);
            if (p1 == null || p1.Stats == null)
            {
                Debug.LogError($"{nameof(PlayReplay)}: roster missing P1 character {replay.P1Character}.");
                return null;
            }
            if (p2 == null || p2.Stats == null)
            {
                Debug.LogError($"{nameof(PlayReplay)}: roster missing P2 character {replay.P2Character}.");
                return null;
            }

            sim.Global = SessionDirectory.GlobalStats;
            sim.Players[0].Character = p1.Stats;
            sim.Players[1].Character = p2.Stats;

            ulong currentStatsHash = ReplayFile.ComputeStatsHash(sim.Global, p1.Stats, p2.Stats);
            if (currentStatsHash != replay.StatsHash)
            {
                Debug.LogWarning(
                    $"{nameof(PlayReplay)}: stats hash mismatch (replay 0x{replay.StatsHash:X16} vs editor 0x{currentStatsHash:X16}). "
                        + "Balance changed since record; playback may diverge."
                );
            }

            PresentationOptions presentation = new PresentationOptions
            {
                Players = new PlayerPresentation[]
                {
                    new PlayerPresentation { Character = p1, SkinIndex = ClampSkin(p1, replay.P1Skin) },
                    new PlayerPresentation { Character = p2, SkinIndex = ClampSkin(p2, replay.P2Skin) },
                },
                Audio = SessionDirectory.AudioPresentation,
                Stage = replay.Stage,
            };

            return new GameOptions
            {
                Sim = sim,
                Presentation = presentation,
                Input = new InputOptions(),
            };
        }

        private static int ClampSkin(CharacterPresentation cp, int skinIndex)
        {
            if (cp == null || cp.Skins == null || cp.Skins.Length == 0)
                return 0;
            if (skinIndex < 0 || skinIndex >= cp.Skins.Length)
                return 0;
            return skinIndex;
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
