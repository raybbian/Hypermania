using System;
using TMPro;
using UnityEngine;

namespace Scenes.Menus.InputSelect
{
    public class DeviceIcon : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _deviceName;

        private RectTransform _rectTransform;

        private Vector2 _targetTransform;

        public void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        public void SetDeviceName(string deviceName)
        {
            _deviceName.text = deviceName;
        }

        public void Update()
        {
            _rectTransform.anchoredPosition = Vector2.Lerp(
                _rectTransform.anchoredPosition,
                _targetTransform,
                Time.deltaTime * 10
            );
        }

        public void SetDeviceAssignment(DeviceAssignment assignment, float spacing, bool lerp = true)
        {
            _targetTransform = new Vector2(((int)assignment - 1) * spacing, _rectTransform.anchoredPosition.y);
            if (!lerp)
            {
                _rectTransform.anchoredPosition = _targetTransform;
            }
        }
    }
}
