using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    public class EntityView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip(
            "GameObjects (and their children) to skip when SetOutlinePlayerIndex reassigns layers — keeps VFX or props that need to stay on their own layer off the outline pass."
        )]
        private GameObject[] _outlineExclusions;

        // Layer 6 = CharacterOutline1, Layer 7 = CharacterOutline2 (ProjectSettings/TagManager.asset).
        // Drives which per-player OutlineFeature entry renders this entity.
        public void SetOutlinePlayerIndex(int playerIndex)
        {
            HashSet<Transform> excluded = null;
            if (_outlineExclusions != null && _outlineExclusions.Length > 0)
            {
                excluded = new HashSet<Transform>();
                for (int i = 0; i < _outlineExclusions.Length; i++)
                {
                    GameObject go = _outlineExclusions[i];
                    if (go != null)
                        excluded.Add(go.transform);
                }
            }
            SetLayerRecursive(gameObject, 6 + playerIndex, excluded);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            SetLayerRecursive(go, layer, null);
        }

        public static void SetLayerRecursive(GameObject go, int layer, HashSet<Transform> excluded)
        {
            if (excluded != null && excluded.Contains(go.transform))
                return;
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer, excluded);
        }
    }
}
