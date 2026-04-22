using System.Collections.Generic;
using Design.Configs;
using Game.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

public class ConfigInputListener : MonoBehaviour
{
    [SerializeField]
    protected InputDevice _inputDevice;

    [SerializeField]
    protected InputFlags _inputFlag = InputFlags.None;

    [SerializeField]
    protected GameObject _inputTitleObject,
        _primaryInputObject,
        _secondaryInputObject;

    private Button _primaryInputButton,
        _secondaryInputButton;
    private TextMeshProUGUI _inputTitleText,
       _primaryInputText,
        _secondaryInputText;

   private InputControl _primaryInput,
        _secondaryInput;


    void Awake()
    {
        _inputTitleText = _inputTitleObject.GetComponent<TextMeshProUGUI>();
        _primaryInputText = _primaryInputObject.GetComponent<TextMeshProUGUI>();
        _secondaryInputText = _secondaryInputObject.GetComponent<TextMeshProUGUI>();
        _primaryInputButton = _primaryInputObject.GetComponent<Button>();
        _secondaryInputButton = _secondaryInputObject.GetComponent<Button>();

        _inputTitleText.text = _inputFlag.ToString().ToUpper();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void ActivatePrimaryInputListener()
    {
        Debug.Log($"PRIMARY INPUT AWAITING RESPONSE");
        _primaryInputText.text = "";
        InputSystem.onAnyButtonPress.CallOnce(ctrl => recordButtonPress(ctrl, 0));
        _primaryInputButton.interactable = false;
        _secondaryInputButton.interactable = false;
    }

    public void ActivateSecondaryInputListener()
    {
        Debug.Log($"SECONDARY INPUT AWAITING RESPONSE");
        _secondaryInputText.text = "";
        InputSystem.onAnyButtonPress.CallOnce(ctrl => recordButtonPress(ctrl, 1));
        _primaryInputButton.interactable = false;
        _secondaryInputButton.interactable = false;
    }

    private void recordButtonPress(InputControl ctrl, int inputPriority)
    {
        if (ctrl.device != _inputDevice)
        { //Check If Input Device Is The Correct Player's Device
            //return;
        }
        if (inputPriority == 0)
        {
            _primaryInputText.text = ctrl.displayName;
            _primaryInput = ctrl;
            Debug.Log($"Primary Input Stored: {_primaryInput}");
        }
        if (inputPriority == 1)
        {
            _secondaryInputText.text = ctrl.displayName;
            _secondaryInput = ctrl;
            Debug.Log($"Secondary Input Stored: {_secondaryInput}");
        }

        _primaryInputButton.interactable = true;
        _secondaryInputButton.interactable = true;
    }

    public void setFlag(InputFlags flag)
    {
        _inputFlag = flag;
        _inputTitleText.text = _inputFlag.ToString().ToUpper();
    }

    public void setInputs(Binding binds)
    {
        /*
         * STEPS:
         * 1) Check for Gamepad vs. Keyboard
         * 2) Convert InputControl Class to Classes Used in our Input System
         * 3) Set InputControl primaryInput, secondaryInput Local Variables
         * 4) Update Control Config File
        */
    }
}
