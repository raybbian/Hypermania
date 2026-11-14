Shader "Hypermania/FighterShadow"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.01, 0, 0.04, 0.9)
        _GroundY ("Ground Y", Float) = 0
        // How far back on the floor the shadow of something lands, per unit
        // of its height above the ground.
        _Squash ("Squash", Float) = 0.12
        // How far sideways it lands, per unit of height above the ground.
        _Shear ("Shear", Float) = 0.2
        _MinSoftness ("Softness On The Ground", Float) = 0.004
        _Softness ("Softness Per Unit Height", Float) = 0.02
        // Height above the ground at which a shadow has fully faded out.
        _FadeHeight ("Fade Height", Float) = 8
        // Where the floor meets the back wall, so shadows do not climb it.
        _FloorTopY ("Floor Top Y", Float) = 0.4
        _FloorEdgeSoftness ("Floor Edge Softness", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Written every frame by FighterShadowProjector.
            TEXTURE2D(_FighterSilhouette0);
            TEXTURE2D(_FighterSilhouette1);
            SAMPLER(sampler_FighterSilhouette0);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float _GroundY;
                float _Squash;
                float _Shear;
                float _MinSoftness;
                float _Softness;
                float _FadeHeight;
                float _FloorTopY;
                float _FloorEdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            static const float2 kTaps[8] = {
                float2( 1.0000,  0.0000), float2( 0.7071,  0.7071),
                float2( 0.0000,  1.0000), float2(-0.7071,  0.7071),
                float2(-1.0000,  0.0000), float2(-0.7071, -0.7071),
                float2( 0.0000, -1.0000), float2( 0.7071, -0.7071)
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float2 WorldToScreenUV(float2 xy, float z)
            {
                float4 cs = TransformWorldToHClip(float3(xy, z));
                float4 ndc = cs * 0.5;
                return (float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w) / cs.w;
            }

            float SampleSilhouette(TEXTURE2D_PARAM(silhouette, silhouetteSampler), float2 uv)
            {
                float inside = all(uv >= 0.0) && all(uv <= 1.0) ? 1.0 : 0.0;
                return SAMPLE_TEXTURE2D(silhouette, silhouetteSampler, uv).a * inside;
            }

            float BlurredSilhouette(TEXTURE2D_PARAM(silhouette, silhouetteSampler), float2 uv, float2 uvPerWorldX, float2 uvPerWorldY, float radius)
            {
                float a = SampleSilhouette(TEXTURE2D_ARGS(silhouette, silhouetteSampler), uv);
                UNITY_UNROLL
                for (int i = 0; i < 8; i++)
                {
                    float2 offset = (uvPerWorldX * kTaps[i].x + uvPerWorldY * kTaps[i].y) * radius;
                    a += SampleSilhouette(TEXTURE2D_ARGS(silhouette, silhouetteSampler), uv + offset);
                }
                return a / 9.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Height above the ground of whatever would cast onto this
                // pixel. Body height and jump height go through the same
                // mapping, so an airborne fighter's shadow slides away from
                // them exactly as far as the light direction says it should.
                float h = (input.positionWS.y - _GroundY) / _Squash;
                clip(h);
                clip(_FadeHeight - h);

                float2 src = float2(input.positionWS.x - h * _Shear, _GroundY + h);
                float2 uv = WorldToScreenUV(src, input.positionWS.z);
                float2 uvPerWorldX = WorldToScreenUV(src + float2(1.0, 0.0), input.positionWS.z) - uv;
                float2 uvPerWorldY = WorldToScreenUV(src + float2(0.0, 1.0), input.positionWS.z) - uv;

                // The further the caster is from the floor, the softer its shadow.
                float radius = _MinSoftness + _Softness * h;
                float a0 = BlurredSilhouette(TEXTURE2D_ARGS(_FighterSilhouette0, sampler_FighterSilhouette0), uv, uvPerWorldX, uvPerWorldY, radius);
                float a1 = BlurredSilhouette(TEXTURE2D_ARGS(_FighterSilhouette1, sampler_FighterSilhouette0), uv, uvPerWorldX, uvPerWorldY, radius);
                // Overlapping shadows should not get darker than a single one.
                float a = max(a0, a1);

                float fade = saturate(1.0 - h / _FadeHeight);
                float floorEdge = saturate((_FloorTopY - input.positionWS.y) / max(_FloorEdgeSoftness, 1e-4));
                return half4(_ShadowColor.rgb, _ShadowColor.a * a * fade * floorEdge);
            }
            ENDHLSL
        }
    }
}
