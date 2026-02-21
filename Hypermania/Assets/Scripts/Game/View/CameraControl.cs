using System;
using System.Collections.Generic;
using Design;
using Game.View.Events;
using UnityEngine;
using Utils;

namespace Game.View
{
    /// <summary>
    /// Should be placed on a parent of the Camera
    /// </summary>
    [RequireComponent(typeof(CameraShakeManager))]
    public class CameraControl : MonoBehaviour
    {
        [Serializable]
        public struct Params
        {
            public float CameraSpeed;
            public float MaxZoom;
            public float MinZoom;
            public GlobalConfig Config;

            // Additional area outside the arena bounds that the camera is allowed to see
            public float Margin;

            // Additional area around the interest points that the camera must see
            public float Padding;
            public Camera Camera;
        }

        [SerializeField]
        private Params _params;
        private List<Vector2> _interestPoints;

        void Start()
        {
            _interestPoints = new List<Vector2>();
        }

        public void OnValidate()
        {
            if (_params.Config == null)
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
            Vector2 padding = new Vector2(_params.Padding, _params.Padding);
            min -= padding;
            max += padding;

            float width = max.x - min.x;
            float wZoom = Mathf.Clamp(width / 2 / _params.Camera.aspect, _params.MinZoom, _params.MaxZoom);

            float dt = Time.deltaTime;
            float k = _params.CameraSpeed;
            float a = 1f - Mathf.Exp(-k * dt);
            _params.Camera.orthographicSize = Mathf.Lerp(_params.Camera.orthographicSize, wZoom, a);

            // adjust position with respect to zoom

            Vector3 p = transform.position;
            min.y = max.y - 2 * _params.Camera.orthographicSize;
            Vector2 pos2 = Vector2.Lerp(new Vector2(p.x, p.y), (min + max) / 2, a);
            float halfHeight = _params.Camera.orthographicSize;
            float halfWidth = _params.Camera.orthographicSize * _params.Camera.aspect;

            float minX = (float)-_params.Config.WallsX + halfWidth - _params.Margin;
            float maxX = (float)_params.Config.WallsX - halfWidth + _params.Margin;
            float minY = (float)_params.Config.GroundY + halfHeight - _params.Margin;
            float maxY = float.PositiveInfinity;

            // Clamping Camera View
            if (minX > maxX || minY > maxY)
            {
                throw new InvalidOperationException("bounds too small");
            }

            pos2.x = Mathf.Clamp(pos2.x, minX, maxX);
            pos2.y = Mathf.Clamp(pos2.y, minY, maxY);
            transform.position = new Vector3(pos2.x, pos2.y, p.z);
        }
    }
}
