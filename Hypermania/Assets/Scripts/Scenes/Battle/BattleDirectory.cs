using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game;
using Game.Runners;
using Game.Sim;
using Netcode.Rollback;
using Scenes.Menus.MainMenu;
using Scenes.Online;
using Scenes.Session;
using Steamworks;
using UnityEngine;

namespace Scenes.Battle
{
    [DisallowMultipleComponent]
    public class BattleDirectory : MonoBehaviour
    {
        [SerializeField]
        private GameManager _gameManager;

        [SerializeField]
        private GameRunner _localRunner;

        [SerializeField]
        private GameRunner _onlineRunner;

        private static readonly List<(
            PlayerHandle handle,
            PlayerKind playerKind,
            SteamNetworkingIdentity address
        )> LOCAL_DEFAULT = new()
        {
            (new PlayerHandle(0), PlayerKind.Local, default),
            (new PlayerHandle(1), PlayerKind.Local, default),
        };

        public void OnEnable()
        {
            _gameManager.OnGameFinished += OnGameFinished;
            switch (SessionDirectory.Config)
            {
                case GameConfig.Local:
                case GameConfig.Training:
                    _gameManager.Runner = _localRunner;
                    _gameManager.StartGame(LOCAL_DEFAULT, null, SessionDirectory.Options);
                    break;
                case GameConfig.Online:
                    if (
                        LiveConnectionDirectory._players == null
                        || LiveConnectionDirectory._players.Count == 0
                        || LiveConnectionDirectory._p2pClient == null
                    )
                    {
                        Debug.LogError("Started online game without live connection directory");
                        return;
                    }
                    _gameManager.Runner = _onlineRunner;
                    _gameManager.StartGame(
                        LiveConnectionDirectory._players,
                        LiveConnectionDirectory._p2pClient,
                        SessionDirectory.Options
                    );
                    break;
            }
        }

        public void OnGameFinished()
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.BattleEnd, SceneDatabase.BATTLE_END)
                .Unload(SceneID.Battle)
                .WithOverlay()
                .Execute();
        }

        public void OnDisable()
        {
            _gameManager.OnGameFinished -= OnGameFinished;
            _gameManager.DeInit();
        }
    }
}
