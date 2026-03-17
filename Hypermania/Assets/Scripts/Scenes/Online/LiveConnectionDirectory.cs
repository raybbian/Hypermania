using System;
using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using Netcode.P2P;
using Netcode.Rollback;
using Scenes.Session;
using Steamworks;
using UnityEngine;

namespace Scenes.Online
{
    [DisallowMultipleComponent]
    public class LiveConnectionDirectory : MonoBehaviour
    {
        public static P2PClient _p2pClient;
        public static List<(PlayerHandle handle, PlayerKind playerKind, SteamNetworkingIdentity netId)> _players =
            new();

        public void OnEnable()
        {
            // start connecting when scene loads
            if (!OnlineDirectory.InLobby)
                return;
            StartWithPlayers(OnlineDirectory.Players);
        }

        public void OnDisable()
        {
            if (_p2pClient != null)
            {
                _p2pClient.DisconnectAllPeers();
                _p2pClient.OnAllPeersConnected -= OnAllPeersConnected;
                _p2pClient.OnPeerDisconnected -= OnPeerDisconnected;
                _p2pClient.Dispose();
                _p2pClient = null;
            }
            _players.Clear();
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
            // go to battle, don't unload current
            SceneLoader.Instance.LoadNewScene().Load(SceneID.Battle, SceneDatabase.BATTLE).WithOverlay().Execute();
        }

        void OnPeerDisconnected(SteamNetworkingIdentity id)
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Unload(SceneID.BattleEnd)
                .Unload(SceneID.Battle)
                .Unload(SceneID.LiveConnection)
                .WithOverlay()
                .Execute();
            // switch back to lobby scene, should disable the p2p client automagically
        }
    }
}
