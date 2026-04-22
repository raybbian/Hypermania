using System;
using Design.Configs;
using Game.Sim;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Utils.EnumArray;

public class ConfigInputMenu : MonoBehaviour
{
    [SerializeField]
    private ControlsConfig _config;

    [SerializeField]
    private ConfigInputListener _listener;
    public EnumArray<InputFlags, Binding> _controlScheme => _config.ControlScheme;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        int i = 0;
        foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
        {
            if (flag == InputFlags.None)
            {
                continue;
            }
            ConfigInputListener inputListener = Instantiate(_listener);
            inputListener.transform.parent = transform;
            inputListener.gameObject.transform.localPosition = new Vector3(0, 300 + i * -50, 0);
            inputListener.gameObject.transform.localScale = new Vector3(.75f, .75f, .75f);
            inputListener.setFlag(flag);
            //inputListener.setInputs(_controlScheme[flag]);
            i++;
        }
    }
}
