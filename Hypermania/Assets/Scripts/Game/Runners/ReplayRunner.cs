using System;
using Netcode.Rollback;
using UnityEngine;
using UnityEngine.InputSystem;
using Hypermania.Game;
using Hypermania.Game.Replay;
using Hypermania.Shared;

namespace Game.Runners
{
    // Plays back a recorded .hmrep against a caller-supplied GameOptions.
    // Pause toggle on Space, single-step (and hold-to-repeat) on RightArrow
    // while paused - same UX as ManualRunner.
    //
    // Bypasses LocalRunner entirely: no rollback session, no input buffers.
    // The replay's input streams drive Advance directly.
    public class ReplayRunner : GameRunner
    {
        [SerializeField]
        private float _holdS = 0.1f;

        private ReplayFile _replay;
        private (GameInput input, InputStatus status)[] _scratch;
        private int _frame;
        private bool _paused;
        private float _curHoldS;

        // Replay runners are spawned by the caller with the file already in
        // hand - we don't go through GameRunner's player-list init path.
        public void InitFromReplay(ReplayFile replay, GameOptions options)
        {
            if (_initialized)
                throw new InvalidOperationException("double initialization");
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _replay = replay;
            _options = options;
            _scratch = new (GameInput, InputStatus)[2];
            _curState = GameState.Create(_options.Sim);
            _view.Init(_options);
            _frame = 0;
            _paused = false;
            _curHoldS = 0f;
            _time = 0f;
            _initialized = true;
        }

        public override bool Poll(float deltaTime)
        {
            if (!_initialized)
                return false;

            if (Keyboard.current[Key.Space].wasPressedThisFrame)
                _paused = !_paused;

            if (_paused)
                return PollPaused(deltaTime);

            return PollPlaying(deltaTime);
        }

        private bool PollPlaying(float deltaTime)
        {
            float fpsDelta = 1.0f / GameManager.TPS;
            _time += deltaTime;
            while (_time > fpsDelta)
            {
                _time -= fpsDelta;
                if (AdvanceOne(fpsDelta))
                    return true;
            }
            _view.Render(deltaTime, _curState, _options, default);
            return false;
        }

        private bool PollPaused(float deltaTime)
        {
            _time = 0f;
            float fpsDelta = 1.0f / GameManager.TPS;

            if (Keyboard.current[Key.RightArrow].wasPressedThisFrame)
            {
                _curHoldS = 0f;
                if (AdvanceOne(fpsDelta))
                    return true;
            }
            else if (Keyboard.current[Key.RightArrow].isPressed)
            {
                _curHoldS += deltaTime;
                if (_curHoldS >= _holdS)
                {
                    _curHoldS = 0f;
                    if (AdvanceOne(fpsDelta))
                        return true;
                }
            }
            else
            {
                _curHoldS = 0f;
            }

            _view.Render(deltaTime, _curState, _options, default);
            return false;
        }

        private bool AdvanceOne(float fpsDelta)
        {
            if (_curState.GameMode == GameMode.End)
                return true;
            if (_frame >= _replay.P1Inputs.Length)
                return true;

            _scratch[0] = (new GameInput((InputFlags)_replay.P1Inputs[_frame]), InputStatus.Confirmed);
            _scratch[1] = (new GameInput((InputFlags)_replay.P2Inputs[_frame]), InputStatus.Confirmed);
            _curState.Advance(_options.Sim, _scratch);
            // Pump SFX/VFX events produced by the advance into the view, mirroring
            // the AdvanceFrameReq path in LocalRunner/OnlineRunner. Without this,
            // hits/blocks/super-readies wouldn't get their audio/vfx queued before
            // the next Render call drains them.
            _view.RollbackRender(_curState);
            _frame++;
            return _curState.GameMode == GameMode.End;
        }
    }
}
