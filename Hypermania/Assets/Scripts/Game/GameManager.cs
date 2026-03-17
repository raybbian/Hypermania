using System;
using System.Collections;
using System.Collections.Generic;
using Game.Runners;
using Game.Sim;
using Netcode.P2P;
using Netcode.Rollback;
using Scenes.Online;
using Scenes.Session;
using Steamworks;
using UnityEngine;

namespace Game
{
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        [SerializeField]
        public GameRunner Runner;
        private P2PClient _p2pClient;
        private List<(PlayerHandle handle, PlayerKind playerKind, SteamNetworkingIdentity netId)> _players;

        public const int TPS = 60;
        public const int ROLLBACK_FRAMES = 8;

        private bool _started;
        public bool Started => _started;

        void OnEnable()
        {
            _started = false;
            _p2pClient = null;
            _players = new List<(PlayerHandle handle, PlayerKind playerKind, SteamNetworkingIdentity netId)>();
        }

        void OnValidate()
        {
            if (Runner == null)
            {
                Debug.LogError($"{nameof(GameManager)}: Runner component is required.", this);
            }
        }

        void OnDisable()
        {
            _started = false;
            _p2pClient = null;
            _players = null;
        }

        #region Controls

        public void StartLocalGame()
        {
            if (_started)
                return;
            _players.Clear();
            _players.Add((new PlayerHandle(0), PlayerKind.Local, default));
            _players.Add((new PlayerHandle(1), PlayerKind.Local, default));
            StartRunner(SessionDirectory.Options);
        }

        public void DeInit()
        {
            if (!_started)
                return;
            _started = false;
            Runner.DeInit();
        }

        #endregion

        public void StartOnlineGame()
        {
            if (!OnlineDirectory.InLobby)
                return;
            StartWithPlayers(OnlineDirectory.Players);
        }

        private void StartWithPlayers(IReadOnlyList<CSteamID> players)
        {
            // start connecting to all peers
            List<SteamNetworkingIdentity> peerAddr = new List<SteamNetworkingIdentity>();
            foreach (CSteamID id in players)
            {
                bool isLocal = id == SteamUser.GetSteamID();
                SteamNetworkingIdentity netId = new SteamNetworkingIdentity();
                netId.SetSteamID(id);
                if (!isLocal)
                {
                    peerAddr.Add(netId);
                }
            }

            _p2pClient = new P2PClient(peerAddr);
            _p2pClient.OnAllPeersConnected += OnAllPeersConnected;
            _p2pClient.OnPeerDisconnected += OnPeerDisconnected;

            _players.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                bool isLocal = players[i] == SteamUser.GetSteamID();
                SteamNetworkingIdentity netId = new SteamNetworkingIdentity();
                netId.SetSteamID(players[i]);
                _players.Add((new PlayerHandle(i), isLocal ? PlayerKind.Local : PlayerKind.Remote, netId));
            }

            _p2pClient.ConnectToPeers();
        }

        void OnAllPeersConnected()
        {
            StartRunner(SessionDirectory.Options);
        }

        void StartRunner(GameOptions overrideOptions)
        {
            if (_players == null)
            {
                throw new InvalidOperationException("players should be initialized if peers are connected");
            }

            Runner.Init(_players, _p2pClient, overrideOptions);
            _started = true;
        }

        void OnPeerDisconnected(SteamNetworkingIdentity id)
        {
            _p2pClient.DisconnectAllPeers();
            _p2pClient.OnAllPeersConnected -= OnAllPeersConnected;
            _p2pClient.OnPeerDisconnected -= OnPeerDisconnected;
            _p2pClient.Dispose();
            _p2pClient = null;
            DeInit();
        }

        void Update()
        {
            Runner.Poll(Time.deltaTime);
        }
    }
}
