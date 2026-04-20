using UnityEngine;

namespace Game.View
{
    public class EntityView : MonoBehaviour
    {
        // Layer 6 = CharacterOutline1, Layer 7 = CharacterOutline2 (ProjectSettings/TagManager.asset).
        // Drives which per-player OutlineFeature entry renders this entity.
        public void SetOutlinePlayerIndex(int playerIndex)
        {
            SetLayerRecursive(gameObject, 6 + playerIndex);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
