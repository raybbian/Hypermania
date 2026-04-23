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

        [SerializeField]
        private GameRunner _manualRunner;

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
            _gameManager.OnGameDisconnected += OnGameDisconnected;
            switch (SessionDirectory.Config)
            {
                case GameConfig.Local:
                case GameConfig.Training:
                    _gameManager.Runner = _localRunner;
                    _gameManager.StartGame(LOCAL_DEFAULT, null, SessionDirectory.Options);
                    break;
                case GameConfig.Manual:
                    _gameManager.Runner = _manualRunner;
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
            _gameManager.DeInit();
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.BattleEnd, SceneDatabase.BATTLE_END)
                .Unload(SceneID.Battle)
                .WithOverlay()
                .Execute();
        }

        void OnGameDisconnected()
        {
            // If the local sim already reached End, the peer's disconnect is
            // just them having raced ahead through OnGameFinished and torn
            // down their session. Route to BattleEnd like a clean finish so
            // the slower client doesn't get yanked back to the Online lobby.
            if (_gameManager.Runner.MatchDecided)
            {
                OnGameFinished();
                return;
            }
            _gameManager.DeInit();
            // Route through LiveConnectionDirectory so the rollback-session
            // disconnect and the P2P-level disconnect (which often fire in the
            // same frame) converge on one scene transition back to the Online
            // lobby instead of queueing two.
            LiveConnectionDirectory.ReturnToLobby();
        }

        public void OnDisable()
        {
            _gameManager.OnGameFinished -= OnGameFinished;
            _gameManager.OnGameDisconnected -= OnGameDisconnected;
        }
    }
}
