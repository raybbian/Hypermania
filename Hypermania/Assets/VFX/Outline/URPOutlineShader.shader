Shader "Custom/URPSpriteOutline"
{
    Properties
    {
        [HDR] _OutlineColor    ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth    ("Outline Width (px)", Range(0, 16)) = 2
        _AlphaThreshold  ("Alpha Threshold", Range(0,1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Outline"
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _AlphaThreshold;

            // 16-tap multi-ring sampling: two concentric rings at offset angles
            // give 17 distinct coverage levels instead of 9, and sampling at half
            // width catches the sprite body for sub-outline-width silhouettes.
            static const float2 kRingOuter[8] = {
                float2( 1.0000,  0.0000), float2( 0.7071,  0.7071),
                float2( 0.0000,  1.0000), float2(-0.7071,  0.7071),
                float2(-1.0000,  0.0000), float2(-0.7071, -0.7071),
                float2( 0.0000, -1.0000), float2( 0.7071, -0.7071)
            };
            static const float2 kRingInner[8] = {
                float2( 0.9239,  0.3827), float2( 0.3827,  0.9239),
                float2(-0.3827,  0.9239), float2(-0.9239,  0.3827),
                float2(-0.9239, -0.3827), float2(-0.3827, -0.9239),
                float2( 0.3827, -0.9239), float2( 0.9239, -0.3827)
            };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 texel = _BlitTexture_TexelSize.xy;

                float centerA = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).a;

                float2 oOuter = _OutlineWidth * texel;
                float2 oInner = _OutlineWidth * 0.5 * texel;

                float a = 0.0;
                UNITY_UNROLL
                for (int i = 0; i < 8; i++)
                {
                    a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + kRingOuter[i] * oOuter).a;
                    a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + kRingInner[i] * oInner).a;
                }
                float avgA = a / 16.0;

                // Outer coverage still uses the threshold knob.
                float coverage = smoothstep(0.0, _AlphaThreshold, avgA);

                // Inner (sprite↔outline) edge uses the full [0, 1] range so the
                // bilinear ~0.5 value at the silhouette boundary maps to ~0.5
                // here instead of ~1. Threshold is deliberately not used here.
                float insideMask = smoothstep(0.0, 1.0, centerA);

                float ringMask = coverage * (1.0 - insideMask);
                float outlineAlpha = _OutlineColor.a * ringMask;

                // HDR bloom on partial-coverage edge pixels is what shimmers:
                // sub-pixel ring shifts flip pixels above/below the bloom
                // threshold each frame. Keep HDR only at stable fully-covered
                // pixels (the ring's interior); fade to LDR on the AA edges.
                half3 hdr = _OutlineColor.rgb;
                half3 ldr = min(hdr, half3(1.0, 1.0, 1.0));
                half3 rgb = lerp(ldr, hdr, ringMask);
                return half4(rgb, outlineAlpha);
            }
            ENDHLSL
        }
    }
}