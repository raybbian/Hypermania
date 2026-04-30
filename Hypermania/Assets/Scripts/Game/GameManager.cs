using System;
using System.Collections.Generic;
using Game.Runners;
using Game.Sim;
using Game.Sim.Replay;
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

        public const int TPS = SimConstants.TPS;
        public const int ROLLBACK_FRAMES = SimConstants.ROLLBACK_FRAMES;

        public Action OnGameFinished;
        public Action OnGameDisconnected;
        public bool FirstFinish;

        void OnValidate()
        {
            if (Runner == null)
            {
                Debug.LogError($"{nameof(GameManager)}: Runner component is required.", this);
            }
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

        // Bypass the standard Init path: ReplayRunner uses its own InitFromReplay
        // since it doesn't go through the rollback session or input-buffer setup.
        public void StartReplay(ReplayFile replay, GameOptions overrideOptions)
        {
            if (Runner.Initialized)
                return;
            if (!(Runner is ReplayRunner replayRunner))
            {
                Debug.LogError(
                    $"{nameof(GameManager)}.{nameof(StartReplay)}: assigned Runner ({Runner?.GetType().Name}) is not a {nameof(ReplayRunner)}."
                );
                return;
            }
            replayRunner.InitFromReplay(replay, overrideOptions);
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
            if (Runner.Disconnected && Runner.Initialized)
            {
                OnGameDisconnected?.Invoke();
                return;
            }
            if (finished && FirstFinish)
            {
                OnGameFinished?.Invoke();
                FirstFinish = false;
            }
        }
    }
}
