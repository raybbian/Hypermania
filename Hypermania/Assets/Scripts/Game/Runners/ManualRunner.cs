using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runners
{
    public class ManualRunner : SingleplayerRunner
    {
        [SerializeField]
        private Key _advanceKey;

        [SerializeField]
        private float _holdS;

        private float _curHoldS = 0;

        public override void Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }
            _inputBuffer.Saturate();
            if (Keyboard.current[Key.RightArrow].wasPressedThisFrame)
            {
                GameLoop();
                _inputBuffer.Clear();
            }
            if (Keyboard.current[Key.RightArrow].isPressed)
            {
                _curHoldS += deltaTime;
                if (_curHoldS >= _holdS)
                {
                    GameLoop();
                    _inputBuffer.Clear();
                }
            }
            else
            {
                _curHoldS = 0;
            }
        }
    }
}
