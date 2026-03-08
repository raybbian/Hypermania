using UnityEngine;

public class ParallaxController : MonoBehaviour
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public SpriteRenderer image;
        public float speedX; // 0 = moves with camera (no parallax), 1 = stays fixed in world space
    }

    public Camera _camera;
    public ParallaxLayer[] layers;

    private float cameraPrevXValue;

    void Start()
    {
        cameraPrevXValue = _camera.transform.position.x;
    }

    void Update()
    {
        float cameraXMovement = _camera.transform.position.x - cameraPrevXValue;

        for (int i = 0; i < layers.Length; i++)
        {
            layers[i].image.transform.position = new Vector2(
                layers[i].image.transform.position.x + layers[i].speedX * cameraXMovement,
                layers[i].image.transform.position.y
            );
        }

        cameraPrevXValue = _camera.transform.position.x;
    }
}
