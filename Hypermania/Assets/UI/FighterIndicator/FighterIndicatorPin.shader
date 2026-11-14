Shader "Hypermania/UI/FighterIndicatorPin"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        // Both are fractions of the distance from the middle of the rect to its edge.
        _HeadRadius ("Head Radius", Range(0, 1)) = 0.5
        _TipDistance ("Tip Distance", Range(0, 1)) = 0.8

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
            Name "FighterIndicatorPin"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            // Set by the canvas when it hands over vertex colours in gamma space.
            int _UIVertexColorAlwaysGammaSpace;

            CBUFFER_START(UnityPerMaterial)
                float _HeadRadius;
                float _TipDistance;
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
                // -1 to 1 across the rect. The head sits in the middle and the
                // tail runs straight up to its tip.
                float2 p = (input.uv - 0.5) * 2.0;
                float fromMiddle = length(p);

                // The tail's sides leave the head along its tangents, so the two
                // meet without a corner. This is the direction to where they touch.
                float rise = _HeadRadius / _TipDistance;
                float2 touch = float2(sqrt(1.0 - rise * rise), rise);

                float head = fromMiddle - _HeadRadius;
                float tail = dot(float2(abs(p.x), p.y), touch) - _HeadRadius;
                float edge = p.y > fromMiddle * rise ? tail : head;

                float aa = max(fwidth(edge), 1e-4);
                half4 color = input.color;
                color.a *= 1.0 - smoothstep(-aa, 0.0, edge);
                return color;
            }
            ENDHLSL
        }
    }
}
