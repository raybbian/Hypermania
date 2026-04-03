using Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// When entering Play mode, checks for already-loaded scenes that match
/// SceneDatabase entries and registers them with SceneLoader so it knows
/// they exist.
/// </summary>
public static class DevSceneBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterOpenScenes()
    {
        if (SceneLoader.Instance == null)
            return;

        int registered = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            if (SceneDatabase.NameToID.TryGetValue(scene.name, out SceneID id))
            {
                SceneLoader.Instance.RegisterLoadedScene(id, scene.name);
                registered++;
            }
        }

        if (registered > 0)
        {
            Debug.Log($"[DevSceneBootstrapper] Registered {registered} already-loaded scene(s) with SceneLoader");
        }
    }
}
