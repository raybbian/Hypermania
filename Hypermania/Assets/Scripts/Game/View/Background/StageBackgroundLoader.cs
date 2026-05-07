using UnityEngine;
using Utils.EnumArray;
using Hypermania.Shared;

namespace Game.View.Background
{

    public class StageBackgroundLoader : MonoBehaviour
    {
        [SerializeField]
        private EnumArray<Stage, GameObject> _prefabs;

        [SerializeField]
        private Camera _camera;

        private GameObject _instance;

        public void Init(Stage stage)
        {
            GameObject prefab = _prefabs[stage];
            if (prefab == null)
                return;
            _instance = Instantiate(prefab, transform, false);
            ParallaxController controller = _instance.GetComponent<ParallaxController>();
            controller.Init(_camera);
        }
    }
}
