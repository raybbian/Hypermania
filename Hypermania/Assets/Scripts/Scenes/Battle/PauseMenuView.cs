using System;
using Game.Runners;
using Scenes;
using Scenes.Session;
using UnityEngine;

namespace Scenes.Battle
{
    [RequireComponent(typeof(Animator))]
    public class PauseMenuView : MonoBehaviour
    {
        public Action OnResume;
        private Animator _animator;

        public void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        public void Resume()
        {
            OnResume?.Invoke();
        }

        public void Hide()
        {
            _animator.SetBool("Paused", false);
        }

        public void Show()
        {
            _animator.SetBool("Paused", true);
        }

        public void MainMenu()
        {
            SceneLoader
                .Instance.LoadNewScene()
                .Load(SceneID.MenuBase, SceneDatabase.MENU_BASE)
                .Load(SceneID.MainMenu, SceneDatabase.MAIN_MENU)
                .Unload(SceneID.Battle)
                .WithOverlay()
                .Execute();
        }
    }
}
