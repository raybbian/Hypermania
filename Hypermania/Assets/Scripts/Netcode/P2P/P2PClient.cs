using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Netcode.Rollback;
using Netcode.Rollback.Network;
using Steamworks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Netcode.P2P
{
    public class P2PClient : INonBlockingSocket<SteamNetworkingIdentity>, IDisposable
    {
        public Action OnAllPeersConnected;
        public Action<SteamNetworkingIdentity> OnPeerDisconnected;

        private class P2PConnection
        {
            public SteamNetworkingIdentity PeerIdentity;
            public HSteamNetConnection Incoming;
            public HSteamNetConnection Outgoing;

            public P2PConnection(SteamNetworkingIdentity peerIdentity)
            {
                PeerIdentity = peerIdentity;
                Incoming = HSteamNetConnection.Invalid;
                Outgoing = HSteamNetConnection.Invalid;
            }

            public bool ShouldConnect => SteamUser.GetSteamID().m_SteamID < PeerIdentity.GetSteamID().m_SteamID;

            public HSteamNetConnection GetConnection()
            {
                if (ShouldConnect)
                {
                    return Outgoing;
                }
                else
                {
                    return Incoming;
                }
            }
        }

        private Dictionary<SteamNetworkingIdentity, P2PConnection> _connections;
        private HSteamListenSocket _listen;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connStatusCb;

        private bool _initialized;

        public P2PClient(List<SteamNetworkingIdentity> peerAddresses)
        {
            _connections = new Dictionary<SteamNetworkingIdentity, P2PConnection>();
            foreach (SteamNetworkingIdentity id in peerAddresses)
            {
                if (id.GetSteamID() == SteamUser.GetSteamID())
                {
                    throw new InvalidOperationException("cannot connect to yourself");
                }
                _connections.Add(id, new P2PConnection(id));
            }

            Debug.Log($"[Connecting] Setting listen socket options");
            SteamNetworkingConfigValue_t[] opts = new SteamNetworkingConfigValue_t[2];
            opts[0] = new SteamNetworkingConfigValue_t
            {
                m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Penalty,
                m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = 0 },
            };
            opts[1] = new SteamNetworkingConfigValue_t
            {
                m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_SDR_Penalty,
                m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = 200 },
            };

            Debug.Log($"[Connecting] Creating listen socket (P2P). virtPort=0");
            _listen = SteamNetworkingSockets.CreateListenSocketP2P(0, opts.Length, opts);
            if (_listen == HSteamListenSocket.Invalid)
                throw new InvalidOperationException("CreateListenSocketP2P returned invalid listen socket handle.");

            _connStatusCb = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnNetConnectionStatusChanged);
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized)
                return;
            _initialized = false;

            _connStatusCb?.Dispose();
            _connStatusCb = null;

            if (SteamManager.IsInitialized)
            {
                if (_connections != null)
                {
                    foreach (var kv in _connections)
                    {
                        var c = kv.Value;
                        if (c.Incoming != HSteamNetConnection.Invalid)
                        {
                            SteamNetworkingSockets.CloseConnection(c.Incoming, 0, "dispose", false);
                            c.Incoming = HSteamNetConnection.Invalid;
                        }
                        if (c.Outgoing != HSteamNetConnection.Invalid)
                        {
                            SteamNetworkingSockets.CloseConnection(c.Outgoing, 0, "dispose", false);
                            c.Outgoing = HSteamNetConnection.Invalid;
                        }
                    }
                }

                if (_listen != HSteamListenSocket.Invalid)
                {
                    SteamNetworkingSockets.CloseListenSocket(_listen);
                    _listen = HSteamListenSocket.Invalid;
                }
            }

            _connections?.Clear();
        }

        public void ConnectToPeers()
        {
            if (_connections.Count == 0)
            {
                OnAllPeersConnected?.Invoke();
                return;
            }

            foreach ((SteamNetworkingIdentity identity, P2PConnection connection) in _connections)
            {
                if (!connection.ShouldConnect)
                {
                    continue;
                }

                Debug.Log($"[Connecting] Setting connect options for peer {identity.GetSteamID()}");

                SteamNetworkingConfigValue_t[] opts = new SteamNetworkingConfigValue_t[2];
                opts[0] = new SteamNetworkingConfigValue_t
                {
                    m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Penalty,
                    m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = 0 },
                };
                opts[1] = new SteamNetworkingConfigValue_t
                {
                    m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_SDR_Penalty,
                    m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    m_val = new SteamNetworkingConfigValue_t.OptionValue { m_int32 = 200 },
                };

                Debug.Log($"[Connecting] Connecting to peer {identity.GetSteamID()} (P2P). virtPort=0");

                SteamNetworkingIdentity idDup = identity;
                connection.Outgoing = SteamNetworkingSockets.ConnectP2P(ref idDup, 0, opts.Length, opts);
                if (connection.Outgoing == HSteamNetConnection.Invalid)
                    throw new InvalidOperationException("ConnectP2P returned invalid net connection");
            }
        }

        public void DisconnectAllPeers(string reason = "disconnect_all")
        {
            if (!_initialized || _connections == null)
                return;

            if (!SteamManager.IsInitialized)
                return;

            foreach (var kv in _connections)
            {
                var c = kv.Value;

                if (c.Incoming != HSteamNetConnection.Invalid)
                {
                    SteamNetworkingSockets.CloseConnection(c.Incoming, 0, reason, false);
                    c.Incoming = HSteamNetConnection.Invalid;
                }
                if (c.Outgoing != HSteamNetConnection.Invalid)
                {
                    SteamNetworkingSockets.CloseConnection(c.Outgoing, 0, reason, false);
                    c.Outgoing = HSteamNetConnection.Invalid;
                }
            }
        }

        private void OnNetConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            Debug.Log(
                $"[Connecting] OnNetConnectionStatusChanged: conn={data.m_hConn.m_HSteamNetConnection}, old={data.m_eOldState}, new={data.m_info.m_eState}, listen={data.m_info.m_hListenSocket.m_HSteamListenSocket}, endReason={data.m_info.m_eEndReason}"
            );

            bool isIncoming = data.m_info.m_hListenSocket != HSteamListenSocket.Invalid;

            switch (data.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                {
                    if (isIncoming)
                    {
                        Debug.Log("[Connecting] Incoming connection in Connecting state. Accepting now.");
                        var r = SteamNetworkingSockets.AcceptConnection(data.m_hConn);
                        Debug.Log($"[Connecting] AcceptConnection result: {r}");
                    }
                    else
                    {
                        Debug.Log("[Connecting] Outbound connection in Connecting state.");
                    }
                    break;
                }

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                {
                    if (isIncoming)
                    {
                        _connections[data.m_info.m_identityRemote].Incoming = data.m_hConn;
                    }
                    else
                    {
                        // the connection should have been set when ConnectP2P was called
                        Assert.IsTrue(data.m_hConn == _connections[data.m_info.m_identityRemote].Outgoing);
                    }
                    Debug.Log($"[Connecting] Connected. Remote SteamID={data.m_info.m_identityRemote.GetSteamID()}");

                    if (_connections.Values.All((conn) => conn.GetConnection() != HSteamNetConnection.Invalid))
                    {
                        OnAllPeersConnected?.Invoke();
                    }
                    break;
                }

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                {
                    Debug.LogWarning(
                        $"[Connecting] Connection ended. state={data.m_info.m_eState}, endReason={data.m_info.m_eEndReason}, debug='{data.m_info.m_szEndDebug}'"
                    );
                    if (isIncoming)
                    {
                        SteamNetworkingSockets.CloseConnection(
                            _connections[data.m_info.m_identityRemote].Incoming,
                            0,
                            "closed",
                            false
                        );
                        _connections[data.m_info.m_identityRemote].Incoming = HSteamNetConnection.Invalid;
                    }
                    else
                    {
                        SteamNetworkingSockets.CloseConnection(
                            _connections[data.m_info.m_identityRemote].Outgoing,
                            0,
                            "closed",
                            false
                        );
                        _connections[data.m_info.m_identityRemote].Outgoing = HSteamNetConnection.Invalid;
                    }
                    OnPeerDisconnected?.Invoke(data.m_info.m_identityRemote);
                    break;
                }
            }
        }

        public void SendTo(in Message message, SteamNetworkingIdentity addr)
        {
            if (addr.IsInvalid())
                throw new ArgumentException("Invalid addr.", nameof(addr));

            HSteamNetConnection connection = _connections[addr].GetConnection();
            if (connection == HSteamNetConnection.Invalid)
                throw new InvalidOperationException("No active connection.");

            byte[] payload = new byte[message.SerdeSize()];
            message.Serialize(payload);
            unsafe
            {
                fixed (byte* pData = payload)
                {
                    var res = SteamNetworkingSockets.SendMessageToConnection(
                        connection,
                        (IntPtr)pData,
                        (uint)payload.Length,
                        Constants.k_nSteamNetworkingSend_UnreliableNoNagle,
                        out _
                    );

                    if (res != EResult.k_EResultOK)
                        throw new InvalidOperationException($"SendMessageToConnection failed: {res}");
                }
            }
        }

        public List<(SteamNetworkingIdentity addr, Message message)> ReceiveAllMessages()
        {
            const int batch = 32;
            IntPtr[] ptrs = new IntPtr[batch];

            var received = new List<(SteamNetworkingIdentity addr, Message message)>();

            foreach ((SteamNetworkingIdentity identity, P2PConnection connection) in _connections)
            {
                HSteamNetConnection conn = connection.GetConnection();
                if (conn == HSteamNetConnection.Invalid)
                {
                    continue;
                }

                while (true)
                {
                    int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, batch);
                    if (n <= 0)
                        break;
                    for (int i = 0; i < n; i++)
                    {
                        var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[i]);
                        try
                        {
                            byte[] data = new byte[msg.m_cbSize];
                            Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);

                            Message decoded = default;
                            decoded.Deserialize(data);

                            received.Add((identity, decoded));
                        }
                        finally
                        {
                            SteamNetworkingMessage_t.Release(ptrs[i]);
                        }
                    }
                }
            }
            return received;
        }
    }
}
