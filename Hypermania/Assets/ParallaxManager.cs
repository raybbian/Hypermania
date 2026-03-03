using UnityEngine;
using UnityEngine.UI;

public class ParallaxManager : MonoBehaviour
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public RawImage rawImage;
        public float speedX; // how fast a layer moves - higher = faster
        public float zoomSpeed; // how much a layer shrinks when zooming out {0-1}
    }

    [SerializeField]
    private ParallaxLayer[] layers;
    [SerializeField]
    private Camera _camera;
    private float[] offsetX;
    private float cameraPrevXValue;
    private float defZoom;
    private bool initialized;

    void Initialize()
    {
        offsetX = new float[layers.Length];
        cameraPrevXValue = _camera.transform.position.x;
        defZoom = _camera.orthographicSize;
        for (int i = 0; i < layers.Length; i++)
        {
            offsetX[i] = layers[i].rawImage.rectTransform.anchoredPosition.x;
        }
        initialized = true;
    }

    void Update()
    {
        if (!initialized)
        {
            if (Mathf.Approximately(_camera.orthographicSize, 1.5f)) // skips the initial zoom when game starts
            {
                Initialize();
            }
            return;
        }
        float cameraXMovement = _camera.transform.position.x - cameraPrevXValue;
        float zoomRatio = defZoom / _camera.orthographicSize;
        for (int i = 0; i < layers.Length; i++)
        {
            offsetX[i] -= layers[i].speedX * cameraXMovement;
            layers[i].rawImage.rectTransform.anchoredPosition = new Vector2(offsetX[i], layers[i].rawImage.rectTransform.anchoredPosition.y);
            float layerZoom = Mathf.Lerp(1f, zoomRatio, layers[i].zoomSpeed);
            layers[i].rawImage.rectTransform.localScale = new Vector3(layerZoom, layerZoom, 1);
        }

        cameraPrevXValue = _camera.transform.position.x;
    }
}