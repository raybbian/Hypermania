// Unity-only conversions between SVector2 and UnityEngine.Vector2. Compiled
// in by the Unity asmdef (UNITY_5_3_OR_NEWER is defined); excluded from the
// .NET build (Hypermania.CPU never sees UnityEngine, and dotnet build skips
// this whole block).
#if UNITY_5_3_OR_NEWER
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Hypermania.Shared.SoftFloat
{
    public partial struct SVector2
    {
        // sfloat -> float is non-throwing, so implicit is safe and gives view
        // code clean ergonomics: `transform.position = simState.Position;`.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(SVector2 v) =>
            new Vector2((float)v.x, (float)v.y);

        // float -> sfloat is the lossy direction (and only valid in
        // editor/authoring code); explicit forces a cast at the call site.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator SVector2(Vector2 v) =>
            new SVector2((sfloat)v.x, (sfloat)v.y);
    }
}
#endif
