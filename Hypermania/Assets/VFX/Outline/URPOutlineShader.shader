Shader "Custom/URPSpriteOutline"
{
    Properties
    {
        _OutlineColor    ("Outline Color", Color) = (1,1,1,1)
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float centerA = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).a;
                if (centerA > _AlphaThreshold)
                    return half4(0, 0, 0, 0);

                float2 o = _OutlineWidth * _BlitTexture_TexelSize.xy;
                float a = 0;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( o.x,  0   )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x,  0   )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( 0  ,  o.y )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( 0  , -o.y )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( o.x,  o.y )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x,  o.y )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( o.x, -o.y )).a;
                a += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x, -o.y )).a;

                if (a > _AlphaThreshold)
                    return _OutlineColor;

                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}