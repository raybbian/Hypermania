using UnityEngine;

public class ParallaxController : MonoBehaviour
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public SpriteRenderer image;
    }

    public Camera _camera;
    public SpriteRenderer _foreground;
    public SpriteRenderer _background;
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
            float speed = Mathf.InverseLerp(
                _foreground.bounds.size.x,
                _background.bounds.size.x,
                layers[i].image.bounds.size.x
            );
            layers[i].image.transform.position = new Vector2(
                layers[i].image.transform.position.x + speed * cameraXMovement,
                layers[i].image.transform.position.y
            );
        }

        cameraPrevXValue = _camera.transform.position.x;
    }
}
