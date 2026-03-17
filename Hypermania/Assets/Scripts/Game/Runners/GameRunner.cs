using System;
using System.Collections.Generic;
using Design.Animation;
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

        public void OnDrawGizmos()
        {
            if (_curState == null || _view == null || _view.Fighters == null || _curState.Fighters == null)
                return;

            for (int i = 0; i < _curState.Fighters.Length; i++)
            {
                var fighterView = _view.Fighters[i];
                CharacterState anim = _curState.Fighters[i].State;
                int tick = _curState.SimFrame - _curState.Fighters[i].StateStart;
                FrameData frame = _options.Players[i].Character.GetFrameData(anim, tick);

                Transform t = fighterView.transform;

                foreach (var box in frame.Boxes)
                {
                    var kind = box.Props.Kind;
                    if (kind == HitboxKind.Hurtbox)
                        Gizmos.color = Color.blue;
                    else if (kind == HitboxKind.Hitbox)
                        Gizmos.color = Color.red;
                    else
                        continue;

                    Vector2 centerLocal = (Vector2)box.CenterLocal;
                    if (_curState.Fighters[i].FacingDir == FighterFacing.Left)
                    {
                        centerLocal.x *= -1;
                    }
                    Vector2 sizeLocal = (Vector2)box.SizeLocal;

                    Vector3 centerWorld = t.TransformPoint(new Vector3(centerLocal.x, centerLocal.y, 0f));
                    Vector3 halfWorldX = t.TransformVector(new Vector3(sizeLocal.x * 0.5f, 0f, 0f));
                    Vector3 halfWorldY = t.TransformVector(new Vector3(0f, sizeLocal.y * 0.5f, 0f));

                    Vector3 p0 = centerWorld - halfWorldX - halfWorldY;
                    Vector3 p1 = centerWorld + halfWorldX - halfWorldY;
                    Vector3 p2 = centerWorld + halfWorldX + halfWorldY;
                    Vector3 p3 = centerWorld - halfWorldX + halfWorldY;

                    Gizmos.DrawLine(p0, p1);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p3);
                    Gizmos.DrawLine(p3, p0);
                }
            }
        }
    }
}
