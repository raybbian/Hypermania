Shader "Hypermania/UI/PortraitDisc"
{
    Properties
    {
        [PerRendererData] _MainTex ("Portrait", 2D) = "white" {}
        // Multiplied into the portrait. The ring takes the graphic's colour instead.
        _FillColor ("Fill Color", Color) = (1, 1, 1, 1)
        // How much of the radius the ring takes up.
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.14

        // What every UI shader needs to work under a Mask.
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "RenderPipeline"="UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "PortraitDisc"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Set by the canvas when it hands over vertex colours in gamma space.
            int _UIVertexColorAlwaysGammaSpace;

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
            #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace)
                    output.color.rgb = SRGBToLinear(output.color.rgb);
            #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 0 in the middle of the rect, 1 at the edge of the circle.
                float d = length(input.uv - 0.5) * 2.0;
                float aa = max(fwidth(d), 1e-4);
                float circle = 1.0 - smoothstep(1.0 - aa, 1.0, d);
                float ring = smoothstep(1.0 - _OutlineWidth - aa, 1.0 - _OutlineWidth, d);

                half4 fill = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _FillColor;
                half4 color = lerp(fill, input.color, ring);
                color.a *= circle;
                return color;
            }
            ENDHLSL
        }
    }
}
