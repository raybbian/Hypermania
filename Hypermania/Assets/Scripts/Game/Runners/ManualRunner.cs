using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Runners
{
    public class ManualRunner : LocalRunner
    {
        [SerializeField]
        private Key _advanceKey;

        [SerializeField]
        private float _holdS;

        private float _curHoldS;

        public override bool Poll(float deltaTime)
        {
            if (!_initialized)
            {
                return false;
            }

            for (int i = 0; i < _inputBuffers.Length; i++)
            {
                _inputBuffers[i].Saturate();
            }

            if (Keyboard.current[Key.RightArrow].wasPressedThisFrame)
            {
                bool finished = GameLoop(deltaTime);
                for (int i = 0; i < _inputBuffers.Length; i++)
                {
                    _inputBuffers[i].Clear();
                }

                if (finished)
                    return true;
            }
            if (Keyboard.current[Key.RightArrow].isPressed)
            {
                _curHoldS += deltaTime;
                if (_curHoldS >= _holdS)
                {
                    bool finished = GameLoop(deltaTime);
                    for (int i = 0; i < _inputBuffers.Length; i++)
                    {
                        _inputBuffers[i].Clear();
                    }

                    if (finished)
                        return true;
                }
            }
            else
            {
                _curHoldS = 0;
            }

            return false;
        }
    }
}
