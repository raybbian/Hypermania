using System.Collections.Generic;
using Scenes.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using DeviceType = UnityEngine.DeviceType;

namespace Scenes.Menus.InputSelect
{
    public class DeviceDisplay : MonoBehaviour
    {
        [SerializeField]
        private RectTransform _inputContainer;

        [SerializeField]
        private Vector2 _spacing;

        [SerializeField]
        private GameObject _controllerPrefab;

        [SerializeField]
        private GameObject _keyboardPrefab;

        private Dictionary<InputDevice, (int pos, DeviceIcon icon)> _icons = new();
        private HashSet<int> _occuPos = new();

        public void Awake()
        {
            UpdateDisplay(false);
        }

        public void Update()
        {
            UpdateDisplay();
        }

        public void UpdateDisplay(bool lerp = true)
        {
            foreach ((InputDevice dev, DeviceAssignment asg) in SessionDirectory.RegisteredDevices)
            {
                if (!_icons.ContainsKey(dev))
                {
                    AddDevice(dev, DeviceManager.GetDeviceType(dev), dev.name);
                }

                _icons[dev].icon.SetDeviceAssignment(asg, _spacing.x, lerp);
            }

            List<InputDevice> toRemove = new();
            foreach (InputDevice dev in _icons.Keys)
            {
                if (!SessionDirectory.RegisteredDevices.ContainsKey(dev))
                {
                    toRemove.Add(dev);
                }
            }

            foreach (InputDevice dev in toRemove)
            {
                RemoveDevice(dev);
            }
        }

        private void AddDevice(InputDevice device, DeviceType type, string deviceName)
        {
            GameObject icon = Instantiate(GetInputIcon(type), _inputContainer, false);
            int pos = 0;
            while (_occuPos.Contains(pos))
            {
                pos++;
            }
            RectTransform rect = icon.GetComponent<RectTransform>();
            DeviceIcon deviceIcon = icon.GetComponent<DeviceIcon>();
            deviceIcon.SetDeviceName(deviceName);
            _icons.Add(device, (pos, deviceIcon));
            _occuPos.Add(pos);
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            icon.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, pos * -_spacing.y);
        }

        private void RemoveDevice(InputDevice device)
        {
            if (_icons.Remove(device, out var data))
            {
                Destroy(data.icon);
                _occuPos.Remove(data.pos);
            }
        }

        private GameObject GetInputIcon(DeviceType type)
        {
            if ((type & DeviceType.Gamepad) != 0)
                return _controllerPrefab;

            if ((type & DeviceType.Keyboard) != 0)
                return _keyboardPrefab;

            return null;
        }
    }
}
