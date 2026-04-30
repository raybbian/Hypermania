// Stubs for the Unity surface that sim+config sources reach into when
// compiled headless. Kept narrow on purpose - if the sim grows a real Unity
// dependency, prefer pruning it from the sim over expanding these shims.
//
// Nothing here runs. The trainer never instantiates a FighterView prefab or
// reads a Texture2D. These types exist only so the config field declarations
// compile. The shimmed values stay default and are never observed.

using System;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public HideFlags hideFlags;

        public int GetInstanceID() => 0;

        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }

    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61,
    }

    public class ScriptableObject : Object { }

    public class MonoBehaviour : Object { }

    public class GameObject : Object { }

    public class Component : Object { }

    public class Transform : Component { }

    public class AudioClip : Object
    {
        public float length;
    }

    public class Sprite : Object { }

    public class Texture2D : Object { }

    public class AnimationClip : Object
    {
        public float length;
        public bool isLooping;
    }

    public class RuntimeAnimatorController : Object { }

    public class AnimatorOverrideController : RuntimeAnimatorController { }

    public class SpriteRenderer : Component { }

    public static class Mathf
    {
        public static int Min(int a, int b) => a < b ? a : b;

        public static float Min(float a, float b) => a < b ? a : b;

        public static int Max(int a, int b) => a > b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;

        public static int CeilToInt(float v) => (int)System.Math.Ceiling(v);

        public static int FloorToInt(float v) => (int)System.Math.Floor(v);

        public static int RoundToInt(float v) => (int)System.Math.Round(v);

        public static int Clamp(int v, int min, int max) =>
            v < min ? min
            : v > max ? max
            : v;

        public static float Clamp(float v, float min, float max) =>
            v < min ? min
            : v > max ? max
            : v;
    }

    public static class Debug
    {
        public static void Log(object o) { }

        public static void LogWarning(object o) { }

        public static void LogError(object o) { }

        public static void Assert(bool cond) { }

        public static void Assert(bool cond, string msg) { }
    }

    public struct Vector2
    {
        public float x,
            y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public float x,
            y,
            z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public struct Color
    {
        public float r,
            g,
            b,
            a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReference : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HideInInspector : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SpaceAttribute : Attribute
    {
        public SpaceAttribute() { }

        public SpaceAttribute(float height) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponentAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type t) { }

        public RequireComponent(Type t1, Type t2) { }

        public RequireComponent(Type t1, Type t2, Type t3) { }
    }
}

namespace UnityEngine.Rendering
{
    // Existence placeholder so `using UnityEngine.Rendering;` compiles.
    internal sealed class _RenderingNamespacePlaceholder { }
}

namespace UnityEngine.InputSystem
{
    public class InputDevice { }

    public enum Key
    {
        None,
        Space,
        Enter,
        Tab,
        Backquote,
        Quote,
        Semicolon,
        Comma,
        Period,
        Slash,
        Backslash,
        LeftBracket,
        RightBracket,
        Minus,
        Equals,
        A,
        B,
        C,
        D,
        E,
        F,
        G,
        H,
        I,
        J,
        K,
        L,
        M,
        N,
        O,
        P,
        Q,
        R,
        S,
        T,
        U,
        V,
        W,
        X,
        Y,
        Z,
        Digit0,
        Digit1,
        Digit2,
        Digit3,
        Digit4,
        Digit5,
        Digit6,
        Digit7,
        Digit8,
        Digit9,
        LeftShift,
        RightShift,
        LeftAlt,
        RightAlt,
        LeftCtrl,
        RightCtrl,
        LeftMeta,
        RightMeta,
        ContextMenu,
        Escape,
        LeftArrow,
        RightArrow,
        UpArrow,
        DownArrow,
        Backspace,
        PageDown,
        PageUp,
        Home,
        End,
        Insert,
        Delete,
        CapsLock,
        NumLock,
        PrintScreen,
        ScrollLock,
        Pause,
        NumpadEnter,
        NumpadDivide,
        NumpadMultiply,
        NumpadPlus,
        NumpadMinus,
        NumpadPeriod,
        NumpadEquals,
        Numpad0,
        Numpad1,
        Numpad2,
        Numpad3,
        Numpad4,
        Numpad5,
        Numpad6,
        Numpad7,
        Numpad8,
        Numpad9,
        F1,
        F2,
        F3,
        F4,
        F5,
        F6,
        F7,
        F8,
        F9,
        F10,
        F11,
        F12,
    }

    public enum GamepadButton
    {
        North,
        South,
        East,
        West,
        Start,
        Select,
        LeftStick,
        RightStick,
        LeftShoulder,
        RightShoulder,
        DpadUp,
        DpadDown,
        DpadLeft,
        DpadRight,
        LeftTrigger,
        RightTrigger,
        Y,
        A,
        B,
        X,
        Cross,
        Circle,
        Square,
        Triangle,
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    public struct GamepadState { }
}

namespace UnityEngine.U2D.Animation
{
    public class SpriteLibraryAsset : Object { }
}

namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : Attribute
    {
        public FormerlySerializedAsAttribute(string oldName) { }
    }
}

namespace TMPro
{
    public class TMP_Text : UnityEngine.Component { }
}

// Stand-in for Hypermania/Assets/Scripts/Game/GameManager.cs (a Unity
// MonoBehaviour we don't pull into headless). Only the constants are read by
// sim/config code.
namespace Game
{
    public static class GameManager
    {
        public const int TPS = 60;
        public const int ROLLBACK_FRAMES = 8;
    }
}

// View prefabs the configs reference by type. Empty in headless - the trainer
// never spawns them. The Unity build uses the real classes from
// Hypermania/Assets/Scripts/Game/View/.
namespace Game.View.Fighters
{
    public class FighterView : UnityEngine.MonoBehaviour { }
}

namespace Game.View.Projectiles
{
    public class ProjectileView : UnityEngine.MonoBehaviour { }
}

namespace Game.View.Events
{
    public class FighterMoveSfx : UnityEngine.ScriptableObject { }
}

namespace Game.View.Events.Vfx
{
    internal sealed class _VfxNamespacePlaceholder { }
}
