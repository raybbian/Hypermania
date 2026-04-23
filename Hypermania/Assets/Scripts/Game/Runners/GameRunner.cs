using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using Game.View;
using Netcode.P2P;
using Netcode.Rollback;
using Steamworks;
using UnityEngine;

namespace Game.Runners
{
    public abstract class GameRunner : MonoBehaviour
    {
        [SerializeField]
        protected GameView _view;

        /// <summary>
        /// The current state of the runner. If you derive from this class, it must be initialized on Init();
        /// </summary>
        protected GameState _curState;
        protected GameOptions _options;

        protected InputBuffer[] _inputBuffers;
        protected bool _initialized;
        public bool Initialized => _initialized;
        public virtual bool Disconnected => false;

        // True once the local sim has reached the post-match End mode. Used by
        // BattleDirectory to distinguish "peer left after the match was already
        // decided locally" from a genuine mid-match disconnect.
        public bool MatchDecided => _curState?.GameMode == GameMode.End;

        protected float _time;

        public virtual void Init(
            List<(PlayerHandle playerHandle, PlayerKind playerKind, SteamNetworkingIdentity address)> players,
            P2PClient client,
            GameOptions options
        )
        {
            if (_initialized)
            {
                throw new InvalidOperationException("double initialization");
            }

            if (options == null)
            {
                throw new InvalidOperationException("No options");
            }
            if (players.Count != 2)
            {
                throw new InvalidOperationException("must get 2 players");
            }
            _options = options;
            _inputBuffers = new InputBuffer[_options.LocalPlayers.Length];
            for (int i = 0; i < _inputBuffers.Length; i++)
            {
                _inputBuffers[i] = new InputBuffer(
                    _options.LocalPlayers[i]?.InputDevice,
                    _options.LocalPlayers[i]?.Controls?.ControlScheme ?? ControlsConfig.DefaultBindings
                );
            }
            _curState = GameState.Create(_options);
            _view.Init(_options);
            _time = 0;
            _initialized = true;
        }

        public abstract bool Poll(float deltaTime);

        public virtual void DeInit()
        {
            _initialized = false;
            _inputBuffers = null;
            _time = 0;
            _view.DeInit();
            _curState = null;
        }
    }
}
