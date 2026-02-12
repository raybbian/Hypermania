using System;
using System.Collections.Generic;
using Design;
using Game.Sim;
using Game.View.Fighters;
using Game.View.Mania;
using UnityEngine;
using Utils;

namespace Game.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Conductor))]
    public class GameView : MonoBehaviour
    {
        private Conductor _conductor;
        private HashSet<SfxEvent> _sfx;
        private HashSet<CameraShakeEvent> _cameraShakes;
        private Frame _rollbackStart;

        public FighterView[] Fighters => _fighters;

        private FighterView[] _fighters;
        private CharacterConfig[] _characters;

        [SerializeField]
        private BurstBarView[] _burstBars;

        [SerializeField]
        private FighterIndicatorManager _fighterIndicatorManager;

        [SerializeField]
        private HealthBarView[] _healthbars;

        [SerializeField]
        private ManiaView[] _manias;

        [SerializeField]
        private float _zoom = 1.6f;

        [SerializeField]
        private CameraControl _cameraControl;

        [SerializeField]
        private ComboCountView[] _comboViews;

        [SerializeField]
        private InfoOverlayView _overlayView;

        [SerializeField]
        private RoundTimerView _roundTimerView;

        [SerializeField]
        private SfxManager _sfxManager;

        public void OnValidate()
        {
            if (_healthbars == null)
            {
                throw new InvalidOperationException("Healthbars should exist");
            }
            if (_healthbars.Length != 2)
            {
                throw new InvalidOperationException("Healthbar length should be 2");
            }
            if (_cameraControl == null)
            {
                throw new InvalidOperationException("Camera control must be assigned to the game view!");
            }
            for (int i = 0; i < 2; i++)
            {
                if (_healthbars[i] == null)
                {
                    throw new InvalidOperationException("Healthbars must be assigned to the game view!");
                }
            }
            if (_burstBars == null)
            {
                throw new InvalidOperationException("BurstBars should exist");
            }
            if (_burstBars.Length != 2)
            {
                throw new InvalidOperationException("BurstBars length should be 2");
            }
            for (int i = 0; i < 2; i++)
            {
                if (_burstBars[i] == null)
                {
                    throw new InvalidOperationException("BurstBars must be assigned to the game view!");
                }
            }
        }

        public void Init(CharacterConfig[] characters)
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

                _manias[i].Init();
                _healthbars[i].SetMaxHealth((float)characters[i].Health);
                _burstBars[i].SetMaxBurst((float)characters[i].BurstMax);
            }
            _conductor.Init();
            _sfx = new HashSet<SfxEvent>();
            _cameraShakes = new HashSet<CameraShakeEvent>();
            _rollbackStart = Frame.NullFrame;
        }

        public void Render(in GameState state, GlobalConfig config, InfoOverlayDetails overlayDetails)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].Render(state.Frame, state.Fighters[i]);
                _manias[i].Render(state.Frame, state.Manias[i]);
            }
            _conductor.RequestSlice(state.Frame);

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
                _healthbars[i].SetHealth((int)state.Fighters[i].Health);
                _burstBars[i].SetBurst((int)state.Fighters[i].Burst);
            }

            _cameraControl.UpdateCamera(interestPoints, _zoom);
            _fighterIndicatorManager.Track(state.Fighters);

            for (int i = 0; i < _characters.Length; i++)
            {
                int combo = state.Fighters[i ^ 1].ComboedCount;
                _comboViews[i].SetComboCount(combo);
            }
            _overlayView.Render(overlayDetails);
            _roundTimerView.DisplayRoundTimer(state.Frame, state.RoundEnd);

            if (_rollbackStart == Frame.NullFrame)
            {
                throw new InvalidOperationException("rollback start frame cannot be null");
            }
            _sfxManager.InvalidateAndPlay(_rollbackStart, state.Frame, _sfx);
            _cameraControl.InvalidateAndApplyShake(_rollbackStart, state.Frame, _cameraShakes);
            _cameraControl.ApplyShake(state.Frame);
            _rollbackStart = Frame.NullFrame;
            _sfx.Clear();
            _cameraShakes.Clear();
        }

        public void RollbackRender(in GameState state)
        {
            // gather all sfx from states in the current rollback process
            if (_rollbackStart == Frame.NullFrame)
            {
                _rollbackStart = state.Frame;
            }
            DoSfx(state);
            DoCameraShake(state);
        }

        private void DoSfx(in GameState state)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (state.Fighters[i].State == CharacterState.Hit && state.Frame == state.Fighters[i].StateStart)
                {
                    // TODO: check other fighter
                    _sfx.Add(
                        new SfxEvent
                        {
                            Kind = SfxKind.MediumPunch,
                            StartFrame = state.Frame,
                            Hash = i,
                        }
                    );
                }
            }
        }

        public void DeInit()
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                _fighters[i].DeInit();
                Destroy(_fighters[i].gameObject);
                _manias[i].DeInit();
            }
            _fighters = null;
            _characters = null;
            _sfx = null;
        }

        private void DoCameraShake(in GameState state)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (state.Fighters[i].State == CharacterState.Hit && state.Frame == state.Fighters[i].StateStart)
                {
                    _cameraShakes.Add(
                        new CameraShakeEvent
                        {
                            StartFrame = state.Frame,
                            Intensity = 1f,
                            Hash = i,
                        }
                    );
                }
            }
        }
    }
}
