using System;
using System.Collections.Generic;
using Game.View.Background;
using Game.View.Configs;
using Game.View.Events;
using Game.View.Events.Vfx;
using Game.View.Fighters;
using Game.View.Mania;
using Game.View.Overlay;
using Game.View.Projectiles;
using UnityEngine;
using Utils;
using Hypermania.Game;
using Hypermania.Game.Configs;
using Hypermania.Shared;
using SkinConfig = Game.View.Configs.SkinConfig;


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
            public AnimatedBarView BurstBarView;
            public SuperBarView SuperBarView;
            public HealthBarView HealthBarView;
            public ManiaView ManiaView;
            public ComboCountView ComboCountView;
            public VictoryMarkView VictoryMarkView;
            public SuperDisplayView SuperDisplayView;
            public UsernamePlateView UsernamePlateView;
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
            public IntroView IntroView;
            public HypeBarView HypeBarView;
            public KOScreenView KOScreenView;
            public BoxVisualizer BoxVisualizer;
            public OutlineGlowView OutlineGlowView;
            public StageBackgroundLoader BackgroundLoader;
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
            if (options.Sim.Players.Length != 2)
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

            int playerCount = options.Sim.Players.Length;
            _fighters = new FighterView[playerCount];
            GlobalStats global = options.Sim.Global;

            for (int i = 0; i < playerCount; i++)
            {
                CharacterPresentation pres = options.Presentation.Players[i].Character;
                int skinIndex = options.Presentation.Players[i].SkinIndex;
                CharacterStats stats = pres.Stats;
                _fighters[i] = Instantiate(pres.Prefab);
                _fighters[i].name = "Fighter View";
                _fighters[i].transform.SetParent(transform, true);
                _fighters[i].Init(pres, skinIndex);
                _fighters[i].SetOutlinePlayerIndex(i);

                _playerParams[i].ManiaView.Init(global.Audio, global.PreGameDelayTicks, pres.Skins[skinIndex]);
                _playerParams[i].HealthBarView.Init(pres, skinIndex);
                _playerParams[i].HealthBarView.SetOutlinePlayerIndex(i);
                _playerParams[i].HealthBarView.SetMaxHealth((float)stats.Health);
                _playerParams[i].BurstBarView.SetMaxValue((float)stats.BurstMax);
                _playerParams[i].SuperBarView.Init((float)global.SuperCost);
                _playerParams[i].SuperDisplayView.Init(pres, skinIndex, global.SuperPostDisplayHitstopTicks);
                if (_playerParams[i].UsernamePlateView != null)
                    _playerParams[i].UsernamePlateView.Init(options.Presentation.Players[i].Username);
            }

            _projectileViews = new ProjectileView[GameState.MAX_PROJECTILES];

            _params.CameraControl.Init(global);

            _params.HypeBarView.Init(
                (float)global.MaxHype,
                options.Presentation.Players[0].Character.Skins[options.Presentation.Players[0].SkinIndex],
                options.Presentation.Players[1].Character.Skins[options.Presentation.Players[1].SkinIndex]
            );
            if (_params.IntroView != null)
                _params.IntroView.Init(options);

            _conductor.Init(options);
            _conductor.SetFrame(Frame.FirstFrame);
            _rollbackStart = Frame.NullFrame;

            if (_params.BackgroundLoader != null)
                _params.BackgroundLoader.Init(options.Presentation.Stage);
        }

        public void Render(float deltaTime, in GameState state, GameOptions options, InfoOverlayDetails overlayDetails)
        {
            bool maniasEnabled = false;
            int playerCount = _options.Sim.Players.Length;
            GlobalStats global = options.Sim.Global;
            for (int i = 0; i < playerCount; i++)
            {
                _fighters[i].Render(state.SimFrame, state.Fighters[i]);
                _playerParams[i].ManiaView.Render(state.RealFrame, state.Manias[i]);

                maniasEnabled |= state.Manias[i].Enabled(state.RealFrame);
                if (state.Manias[i].Enabled(state.RealFrame))
                    _conductor.t = Mathf.Lerp(_conductor.t, i * 2 - 1, deltaTime * _conductorLerpSpeed);
            }

            _conductor.PublishTick(state.RealFrame, deltaTime);

            // Re-anchor audio position to RealFrame periodically to prevent
            // cumulative drift between the wall-clock-driven audio cursor
            // and the fixed-rate sim frame counter.
            if (state.RealFrame.No % 25 == 0)
                _conductor.SetFrame(state.RealFrame);

            // Manage projectile views
            for (int i = 0; i < state.Projectiles.Length; i++)
            {
                if (state.Projectiles[i].Active)
                {
                    int owner = state.Projectiles[i].Owner;
                    CharacterPresentation pres = _options.Presentation.Players[owner].Character;
                    var projPresentations = pres.Projectiles;
                    ProjectilePresentation projPres = null;
                    if (projPresentations != null && state.Projectiles[i].ConfigIndex < projPresentations.Count)
                        projPres = projPresentations[state.Projectiles[i].ConfigIndex];

                    if (_projectileViews[i] == null && projPres != null && projPres.Prefab != null)
                    {
                        _projectileViews[i] = Instantiate(projPres.Prefab);
                        _projectileViews[i].transform.SetParent(transform, true);
                        _projectileViews[i].Init(pres, _options.Presentation.Players[owner].SkinIndex);
                        _projectileViews[i].SetOutlinePlayerIndex(owner);
                    }
                    _projectileViews[i]?.Render(state.SimFrame, state.Projectiles[i], projPres?.Stats);
                }
                else if (_projectileViews[i] != null)
                {
                    _projectileViews[i].DeInit();
                    Destroy(_projectileViews[i].gameObject);
                    _projectileViews[i] = null;
                }
            }

            List<Vector2> interestPoints = new List<Vector2>();
            for (int i = 0; i < playerCount; i++)
            {
                interestPoints.Add((Vector2)state.Fighters[i].Position);
                // ensure that fighter heads are included
                interestPoints.Add(
                    (Vector2)state.Fighters[i].Position
                        + new Vector2(0, (float)_options.Sim.Players[i].Character.CharacterHeight)
                );
            }

            for (int i = 0; i < playerCount; i++)
            {
                _playerParams[i].HealthBarView.SetHealth((int)state.Fighters[i].Health);
                _playerParams[i].BurstBarView.SetValue((int)state.Fighters[i].Burst);
                _playerParams[i].SuperBarView.SetValue((float)state.Fighters[i].Super);
                _playerParams[i].VictoryMarkView.SetVictories(state.Fighters[i].Victories, (i == 0 ? -1 : 1));
            }

            Vector2? countdownFocus = null;
            if (state.GameMode == GameMode.Countdown)
            {
                int elapsed = state.SimFrame.No - state.RoundStart.No;
                var audio = global.Audio;
                int focusPlayer = -1;
                if (elapsed >= 0 && elapsed < audio.BeatsToFrame(2))
                    focusPlayer = 0;
                else if (elapsed >= 0 && elapsed < audio.BeatsToFrame(4))
                    focusPlayer = 1;

                if (focusPlayer >= 0)
                {
                    countdownFocus =
                        (Vector2)state.Fighters[focusPlayer].Position
                        + new Vector2(0, (float)_options.Sim.Players[focusPlayer].Character.CharacterHeight);
                }
            }
            _params.CameraControl.UpdateCamera(interestPoints, state.GameMode, countdownFocus);
            _params.FighterIndicatorManager.Track(state.Fighters);

            for (int i = 0; i < playerCount; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                _playerParams[i].ComboCountView.SetComboCount(combo);
                _playerParams[i ^ 1].HealthBarView.SetCombo(combo, (int)state.Fighters[i ^ 1].Health);
            }

            _params.InfoOverlayView.Render(overlayDetails);
            _params.IntroView.DisplayIntro(state.SimFrame, options);
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
                    (float)(-state.HypeMeter / global.MaxHype),
                    deltaTime * _conductorLerpSpeed
                );
            }
            _params.HypeBarView.SetHype((float)state.HypeMeter);

            _params.OutlineGlowView.Render(deltaTime, state, options);

            InfoOptions info = options.Sim.InfoOptions;
            bool showFrameData = info != null && info.ShowFrameData;
            bool showBoxes = info != null && info.ShowBoxes;
            _params.FrameDataOverlay.gameObject.SetActive(showFrameData);
            if (showFrameData)
                _params.FrameDataOverlay.AddFrameData(state, options.Sim);

            _params.BoxVisualizer.gameObject.SetActive(showBoxes);
            if (showBoxes)
                _params.BoxVisualizer.Render(state, options.Sim, _fighters);

            for (int i = 0; i < playerCount; i++)
            {
                if (_playerParams[i].SuperDisplayView != null)
                    _playerParams[i].SuperDisplayView.Render(state, state.Fighters[i], _params.SfxManager, i);
            }
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
            for (int i = 0; i < _options.Sim.Players.Length; i++)
            {
                _fighters[i]
                    .RollbackRender(
                        state.RealFrame,
                        state.Fighters[i],
                        _params.VfxManager,
                        _params.SfxManager,
                        _options.Sim.Global
                    );
                _playerParams[i]
                    .ManiaView.RollbackRender(state.RealFrame, state.Manias[i], _params.VfxManager, _params.SfxManager);
                if (state.Fighters[i].View.SuperTier1MaxedThisRealFrame)
                {
                    _params.SfxManager.AddDesired(
                        i == 0 ? SfxKind.SuperReady : SfxKind.OppSuperReady,
                        state.RealFrame,
                        hash: i
                    );
                }
                if (state.Fighters[i].View.SuperTier2MaxedThisRealFrame)
                {
                    _params.SfxManager.AddDesired(
                        i == 0 ? SfxKind.Super2Ready : SfxKind.OppSuper2Ready,
                        state.RealFrame,
                        hash: i
                    );
                }
                if (i == 0 && state.Fighters[i].View.GrabTechedThisRealFrame)
                {
                    Vector2 center = (_fighters[0].VisualCenter + _fighters[1].VisualCenter) * 0.5f;
                    _params.VfxManager.AddDesired(VfxKind.Tech, state.RealFrame, position: center);
                }
                if (state.Fighters[i].HitLastRealFrame)
                {
                    _params.SfxManager.AddDesired(SfxKind.MediumPunch, state.RealFrame, hash: i);
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
            for (int i = 0; i < _options.Sim.Players.Length; i++)
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
