using UnityEngine;

namespace Scenes.Core
{
    [DisallowMultipleComponent]
    public class CoreDirectory : MonoBehaviour
    {
        [SerializeField]
        public bool LoadScenesOnStart;

        public void Start()
        {
            if (!LoadScenesOnStart)
                return;
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.Session, SceneDatabase.SESSION)
                .Load(SceneID.MenuBase, SceneDatabase.MENU_BASE)
                .Load(SceneID.MainMenu, SceneDatabase.MAIN_MENU)
                .WithOverlay()
                .Execute();
        }
    }
}
