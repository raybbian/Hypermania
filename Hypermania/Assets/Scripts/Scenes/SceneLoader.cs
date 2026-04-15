using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Scenes
{
    public class SceneLoader : MonoBehaviour
    {
        public class SceneLoadPlan
        {
            public Dictionary<SceneID, string> ScenesToLoad { get; } = new();
            public List<SceneID> ScenesToUnload { get; } = new();
            public string ActiveSceneName { get; private set; }
            public bool Overlay { get; private set; } = false;

            public SceneLoadPlan Load(SceneID id, string sceneName, bool setActive = false)
            {
                ScenesToLoad[id] = sceneName;
                if (setActive)
                {
                    ActiveSceneName = sceneName;
                }
                return this;
            }

            public SceneLoadPlan Unload(SceneID id)
            {
                ScenesToUnload.Add(id);
                return this;
            }

            public SceneLoadPlan WithOverlay()
            {
                Overlay = true;
                return this;
            }

            public void Execute()
            {
                Instance.ExecutePlan(this);
            }
        }

        public static SceneLoader Instance;

        [SerializeField]
        private SceneTransitionAnimator _sceneTransitionAnimator;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private Dictionary<SceneID, string> _sceneIDMap = new();
        private bool _isBusy = false;
        private Queue<SceneLoadPlan> _queuedPlans = new();

        public delegate void SceneLoad(SceneID loaded);
        public event SceneLoad OnSceneLoad;

        public void RegisterLoadedScene(SceneID id, string sceneName)
        {
            _sceneIDMap[id] = sceneName;
        }

        public SceneLoadPlan LoadNewScene()
        {
            return new SceneLoadPlan();
        }

        private void ExecutePlan(SceneLoadPlan plan)
        {
            _queuedPlans.Enqueue(plan);
        }

        public void Update()
        {
            if (_isBusy || _queuedPlans.Count == 0)
                return;
            _isBusy = true;
            StartCoroutine(DoSceneTransition(_queuedPlans.Dequeue()));
        }

        private IEnumerator DoSceneTransition(SceneLoadPlan plan)
        {
            if (plan.Overlay)
            {
                yield return _sceneTransitionAnimator.FadeInBlack();
            }
            foreach (SceneID id in plan.ScenesToUnload)
            {
                yield return DoUnloadScene(id);
            }
            foreach ((var slotId, var scene) in plan.ScenesToLoad)
            {
                if (_sceneIDMap.ContainsKey(slotId))
                {
                    yield return DoUnloadScene(slotId);
                }
                yield return DoLoadScene(slotId, scene, plan.ActiveSceneName == scene);
            }
            if (plan.Overlay)
            {
                yield return _sceneTransitionAnimator.FadeOutBlack();
            }
            _isBusy = false;
        }

        private IEnumerator DoLoadScene(SceneID id, string sceneName, bool setActive)
        {
            AsyncOperation loadAction = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (loadAction == null)
                yield break;
            loadAction.allowSceneActivation = false;
            while (loadAction.progress < 0.9f)
            {
                yield return null;
            }
            loadAction.allowSceneActivation = true;
            while (!loadAction.isDone)
            {
                yield return null;
            }
            if (setActive)
            {
                Scene loadingScene = SceneManager.GetSceneByName(sceneName);
                if (loadingScene.IsValid() && loadingScene.isLoaded)
                {
                    SceneManager.SetActiveScene(loadingScene);
                }
            }
            _sceneIDMap[id] = sceneName;
            OnSceneLoad?.Invoke(id);
        }

        private IEnumerator DoUnloadScene(SceneID id)
        {
            if (!_sceneIDMap.TryGetValue(id, out string sceneName))
                yield break;
            if (string.IsNullOrEmpty(sceneName))
                yield break;

            AsyncOperation unloadAction = SceneManager.UnloadSceneAsync(sceneName);
            if (unloadAction != null)
            {
                while (!unloadAction.isDone)
                {
                    yield return null;
                }
            }

            _sceneIDMap.Remove(id);
        }
    }
}
