Shader "Custom/SpriteWithDepth"
{
    Properties { _MainTex ("Sprite Texture", 2D) = "white" {} }
    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "RenderPipeline"="UniversalPipeline"
        }
        Cull Off
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha
        AlphaToMask On  // helps soften edges

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f    { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - 0.1);  // discard mostly-transparent pixels
                return col;
            }
            ENDCG
        }
    }
}