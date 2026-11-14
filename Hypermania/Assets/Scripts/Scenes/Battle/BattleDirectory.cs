using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game;
using Game.Runners;
using Game.Sim;
using Game.View.Overlay;
using Netcode.Rollback;
using Scenes.Core;
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

        // The Steam status is only rewritten when one of these moves.
        private int _statusRounds;
        private int _statusMinutes;

        public void OnEnable()
        {
            _statusRounds = -1;
            _statusMinutes = -1;
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

        private void Update()
        {
            GameState state = _gameManager.Runner != null ? _gameManager.Runner.State : null;
            if (state == null)
                return;

            // The local player comes first, so friends read it from their side.
            int me = LocalPlayer();
            int mine = RoundsWon(state.Fighters[me]);
            int theirs = RoundsWon(state.Fighters[1 - me]);
            int rounds = mine * 16 + theirs;
            int minutes = state.RealFrame.No / (GameManager.TPS * 60);
            if (rounds == _statusRounds && minutes == _statusMinutes)
                return;
            _statusRounds = rounds;
            _statusMinutes = minutes;

            PlayerOptions[] players = SessionDirectory.Options.Players;
            string status = SteamPresence.ModeName(SessionDirectory.Config) + ": " + players[me].Character.Character;
            if (SessionDirectory.Config != GameConfig.Training)
                status += $" vs {players[1 - me].Character.Character}, {mine}-{theirs}";
            if (minutes > 0)
                status += $", {minutes} min";
            SteamPresence.Set(status);
        }

        private static int LocalPlayer()
        {
            if (SessionDirectory.Config != GameConfig.Online)
                return 0;
            int index = LiveConnectionDirectory._players.FindIndex(p => p.playerKind == PlayerKind.Local);
            return index < 0 ? 0 : index;
        }

        private static int RoundsWon(in FighterState fighter)
        {
            int won = 0;
            for (int i = 0; i < fighter.NumVictories; i++)
            {
                if (fighter.Victories[i] != VictoryKind.Empty)
                    won++;
            }
            return won;
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
