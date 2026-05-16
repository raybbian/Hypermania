using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MenuControl : MonoBehaviour
{
    public GameObject[] options;
    private int selectedId;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        selectedId = 0;
        EventSystem.current.SetSelectedGameObject(options[selectedId]);
    }

    // Update is called once per frame
    void Update()
    {
        // If a keyboard is currently active, use this.
        if (Keyboard.current != null)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            {
                selectedId--;
            }
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            {
                selectedId++;
            }
        }

        if (Gamepad.current != null)
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad.dpad.up.wasPressedThisFrame)
            {
                selectedId--;
            }
            if (gamepad.dpad.down.wasPressedThisFrame)
            {
                selectedId++;
            }
        }
        selectedId = (selectedId + options.Length) % options.Length;
        EventSystem.current.SetSelectedGameObject(options[selectedId].GetComponent<Button>().gameObject);
    }
}
