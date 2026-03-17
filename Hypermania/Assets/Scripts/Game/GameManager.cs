using System;
using System.Collections.Generic;
using Game.Runners;
using Game.Sim;
using Netcode.P2P;
using Netcode.Rollback;
using Steamworks;
using UnityEngine;

namespace Game
{
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        [SerializeField]
        public GameRunner Runner;

        public const int TPS = 60;
        public const int ROLLBACK_FRAMES = 8;

        public Action OnGameFinished;
        public bool FirstFinish;

        void OnValidate()
        {
            if (Runner == null)
            {
                Debug.LogError($"{nameof(GameManager)}: Runner component is required.", this);
            }
        }

        void OnDisable()
        {
            Runner.DeInit();
        }

        public void StartGame(
            List<(PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address)> players,
            P2PClient p2pClient,
            GameOptions overrideOptions
        )
        {
            if (Runner.Initialized)
                return;
            Runner.Init(players, p2pClient, overrideOptions);
            FirstFinish = true;
        }

        public void DeInit()
        {
            FirstFinish = false;
            if (Runner.Initialized)
            {
                Runner.DeInit();
            }
        }

        void Update()
        {
            bool finished = Runner.Poll(Time.deltaTime);
            if (finished && FirstFinish)
            {
                OnGameFinished?.Invoke();
                FirstFinish = false;
            }
        }
    }
}
