#ifndef HYPERMANIA_FLOOR_TINT_INCLUDED
#define HYPERMANIA_FLOOR_TINT_INCLUDED

#include "Assets/VFX/Stage/StageFloor.hlsl"

// Stage art only, copied by the 2D renderer before the fighters draw
// (Renderer2D > Camera Sorting Layer Texture).
TEXTURE2D_X(_CameraSortingLayerTexture);

// Half width of the floor patch that gets averaged, in world units.
static const float kFloorPatchHalfWidth = 0.45;

float3 FloorTint_SampleStage(float2 worldXY)
{
    float4 cs = TransformWorldToHClip(float3(worldXY, 0.0));
    float2 uv = float2(cs.x, cs.y * _ProjectionParams.x) / cs.w * 0.5 + 0.5;
    return SAMPLE_TEXTURE2D_X_LOD(_CameraSortingLayerTexture, sampler_LinearClamp, saturate(uv), 0).rgb;
}

// Tints Color toward the floor under WorldPos. A wide patch of floor is
// averaged so the result is the floor's overall colour, not its details.
// Tint is how much of the floor's hue to take on, MatchBrightness is how
// much to dim on a dark floor.
void FloorTint_float(float3 Color, float3 WorldPos, float Tint, float MatchBrightness, out float3 Out)
{
#ifdef SHADERGRAPH_PREVIEW
    Out = Color;
#else
    float3 floorColor = 0.0;
    for (int row = 0; row < 3; row++)
    {
        float y = lerp(STAGE_GROUND_Y, STAGE_FLOOR_TOP_Y, 0.2 + 0.3 * row);
        for (int col = -2; col <= 2; col++)
        {
            float x = WorldPos.x + col * 0.5 * kFloorPatchHalfWidth;
            floorColor += FloorTint_SampleStage(float2(x, y));
        }
    }
    floorColor /= 15.0;

    float peak = max(max(floorColor.r, floorColor.g), max(floorColor.b, 1e-3));
    float3 hue = floorColor / peak;
    // Square root so a dark floor dims the dust without crushing it.
    float brightness = lerp(1.0, sqrt(saturate(peak)), MatchBrightness);
    Out = Color * lerp(float3(1.0, 1.0, 1.0), hue, Tint) * brightness;
#endif
}

void FloorTint_half(half3 Color, half3 WorldPos, half Tint, half MatchBrightness, out half3 Out)
{
    float3 result;
    FloorTint_float(Color, WorldPos, Tint, MatchBrightness, result);
    Out = result;
}

#endif
