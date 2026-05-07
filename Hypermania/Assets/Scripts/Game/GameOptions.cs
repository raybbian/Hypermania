using System;
using Game.View.Configs;
using Game.View.Configs.Input;
using Hypermania.Game;
using Hypermania.Shared;
using UnityEngine.InputSystem;
using Utils.EnumArray;

namespace Game
{
    // Per-player view fields the runner needs but the sim never reads.
    [Serializable]
    public class PlayerPresentation
    {
        public CharacterPresentation Character;
        public int SkinIndex;
        public string Username;
    }

    // The view-side authoring inputs - skins, prefabs, audio, stage. Runners
    // read this to spawn fighter views, route SFX, pick the stage scene, etc.
    // Sim never sees this field on GameOptions.
    [Serializable]
    public class PresentationOptions
    {
        public PlayerPresentation[] Players;
        public AudioPresentation Audio;
        public Stage Stage;
    }

    // Per-player input bindings. Null entries are valid (remote / spectator).
    // Sim never sees this; runners walk it to wire up InputBuffer per local
    // player at match start.
    [Serializable]
    public class PlayerInputBindings
    {
        public InputDevice InputDevice;

        // Per-player bindings from the disk-backed ControlsProfile selected
        // in the character-select controls menu. Null falls back to
        // ControlsConfig.DefaultBindings.
        public EnumArray<InputFlags, Binding> ControlScheme;
    }

    [Serializable]
    public class InputOptions
    {
        public PlayerInputBindings[] Players;
    }

    // Top-level options bundle the runner constructs at match start. The sim
    // takes only the Sim portion via Advance(simOptions, inputs); Presentation
    // and Input belong to the Unity-side runner / view layer.
    [Serializable]
    public class GameOptions
    {
        public SimOptions Sim;
        public PresentationOptions Presentation;
        public InputOptions Input;
    }
}
