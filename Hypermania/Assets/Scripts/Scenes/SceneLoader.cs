using System;
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

        // True while the fade has the screen blacked out.
        private bool _covered;
        private readonly List<IEnumerator> _beforeReveal = new();

        public delegate void SceneLoad(SceneID loaded);
        public event SceneLoad OnSceneLoad;

        public void RegisterLoadedScene(SceneID id, string sceneName)
        {
            _sceneIDMap[id] = sceneName;
        }

        public bool IsLoaded(SceneID id) => _sceneIDMap.ContainsKey(id);

        // True while a transition is running or waiting its turn.
        public bool Busy => _isBusy || _queuedPlans.Count > 0;

        // For a scene that has just loaded and still has things to get ready,
        // like building shaders. The screen stays black until the work is
        // done. Returns false, without running it, when there is no black
        // screen to do it behind.
        public bool HoldReveal(IEnumerator work)
        {
            if (!_covered)
                return false;
            _beforeReveal.Add(work);
            return true;
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
                _covered = true;
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
            // Indexed, since the work can ask for more of it.
            for (int i = 0; i < _beforeReveal.Count; i++)
            {
                yield return RunHeld(_beforeReveal[i]);
            }
            _beforeReveal.Clear();
            _covered = false;
            if (plan.Overlay)
            {
                yield return _sceneTransitionAnimator.FadeOutBlack();
            }
            _isBusy = false;
        }

        // Stepped by hand so that work which throws gets logged and dropped,
        // and can't leave the game stuck on a black screen.
        private static IEnumerator RunHeld(IEnumerator work)
        {
            while (true)
            {
                bool more;
                try
                {
                    more = work.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    more = false;
                }
                if (!more)
                    yield break;
                yield return work.Current;
            }
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
