using System;
using UnityEngine;

namespace Game.View.Background
{
    public class ParallaxController : MonoBehaviour
    {
        [Serializable]
        public class ParallaxLayer
        {
            public SpriteRenderer Image;
            public Vector2 Speed; // 0 = no movement, 1 = moves with camera
        }

        [SerializeField]
        private Camera _camera;

        [SerializeField]
        private ParallaxLayer[] _layers;

        private Vector3 _cameraPrevPos;

        void Start()
        {
            _cameraPrevPos = _camera.transform.position;
        }

        void Update()
        {
            Vector3 cameraMovement = _camera.transform.position - _cameraPrevPos;

            for (int i = 0; i < _layers.Length; i++)
            {
                _layers[i].Image.transform.position += Vector3.Scale(cameraMovement, _layers[i].Speed);
            }

            _cameraPrevPos = _camera.transform.position;
        }
    }
}
