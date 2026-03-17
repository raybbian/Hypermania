using System;
using System.Collections.Generic;
using Game.Sim;
using Game.View.Overlay;
using Netcode.P2P;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using Steamworks;

namespace Game.Runners
{
    public class LocalRunner : GameRunner
    {
        protected SyncTestSession<GameState, GameInput> _session;

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
                if (playerKind != PlayerKind.Local)
                {
                    throw new InvalidOperationException("Cannot have remote/spectators in a local session");
                }
                builder.AddPlayer(
                    new PlayerType<SteamNetworkingIdentity> { Kind = playerKind, Address = address },
                    playerHandle
                );
            }
            _session = builder.StartSynctestSession<GameState>();
        }

        public override void DeInit()
        {
            _session = null;
            base.DeInit();
        }

        public override bool Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return false;
            }

            for (int i = 0; i < _inputBuffers.Length; i++)
            {
                _inputBuffers[i].Clear();
                _inputBuffers[i].Saturate();
            }

            float fpsDelta = 1.0f / GameManager.TPS;
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

        protected bool GameLoop(float deltaTime)
        {
            if (_session == null)
            {
                return false;
            }
            if (_curState.GameMode == GameMode.End)
            {
                return true;
            }

            for (int i = 0; i < 2; i++)
            {
                GameInput input = i < _inputBuffers.Length ? _inputBuffers[i].Poll() : GameInput.None;
                _session.AddLocalInput(new PlayerHandle(i), input);
            }

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
            InfoOverlayDetails details = new InfoOverlayDetails { HasPing = false, Ping = 0 };
            _view.Render(deltaTime, _curState, _options, details);
            return _curState.GameMode == GameMode.End;
        }
    }
}
