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
    }
}
