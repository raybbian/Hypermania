using System;
using System.Collections.Generic;
using Game;
using Game.Sim;
using Game.Sim.Configs;
using Game.Sim.Replay;
using Game.View.Configs;
using Scenes.Menus.InputSelect;
using Scenes.Menus.MainMenu;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Scenes.Session
{
    [DisallowMultipleComponent]
    public class SessionDirectory : MonoBehaviour
    {
        public static GameConfig Config;
        public static GameOptions Options;
        public static GlobalStats GlobalStats;
        public static AudioPresentation AudioPresentation;

        // Replay-mode payload. Set by MainMenuDirectory.PlayReplay before the
        // Battle transition; consumed by BattleDirectory to drive ReplayRunner.
        public static ReplayFile Replay;

        public static Dictionary<InputDevice, DeviceAssignment> RegisteredDevices { get; private set; } = new();

        [Header("Required")]
        [SerializeField]
        private GlobalStats _globalStats;

        [SerializeField]
        private AudioPresentation _audioPresentation;

        [Header("Overrides")]
        [SerializeField]
        private GameConfig _config;

        [SerializeField]
        private GameOptions _options;

        private void Awake()
        {
            Config = _config;
            Options = _options;
            GlobalStats = _globalStats;
            AudioPresentation = _audioPresentation;
        }

        private void OnValidate()
        {
            Config = _config;
            Options = _options;
            GlobalStats = _globalStats;
            AudioPresentation = _audioPresentation;
            if (
                _options?.Input?.Players != null
                && _options.Input.Players.Length >= 1
                && _options.Input.Players[0] != null
            )
            {
                _options.Input.Players[0].InputDevice = Keyboard.current;
            }
        }
    }
}
