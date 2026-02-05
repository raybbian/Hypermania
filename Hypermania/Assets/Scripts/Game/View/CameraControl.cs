using System;
using System.Collections.Generic;
using Design;
using UnityEngine;

namespace Game.View
{
    public class CameraControl : MonoBehaviour
    {
        private Camera Camera;

        [SerializeField]
        private float CameraSpeed = 10;

        [SerializeField]
        private float MaxZoom = 2.5f;

        [SerializeField]
        private float MinZoom = 1.5f;

        [SerializeField]
        private GlobalConfig Config;

        // Additional area outside the arena bounds that the camera is allowed to see
        [SerializeField]
        private float Margin;

        // Additional area around the interest points that the camera must see
        [SerializeField]
        private float Padding;

        private List<Vector2> _interestPoints;

        void Start()
        {
            Camera = GetComponent<Camera>();
            _interestPoints = new List<Vector2>();
        }

        public void OnValidate()
        {
            if (Config == null)
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
            Vector2 padding = new Vector2(Padding, Padding);
            min -= padding;
            max += padding;

            float width = max.x - min.x;
            float wZoom = Mathf.Clamp(width / 2 / Camera.aspect, MinZoom, MaxZoom);

            float dt = Time.deltaTime;
            float k = CameraSpeed;
            float a = 1f - Mathf.Exp(-k * dt);
            Camera.orthographicSize = Mathf.Lerp(Camera.orthographicSize, wZoom, a);

            // adjust position with respect to zoom

            Vector3 p = transform.position;
            min.y = max.y - 2 * Camera.orthographicSize;
            Vector2 pos2 = Vector2.Lerp(new Vector2(p.x, p.y), (min + max) / 2, a);

            float halfHeight = Camera.orthographicSize;
            float halfWidth = Camera.orthographicSize * Camera.aspect;

            float minX = (float)-Config.WallsX + halfWidth - Margin;
            float maxX = (float)Config.WallsX - halfWidth + Margin;
            float minY = (float)Config.GroundY + halfHeight - Margin;
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
