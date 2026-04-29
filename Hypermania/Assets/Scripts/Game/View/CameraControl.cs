using System;
using System.Collections.Generic;
using Game.Sim.Configs;
using Game.Sim;
using Game.View.Events;
using UnityEngine;

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
            public float ZoomSpeed;

            // Most zoomed-in orthographic half-height. Used during mania.
            public float MinZoom;

            // Most zoomed-out orthographic half-height. Used during normal fighting.
            public float MaxZoom;

            public float CountdownFocusHalfHeight;

            // Extra space added around the interest-point bounding box before
            // computing camera position. Pure visual padding.
            public float Padding;

            // Additional area outside the arena bounds the camera is allowed to see.
            public float Margin;
            public Camera Camera;
        }

        [SerializeField]
        private Params _params;
        private GlobalStats _stats;
        private List<Vector2> _interestPoints;
        private float _targetZoom;

        public void Init(GlobalStats stats)
        {
            _stats = stats;
            _interestPoints = new List<Vector2>();
            _targetZoom = _params.MaxZoom;
            _params.Camera.orthographicSize = _targetZoom;
        }

        public void UpdateCamera(List<Vector2> interestPoints, GameMode gameMode, Vector2? focusPoint = null)
        {
            if (focusPoint.HasValue)
            {
                _interestPoints = new List<Vector2> { focusPoint.Value };
                _targetZoom = _params.CountdownFocusHalfHeight;
                return;
            }

            _interestPoints = interestPoints;
            bool inMania = gameMode == GameMode.ManiaStart || gameMode == GameMode.Mania;
            _targetZoom = inMania ? _params.MinZoom : _params.MaxZoom;
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

            float dt = Time.deltaTime;
            float k = _params.CameraSpeed;
            float a = 1f - Mathf.Exp(-k * dt);

            float zoomA = 1f - Mathf.Exp(-_params.ZoomSpeed * dt);
            _params.Camera.orthographicSize = Mathf.Lerp(_params.Camera.orthographicSize, _targetZoom, zoomA);

            // adjust position with respect to zoom
            Vector3 p = transform.position;
            min.y = max.y - 2 * _params.Camera.orthographicSize;
            Vector2 pos2 = Vector2.Lerp(new Vector2(p.x, p.y), (min + max) / 2, a);
            float halfHeight = _params.Camera.orthographicSize;
            float halfWidth = _params.Camera.orthographicSize * _params.Camera.aspect;

            float minX = (float)-_stats.WallsX + halfWidth - _params.Margin;
            float maxX = (float)_stats.WallsX - halfWidth + _params.Margin;
            float minY = (float)_stats.GroundY + halfHeight - _params.Margin;
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
