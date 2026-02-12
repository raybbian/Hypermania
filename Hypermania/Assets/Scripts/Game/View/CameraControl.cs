using System;
using System.Collections.Generic;
using Design;
using UnityEngine;
using Utils;

namespace Game.View
{
    public struct CameraShakeEvent
    {
        public Frame StartFrame;
        public float Intensity;
        public int Hash;
    }

    public class CameraControl : MonoBehaviour
    {
        private Camera _camera;
        private HashSet<CameraShakeEvent> _curShakes = new();
        private Vector3 _shakeOffset;

        [SerializeField]
        private int _shakeWindow = 6;

        [SerializeField]
        private float _shakeScale = 0.02f;

        [SerializeField]
        private float _cameraSpeed = 10;

        [SerializeField]
        private float _maxZoom = 2.5f;

        [SerializeField]
        private float _minZoom = 1.5f;

        [SerializeField]
        private GlobalConfig _config;

        // Additional area outside the arena bounds that the camera is allowed to see
        [SerializeField]
        private float _margin;

        // Additional area around the interest points that the camera must see
        [SerializeField]
        private float _padding;

        private List<Vector2> _interestPoints;

        void Start()
        {
            _camera = GetComponent<Camera>();
            _interestPoints = new List<Vector2>();
        }

        public void OnValidate()
        {
            if (_config == null)
            {
                throw new InvalidOperationException(
                    "Must set the config field on CameraControl because it reference the arena bounds"
                );
            }
        }

        public void UpdateCamera(List<Vector2> interestPoints, float zoom)
        {
            _interestPoints = interestPoints;
        }

        public void InvalidateAndApplyShake(Frame start, Frame end, HashSet<CameraShakeEvent> desired)
        {
            List<CameraShakeEvent> toRemove = new();

            foreach (var ev in _curShakes)
            {
                if (!desired.Contains(ev) && start <= ev.StartFrame && ev.StartFrame <= end)
                {
                    toRemove.Add(ev);
                }
            }

            foreach (var rem in toRemove)
            {
                _curShakes.Remove(rem);
            }

            foreach (var ev in desired)
            {
                if (!_curShakes.Contains(ev))
                {
                    _curShakes.Add(ev);
                }
            }
        }

        public void ApplyShake(Frame currentFrame)
        {
            float totalIntensity = 0f;

            foreach (var shake in _curShakes)
            {
                int frameDiff = currentFrame - shake.StartFrame;

                if (frameDiff >= 0 && frameDiff <= _shakeWindow)
                {
                    float t = 1f - (frameDiff / (float)_shakeWindow);
                    totalIntensity += shake.Intensity * t;
                }
            }

            if (totalIntensity > 0f)
            {
                _shakeOffset = GetDeterministicShake(currentFrame) * totalIntensity * _shakeScale;
            }
            else
            {
                _shakeOffset = Vector3.zero;
            }
        }

        private Vector3 GetDeterministicShake(Frame frame)
        {
            int seed = frame.No;
            float x = Mathf.Sin(seed * 12.9898f) * 43758.5453f;
            float y = Mathf.Sin(seed * 78.233f) * 12345.6789f;

            return new Vector3(Mathf.Repeat(x, 1f) * 2f - 1f, Mathf.Repeat(y, 1f) * 2f - 1f, 0f);
        }

        public void Update()
        {
            if (_interestPoints == null || _interestPoints.Count == 0)
            {
                return;
            }

            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;
            foreach (Vector2 point in _interestPoints)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            Vector2 padding = new Vector2(_padding, _padding);
            min -= padding;
            max += padding;

            float width = max.x - min.x;
            float wZoom = Mathf.Clamp(width / 2 / _camera.aspect, _minZoom, _maxZoom);

            float dt = Time.deltaTime;
            float k = _cameraSpeed;
            float a = 1f - Mathf.Exp(-k * dt);
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, wZoom, a);

            // adjust position with respect to zoom

            Vector3 p = transform.position;
            min.y = max.y - 2 * _camera.orthographicSize;
            Vector2 pos2 = Vector2.Lerp(new Vector2(p.x, p.y), (min + max) / 2, a);

            float halfHeight = _camera.orthographicSize;
            float halfWidth = _camera.orthographicSize * _camera.aspect;

            float minX = (float)-_config.WallsX + halfWidth - _margin;
            float maxX = (float)_config.WallsX - halfWidth + _margin;
            float minY = (float)_config.GroundY + halfHeight - _margin;
            float maxY = float.PositiveInfinity;

            // Clamping Camera View
            if (minX > maxX || minY > maxY)
            {
                throw new InvalidOperationException("bounds too small");
            }

            pos2.x = Mathf.Clamp(pos2.x, minX, maxX);
            pos2.y = Mathf.Clamp(pos2.y, minY, maxY);
            transform.position = new Vector3(pos2.x, pos2.y, p.z) + _shakeOffset;
        }
    }
}
