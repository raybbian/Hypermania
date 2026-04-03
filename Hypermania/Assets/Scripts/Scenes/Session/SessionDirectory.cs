using System;
using System.Collections.Generic;
using Game.Sim;
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
        public static Dictionary<InputDevice, DeviceAssignment> RegisteredDevices { get; private set; } = new();

        [SerializeField]
        private GameConfig _config;

        [SerializeField]
        private GameOptions _options;

        private void Awake()
        {
            Config = _config;
            Options = _options;
        }

        private void OnValidate()
        {
            Config = _config;
            Options = _options;
            if (_options.LocalPlayers.Length >= 1)
            {
                _options.LocalPlayers[0].InputDevice = Keyboard.current;
            }
        }
    }
}
