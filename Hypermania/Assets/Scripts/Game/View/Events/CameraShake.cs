using UnityEngine;

namespace Game.View.Events
{
    public class CameraShake : MonoBehaviour
    {
        private Params _params;
        private AnimationCurve _moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private Vector3 _direction;
        private Vector3 _previousWaypoint;
        private Vector3 _currentWaypoint;
        private int _bounceIndex;
        private float _t;
        public bool IsFinished { get; private set; }

        public void Init(Params parameters, Vector3 initialDirection)
        {
            _params = parameters;
            _direction = initialDirection.normalized;
            _currentWaypoint = _direction * _params.PositionStrength;
        }

        public void Update()
        {
            if (_t < 1)
            {
                _t += Time.deltaTime * _params.Freq;
                if (_params.Freq == 0)
                    _t = 1;

                transform.localPosition = Vector3.Lerp(_previousWaypoint, _currentWaypoint, _moveCurve.Evaluate(_t));
            }
            else
            {
                _t = 0;
                transform.localPosition = _currentWaypoint;
                _previousWaypoint = _currentWaypoint;
                _bounceIndex++;
                if (_bounceIndex > _params.NumBounces)
                {
                    IsFinished = true;
                    return;
                }

                Vector3 rnd = Random.insideUnitSphere;
                _direction = -_direction + _params.Randomness * rnd.normalized;
                _direction = _direction.normalized;
                float decayValue = 1 - (float)_bounceIndex / _params.NumBounces;
                _currentWaypoint = decayValue * decayValue * _direction * _params.PositionStrength;
            }
        }

        [System.Serializable]
        public struct Params
        {
            public float PositionStrength;
            public float Freq;
            public int NumBounces;
            public float Randomness;
        }
    }
}
