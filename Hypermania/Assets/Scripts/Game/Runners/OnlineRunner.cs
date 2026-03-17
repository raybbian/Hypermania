using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View.Overlay;
using Netcode.P2P;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using Steamworks;
using UnityEngine;
using Utils;

namespace Game.Runners
{
    public class OnlineRunner : GameRunner
    {
        private P2PSession<GameState, GameInput, SteamNetworkingIdentity> _session;

        private uint _waitRemaining;
        private PlayerHandle _myHandle;
        private PlayerHandle _remoteHandle;

        public override void Init(
            List<(PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address)> players,
            P2PClient client,
            GameOptions options
        )
        {
            base.Init(players, client, options);

            SessionBuilder<GameInput, SteamNetworkingIdentity> builder = new SessionBuilder<
                GameInput,
                SteamNetworkingIdentity
            >()
                .WithNumPlayers(players.Count)
                .WithMaxPredictionWindow(GameManager.ROLLBACK_FRAMES)
                .WithFps(GameManager.TPS);
            foreach ((PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address) in players)
            {
                if (playerKind == PlayerKind.Local)
                {
                    _myHandle = playerHandle;
                }
                else if (playerKind == PlayerKind.Remote)
                {
                    // assume only two people for now
                    _remoteHandle = playerHandle;
                }
                builder.AddPlayer(
                    new PlayerType<SteamNetworkingIdentity> { Kind = playerKind, Address = address },
                    playerHandle
                );
            }
            _session = builder.StartP2PSession<GameState>(client);
            _waitRemaining = 0;

            if (_myHandle.Id == -1 || _remoteHandle.Id == -1)
            {
                throw new InvalidOperationException("Players not found in multiplayer runner");
            }

            if (_options.LocalPlayers.Length != 1)
            {
                throw new InvalidOperationException("Multiplayer runner only supports one local player");
            }
        }

        public override void DeInit()
        {
            _waitRemaining = 0;
            _session = null;
            _myHandle = new PlayerHandle(-1);
            _remoteHandle = new PlayerHandle(-1);
            base.DeInit();
        }

        public override bool Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return false;
            }

            _inputBuffers[0].Clear();
            _inputBuffers[0].Saturate();

            _session.PollRemoteClients();

            foreach (RollbackEvent<GameInput, SteamNetworkingIdentity> ev in _session.DrainEvents())
            {
                Debug.Log($"[Game] Received {ev.Kind} event");
                switch (ev.Kind)
                {
                    case RollbackEventKind.WaitRecommendation:
                        var waitRec = ev.GetWaitRecommendation();
                        _waitRemaining = waitRec.SkipFrames;
                        break;
                }
            }

            // accumulate time and update frame
            float fpsDelta = 1.0f / GameManager.TPS;
            if (_session.FramesAhead > 0)
            {
                fpsDelta *= 1.1f;
            }

            _time += deltaTime;
            while (_time > fpsDelta)
            {
                _time -= fpsDelta;
                bool finished = GameLoop(fpsDelta);
                if (finished)
                    return true;
            }

            return false;
        }

        bool GameLoop(float deltaTime)
        {
            if (_session.CurrentState != SessionState.Running)
            {
                return false;
            }
            if (_session.ConfirmedFrame() != Frame.NullFrame && _session.ConfirmedState().GameMode == GameMode.End)
            {
                return true;
            }
            if (_waitRemaining > 0)
            {
                Debug.Log("[Game] Skipping frame due to wait recommendation");
                _waitRemaining--;
                return false;
            }

            _session.AddLocalInput(_myHandle, _inputBuffers[0].Poll());

            List<RollbackRequest<GameState, GameInput>> requests = _session.AdvanceFrame();
            foreach (RollbackRequest<GameState, GameInput> request in requests)
            {
                switch (request.Kind)
                {
                    case RollbackRequestKind.SaveGameStateReq:
                        var saveReq = request.GetSaveGameStateReq();
                        saveReq.Cell.Save(saveReq.Frame, _curState, _curState.Checksum());
                        break;
                    case RollbackRequestKind.LoadGameStateReq:
                        var loadReq = request.GetLoadGameStateReq();
                        loadReq.Cell.Load(out _curState);
                        _view.RollbackRender(_curState);
                        break;
                    case RollbackRequestKind.AdvanceFrameReq:
                        _curState.Advance(_options, request.GetAdvanceFrameRequest().Inputs);
                        _view.RollbackRender(_curState);
                        break;
                }
            }

            InfoOverlayDetails details = new InfoOverlayDetails
            {
                HasPing = true,
                Ping = _session.NetworkStats(_remoteHandle).Ping,
            };
            _view.Render(deltaTime, _curState, _options, details);
            return _session.ConfirmedFrame() != Frame.NullFrame && _session.ConfirmedState().GameMode == GameMode.End;
        }
    }
}
