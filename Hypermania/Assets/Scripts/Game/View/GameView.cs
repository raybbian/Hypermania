using System;
using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Events.Vfx;
using Game.View.Fighters;
using Game.View.Mania;
using Game.View.Overlay;
using Unity.VisualScripting;
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
        private CharacterConfig[] _characters;

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
        }

        public FighterView[] Fighters => _fighters;
        private FighterView[] _fighters;

        [SerializeField]
        private float _zoom = 1.6f;

        [SerializeField]
        private PlayerParams[] _playerParams;

        [SerializeField]
        private Params _params;

        [SerializeField]
        private bool _disableCameraShake;

        public void Init(GlobalConfig config, CharacterConfig[] characters)
        {
            if (characters.Length != 2)
            {
                throw new InvalidOperationException("num characters in GameView must be 2");
            }
            _conductor = GetComponent<Conductor>();
            if (_conductor == null)
            {
                throw new InvalidOperationException(
                    "Conductor was null. Did you forget to assign a conductor component to the GameView?"
                );
            }
            _fighters = new FighterView[characters.Length];

            _characters = characters;
            for (int i = 0; i < characters.Length; i++)
            {
                _fighters[i] = Instantiate(_characters[i].Prefab);
                _fighters[i].name = "Fighter View";
                _fighters[i].transform.SetParent(transform, true);
                _fighters[i].Init(characters[i]);

                _playerParams[i].ManiaView.Init();
                _playerParams[i].HealthBarView.SetMaxHealth((float)characters[i].Health);
                _playerParams[i].BurstBarView.SetMaxBurst((float)characters[i].BurstMax);
            }
            _conductor.Init(config.Audio);
            _rollbackStart = Frame.NullFrame;
        }

        public void Render(in GameState state, GlobalConfig config, InfoOverlayDetails overlayDetails)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].Render(state.SimFrame, state.Fighters[i]);
                _playerParams[i].ManiaView.Render(state.RealFrame, state.Manias[i]);
            }
            _conductor.RequestSlice(state.RealFrame);

            List<Vector2> interestPoints = new List<Vector2>();
            for (int i = 0; i < _characters.Length; i++)
            {
                interestPoints.Add((Vector2)state.Fighters[i].Position);
                // ensure that fighter heads are included
                interestPoints.Add(
                    (Vector2)state.Fighters[i].Position + new Vector2(0, (float)_characters[i].CharacterHeight)
                );
            }

            for (int i = 0; i < _characters.Length; i++)
            {
                _playerParams[i].HealthBarView.SetHealth((int)state.Fighters[i].Health);
                _playerParams[i].BurstBarView.SetBurst((int)state.Fighters[i].Burst);
                _playerParams[i].VictoryMarkView.SetVictories(state.Fighters[i].Victories, (i == 0 ? -1 : 1));
            }

            _params.CameraControl.UpdateCamera(interestPoints, _zoom);
            _params.FighterIndicatorManager.Track(state.Fighters);

            for (int i = 0; i < _characters.Length; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                _playerParams[i].ComboCountView.SetComboCount(combo);
                _playerParams[i ^ 1].HealthBarView.SetCombo(combo, (int)state.Fighters[i ^ 1].Health);
            }
            _params.InfoOverlayView.Render(overlayDetails);
            _params.RoundCountdownView.DisplayRoundCD(state.SimFrame, state.RoundStart, config);
            _params.RoundTimerView.DisplayRoundTimer(state.SimFrame, state.RoundEnd, state.GameMode, config);

            if (_rollbackStart != Frame.NullFrame)
            {
                _params.SfxManager.InvalidateAndConsume(_rollbackStart, state.SimFrame);
                _params.CameraShakeManager.InvalidateAndConsume(_rollbackStart, state.SimFrame);
                _params.VfxManager.InvalidateAndConsume(_rollbackStart, state.SimFrame);
                _rollbackStart = Frame.NullFrame;
            }
            _params.FrameDataOverlay.AddFrameData(state, config, _characters, config.Audio);
        }

        public void RollbackRender(in GameState state)
        {
            // gather all sfx from states in the current rollback process
            if (_rollbackStart == Frame.NullFrame)
            {
                _rollbackStart = state.SimFrame;
            }
            DoViewEvents(state);
        }

        private void DoViewEvents(in GameState state)
        {
            // TODO: refactor me, im thinking some listener pattern
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].RollbackRender(state.SimFrame, state.Fighters[i], _params.VfxManager, _params.SfxManager);
                if (state.Fighters[i].State == CharacterState.Hit && state.SimFrame == state.Fighters[i].StateStart)
                {
                    _params.SfxManager.AddDesired(
                        new ViewEvent<SfxEvent>
                        {
                            Event = new SfxEvent { Kind = SfxKind.MediumPunch },
                            StartFrame = state.SimFrame,
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
                                StartFrame = state.SimFrame,
                                Hash = i,
                            }
                        );
                    }
                }
            }
        }

        public void DeInit()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].DeInit();
                Destroy(_fighters[i].gameObject);
                _playerParams[i].ManiaView.DeInit();
            }
            _fighters = null;
            _characters = null;
        }
    }
}
