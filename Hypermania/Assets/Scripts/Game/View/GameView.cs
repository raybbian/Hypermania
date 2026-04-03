using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
using Game.View.Fighters;
using Game.View.Mania;
using Game.View.Overlay;
using Game.View.Projectiles;
using Steamworks;
using UnityEngine;
using Utils;

namespace Game.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Conductor))]
    public class GameView : MonoBehaviour
    {
        private Conductor _conductor;
        private Frame _rollbackStart;

        [Serializable]
        public struct PlayerParams
        {
            public BurstBarView BurstBarView;
            public HealthBarView HealthBarView;
            public ManiaView ManiaView;
            public ComboCountView ComboCountView;
            public VictoryMarkView VictoryMarkView;
        }

        [Serializable]
        public struct Params
        {
            public FighterIndicatorManager FighterIndicatorManager;
            public CameraControl CameraControl;
            public CameraShakeManager CameraShakeManager;
            public InfoOverlayView InfoOverlayView;
            public RoundTimerView RoundTimerView;
            public SfxManager SfxManager;
            public VfxManager VfxManager;
            public FrameDataOverlay FrameDataOverlay;
            public RoundCountdownView RoundCountdownView;
            public HypeBarView HypeBarView;
            public KOScreenView KOScreenView;
        }

        public FighterView[] Fighters => _fighters;
        private FighterView[] _fighters;
        private ProjectileView[] _projectileViews;

        private GameOptions _options;

        [SerializeField]
        private PlayerParams[] _playerParams;

        [SerializeField]
        private Params _params;

        [SerializeField]
        private float _conductorLerpSpeed;

        [SerializeField]
        private bool _disableCameraShake;

        public void Init(GameOptions options)
        {
            if (options.Players.Length != 2)
            {
                throw new InvalidOperationException("num characters in GameView must be 2");
            }

            _options = options;
            _conductor = GetComponent<Conductor>();
            if (_conductor == null)
            {
                throw new InvalidOperationException(
                    "Conductor was null. Did you forget to assign a conductor component to the GameView?"
                );
            }

            _fighters = new FighterView[options.Players.Length];

            for (int i = 0; i < options.Players.Length; i++)
            {
                CharacterConfig config = options.Players[i].Character;
                _fighters[i] = Instantiate(config.Prefab);
                _fighters[i].name = "Fighter View";
                _fighters[i].transform.SetParent(transform, true);
                _fighters[i].Init(config, options.Players[i].SkinIndex);

                _playerParams[i].ManiaView.Init();
                _playerParams[i].HealthBarView.SetMaxHealth((float)config.Health);
                _playerParams[i].BurstBarView.SetMaxBurst((float)config.BurstMax);
            }

            _projectileViews = new ProjectileView[GameState.MAX_PROJECTILES];

            _params.HypeBarView.SetMaxHype((float)options.Global.MaxHype);
            _conductor.Init(options);
            _rollbackStart = Frame.NullFrame;
        }

        public void Render(float deltaTime, in GameState state, GameOptions options, InfoOverlayDetails overlayDetails)
        {
            bool maniasEnabled = false;
            for (int i = 0; i < _options.Players.Length; i++)
            {
                _fighters[i].Render(state.SimFrame, state.Fighters[i]);
                _playerParams[i].ManiaView.Render(state.RealFrame, state.Manias[i]);

                maniasEnabled |= state.Manias[i].Enabled(state.RealFrame);
                if (state.Manias[i].Enabled(state.RealFrame))
                    _conductor.t = Mathf.Lerp(_conductor.t, i * 2 - 1, deltaTime * _conductorLerpSpeed);
            }

            _conductor.PublishTick(state.RealFrame, deltaTime);

            // Manage projectile views
            for (int i = 0; i < state.Projectiles.Length; i++)
            {
                if (state.Projectiles[i].Active)
                {
                    if (_projectileViews[i] == null)
                    {
                        int owner = state.Projectiles[i].Owner;
                        var characterConfig = _options.Players[owner].Character;
                        var projConfigs = characterConfig.Projectiles;
                        if (projConfigs != null && state.Projectiles[i].ConfigIndex < projConfigs.Count)
                        {
                            var prefab = projConfigs[state.Projectiles[i].ConfigIndex].Prefab;
                            if (prefab != null)
                            {
                                _projectileViews[i] = Instantiate(prefab);
                                _projectileViews[i].transform.SetParent(transform, true);
                                _projectileViews[i].Init(characterConfig, _options.Players[owner].SkinIndex);
                            }
                        }
                    }
                    _projectileViews[i]?.Render(state.SimFrame, state.Projectiles[i]);
                }
                else if (_projectileViews[i] != null)
                {
                    _projectileViews[i].DeInit();
                    Destroy(_projectileViews[i].gameObject);
                    _projectileViews[i] = null;
                }
            }

            List<Vector2> interestPoints = new List<Vector2>();
            for (int i = 0; i < _options.Players.Length; i++)
            {
                interestPoints.Add((Vector2)state.Fighters[i].Position);
                // ensure that fighter heads are included
                interestPoints.Add(
                    (Vector2)state.Fighters[i].Position
                        + new Vector2(0, (float)_options.Players[i].Character.CharacterHeight)
                );
                if (
                    (state.GameMode == GameMode.Mania || state.GameMode == GameMode.ManiaStart)
                    && state.Manias[i].Enabled(state.RealFrame)
                )
                {
                    interestPoints.Add(_playerParams[i].ManiaView.transform.position);
                }
            }

            for (int i = 0; i < _options.Players.Length; i++)
            {
                _playerParams[i].HealthBarView.SetHealth((int)state.Fighters[i].Health);
                _playerParams[i].BurstBarView.SetBurst((int)state.Fighters[i].Burst);
                _playerParams[i].VictoryMarkView.SetVictories(state.Fighters[i].Victories, (i == 0 ? -1 : 1));
            }

            _params.CameraControl.UpdateCamera(interestPoints);
            _params.FighterIndicatorManager.Track(state.Fighters);

            for (int i = 0; i < _options.Players.Length; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                _playerParams[i].ComboCountView.SetComboCount(combo);
                _playerParams[i ^ 1].HealthBarView.SetCombo(combo, (int)state.Fighters[i ^ 1].Health);
            }

            _params.InfoOverlayView.Render(overlayDetails);
            _params.RoundCountdownView.DisplayRoundCD(state.SimFrame, state.RoundStart, options);
            _params.RoundTimerView.DisplayRoundTimer(state.SimFrame, state.RoundEnd, state.GameMode, options);
            _params.KOScreenView.Render(state);

            if (_rollbackStart != Frame.NullFrame)
            {
                _params.SfxManager.InvalidateAndConsume(_rollbackStart, state.RealFrame);
                _params.CameraShakeManager.InvalidateAndConsume(_rollbackStart, state.RealFrame);
                _params.VfxManager.InvalidateAndConsume(_rollbackStart, state.RealFrame);
                _rollbackStart = Frame.NullFrame;
            }

            if (!maniasEnabled)
            {
                _conductor.t = Mathf.Lerp(
                    _conductor.t,
                    (float)(state.HypeMeter / options.Global.MaxHype),
                    deltaTime * _conductorLerpSpeed
                );
            }
            _params.HypeBarView.SetHype((float)state.HypeMeter);

            _params.FrameDataOverlay.gameObject.SetActive(options.InfoOptions.ShowFrameData);
            if (options.InfoOptions.ShowFrameData)
                _params.FrameDataOverlay.AddFrameData(state, options);
        }

        public void RollbackRender(in GameState state)
        {
            // gather all sfx from states in the current rollback process
            if (_rollbackStart == Frame.NullFrame)
            {
                _rollbackStart = state.RealFrame;
            }

            DoViewEvents(state);
        }

        private void DoViewEvents(in GameState state)
        {
            // TODO: refactor me, im thinking some listener pattern
            for (int i = 0; i < _options.Players.Length; i++)
            {
                _fighters[i].RollbackRender(state.RealFrame, state.Fighters[i], _params.VfxManager, _params.SfxManager);
                _playerParams[i]
                    .ManiaView.RollbackRender(state.RealFrame, state.Manias[i], _params.VfxManager, _params.SfxManager);
                if (state.Fighters[i].HitLastRealFrame)
                {
                    _params.SfxManager.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.MediumPunch },
                            StartFrame = state.RealFrame,
                            Hash = i,
                        }
                    );
                    if (!_disableCameraShake)
                    {
                        _params.CameraShakeManager.AddDesired(
                            new ViewEvent<CameraShakeEvent>
                            {
                                Event = new CameraShakeEvent
                                {
                                    Strength = 0.025f,
                                    Frequency = 25,
                                    NumBounces = 10,
                                    KnockbackVector = (Vector2)state.Fighters[i].Velocity,
                                },
                                StartFrame = state.RealFrame,
                                Hash = i,
                            }
                        );
                    }
                }
            }
        }

        public void DeInit()
        {
            for (int i = 0; i < _options.Players.Length; i++)
            {
                _fighters[i].DeInit();
                Destroy(_fighters[i].gameObject);
                _playerParams[i].ManiaView.DeInit();
            }

            if (_projectileViews != null)
            {
                for (int i = 0; i < _projectileViews.Length; i++)
                {
                    if (_projectileViews[i] != null)
                    {
                        _projectileViews[i].DeInit();
                        Destroy(_projectileViews[i].gameObject);
                        _projectileViews[i] = null;
                    }
                }
            }

            _fighters = null;
            _projectileViews = null;
            _options = null;
        }
    }
}
