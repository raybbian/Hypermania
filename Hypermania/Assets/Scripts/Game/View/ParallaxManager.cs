using System;
using UnityEngine;

public class ParallaxController : MonoBehaviour
{
    [Serializable]
    public class ParallaxLayer
    {
        public SpriteRenderer Image;
        public Vector2 Speed; // 0 = no movement, 1 = moves with camera
    }

    public Camera _camera;
    public ParallaxLayer[] layers;

    private Vector3 cameraPrevPos;

    void Start()
    {
        cameraPrevPos = _camera.transform.position;
    }

    void Update()
    {
        Vector3 cameraMovement = _camera.transform.position - cameraPrevPos;

        for (int i = 0; i < layers.Length; i++)
        {
            Vector3 pos = (Vector2)layers[i].Image.transform.position + layers[i].Speed * cameraMovement;
            pos.z = layers[i].Image.transform.position.z;

            layers[i].Image.transform.position =
                layers[i].Image.transform.position + Vector3.Scale(cameraMovement, layers[i].Speed);
        }

        cameraPrevPos = _camera.transform.position;
    }
}
