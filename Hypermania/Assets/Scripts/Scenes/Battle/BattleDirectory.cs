using Game;
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

        public void Start()
        {
            switch (SessionDirectory.Config)
            {
                case GameConfig.Local:
                case GameConfig.Training:
                    _gameManager.StartLocalGame();
                    break;
                case GameConfig.Online:
                    _gameManager.StartOnlineGame();
                    break;
            }
        }
    }
}
