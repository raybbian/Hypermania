using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils.EnumArray;

[CreateAssetMenu(menuName = "Hypermania/Controls Config")]
public class ControlsConfig : ScriptableObject
{
    [SerializeField]
    protected EnumArray<InputFlags, Binding> controlScheme;

    [SerializeField]
    protected Dictionary<InputFlags, Key> defaultBindings = new Dictionary<InputFlags, Key>
    {
        { InputFlags.None, Key.None },
        { InputFlags.Up, Key.W },
        { InputFlags.Down, Key.S },
        { InputFlags.Left, Key.A },
        { InputFlags.Right, Key.D },
        { InputFlags.LightAttack, Key.J },
        { InputFlags.MediumAttack, Key.K },
        { InputFlags.HeavyAttack, Key.L },
        { InputFlags.SpecialAttack, Key.I },
        { InputFlags.Burst, Key.O },
        { InputFlags.Mania1, Key.A },
        { InputFlags.Mania2, Key.S },
        { InputFlags.Mania3, Key.D },
        { InputFlags.Mania4, Key.J },
        { InputFlags.Mania5, Key.K },
        { InputFlags.Mania6, Key.L },
    };

    public EnumArray<InputFlags, Binding> GetControlScheme()
    {
        return controlScheme;
    }

    public Key GetDefaultBinding(InputFlags inputFlag)
    {
        return defaultBindings[inputFlag];
    }
}

[Serializable]
public class Binding
{
    [SerializeField]
    protected Key primaryKey;

    [SerializeField]
    protected Key altKey;

    public Key GetPrimaryKey()
    {
        return primaryKey;
    }

    public Key GetAltKey()
    {
        return altKey;
    }
}
