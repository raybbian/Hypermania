using UnityEngine;

namespace Game.View.Fighters
{
    // Sits on a big quad under the camera whose material lays the fighters'
    // silhouettes down onto the floor. Purely image based, so whatever is on
    // screen (jumps, hit jitter, root motion) is what casts the shadow.
    // Needs a FighterSilhouetteCapture on the camera.
    [RequireComponent(typeof(MeshRenderer))]
    public class FighterShadowProjector : MonoBehaviour
    {
        // Has to sort behind the fighters but above the stage art.
        [SerializeField]
        private string _sortingLayer = "Character";

        [SerializeField]
        private int _sortingOrder = -100;

        private void OnEnable()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sortingLayerName = _sortingLayer;
            meshRenderer.sortingOrder = _sortingOrder;
        }
    }
}
