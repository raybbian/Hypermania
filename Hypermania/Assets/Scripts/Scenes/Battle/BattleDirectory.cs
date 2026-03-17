using Game;
using Game.Runners;
using Game.Sim;
using Scenes.Menus.MainMenu;
using Scenes.Session;
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

        public void Start()
        {
            switch (SessionDirectory.Config)
            {
                case GameConfig.Local:
                case GameConfig.Training:
                    _gameManager.Runner = _localRunner;
                    _gameManager.StartLocalGame();
                    break;
                case GameConfig.Online:
                    _gameManager.Runner = _onlineRunner;
                    _gameManager.StartOnlineGame();
                    break;
            }
        }
    }
}
