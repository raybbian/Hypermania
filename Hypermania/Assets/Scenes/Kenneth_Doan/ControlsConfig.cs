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
    private EnumArray<InputFlags, Binding> _controlScheme;

    // Dictionary for default bindings
    private readonly Dictionary<InputFlags, Key> _defaultBindings = new()
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

    // Sets Default Bindings onEnable to avoid Null Primary bindings
    private void OnEnable()
    {
        _controlScheme ??= new EnumArray<InputFlags, Binding>();

        foreach (InputFlags flag in Enum.GetValues(typeof(InputFlags)))
        {
            if (_controlScheme[flag] == null)
            {
                _controlScheme[flag] = new Binding(_defaultBindings.GetValueOrDefault(flag, Key.None), Key.None);
            }
        }
    }

    /**
     * Getter to return array of InputFlags and Bindings
     */
    public EnumArray<InputFlags, Binding> GetControlScheme()
    {
        return _controlScheme;
    }
}

[Serializable]
public class Binding
{
    [SerializeField]
    private Key _primaryKey;

    [SerializeField]
    private Key _altKey;

    /**
     * Base Binding Constructor
     *
     * Constructs a Binding Structure to store control binds
     *
     * @param primaryKey - Stores the primary key
     * @param altKey - Stores an alternate key
     *
     */
    public Binding(Key primaryKey, Key altKey)
    {
        _primaryKey = primaryKey;
        _altKey = altKey;
    }

    // Getters to Return keys stored in Binding Structure
    public Key GetPrimaryKey()
    {
        return _primaryKey;
    }

    public Key GetAltKey()
    {
        return _altKey;
    }
}
