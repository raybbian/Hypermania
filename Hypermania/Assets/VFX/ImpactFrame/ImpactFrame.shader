Shader "Hypermania/ImpactFrame"
{
    Properties
    {
        // Parts of a fighter brighter than this come out white, the rest black.
        _InkThreshold ("Ink Threshold", Range(0, 1)) = 0.2
        // Black line drawn around each fighter so they read against white.
        _EdgeWidth ("Fighter Edge Width (px at 720p)", Float) = 2
        // White gap kept clear around each fighter so they never merge with a line.
        _HaloWidth ("Fighter Halo Width (px at 720p)", Float) = 6
        _LineCount ("Speed Line Slots", Float) = 90
        // How many of those slots actually hold a line.
        _LineDensity ("Speed Line Density", Range(0, 1)) = 0.45
        // How much of its slot a line fills once it has fully widened.
        _LineWidth ("Speed Line Width", Range(0, 1)) = 0.6
        // How far from the hit the lines start, as a fraction of screen height.
        _LineInnerRadius ("Speed Line Inner Radius", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "ImpactFrame"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/VFX/FighterSilhouette/FighterSilhouettes.hlsl"

            // Set every frame by ImpactFrameView. xy: where the hit landed in
            // viewport space, z: which flash this is (odd ones are inverted),
            // w: how far the fighters are zoomed in toward the hit.
            float4 _ImpactFrame;

            CBUFFER_START(UnityPerMaterial)
                float _InkThreshold;
                float _EdgeWidth;
                float _HaloWidth;
                float _LineCount;
                float _LineDensity;
                float _LineWidth;
                float _LineInnerRadius;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // Both fighters in one sample, whichever covers the pixel more.
            half4 SampleFighters(float2 uv)
            {
                half4 a = SAMPLE_TEXTURE2D(_FighterSilhouette0, sampler_FighterSilhouette0, uv);
                half4 b = SAMPLE_TEXTURE2D(_FighterSilhouette1, sampler_FighterSilhouette0, uv);
                return a.a > b.a ? a : b;
            }

            float Coverage(float2 uv)
            {
                return SampleFighters(uv).a;
            }

            float Hash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            // Offset from the hit, measured in screen heights.
            float2 FromHit(float2 uv)
            {
                float2 d = uv - _ImpactFrame.xy;
                d.x *= _ScreenParams.x / _ScreenParams.y;
                return d;
            }

            // Wedges radiating from the hit that start a little way out and
            // widen toward the edges of the screen. Each flash gets its own set.
            float SpeedLines(float2 d, float flash)
            {
                float r = length(d);

                float slot = (atan2(d.y, d.x) / TWO_PI + 0.5) * _LineCount;
                float rnd = Hash(floor(slot) + flash * 31.0);
                float present = step(1.0 - _LineDensity, rnd);

                float start = _LineInnerRadius * (0.6 + 0.8 * frac(rnd * 7.31));
                float grow = saturate((r - start) / max(1.0 - start, 1e-3));
                float across = abs(frac(slot) - 0.5);
                return present * step(start, r) * step(across, 0.5 * _LineWidth * grow);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float flash = _ImpactFrame.z;
                float2 d = FromHit(input.uv);

                // Only the fighters punch in. The lines already radiate from
                // the hit, so zooming them would change nothing.
                float2 uv = _ImpactFrame.xy + (input.uv - _ImpactFrame.xy) / _ImpactFrame.w;
                half4 fighter = SampleFighters(uv);
                float inside = step(0.5, fighter.a);

                float luminance = dot(fighter.rgb / max(fighter.a, 1e-3), float3(0.2126, 0.7152, 0.0722));
                float ink = step(_InkThreshold, luminance);

                float2 px = (_ScreenParams.y / 720.0) / _ScreenParams.xy;

                // On the fighter but next to a pixel that is not: that is the outline.
                float2 e = _EdgeWidth * px;
                float leastNearby = min(
                    min(Coverage(uv + float2(e.x, 0.0)), Coverage(uv - float2(e.x, 0.0))),
                    min(Coverage(uv + float2(0.0, e.y)), Coverage(uv - float2(0.0, e.y)))
                );
                float edge = 1.0 - step(0.5, leastNearby);

                // Off the fighter but close to one: that is the halo.
                float2 h = _HaloWidth * px;
                float2 hd = h * 0.7071;
                float mostNearby = max(
                    max(
                        max(Coverage(uv + float2(h.x, 0.0)), Coverage(uv - float2(h.x, 0.0))),
                        max(Coverage(uv + float2(0.0, h.y)), Coverage(uv - float2(0.0, h.y)))
                    ),
                    max(
                        max(Coverage(uv + hd), Coverage(uv - hd)),
                        max(Coverage(uv + float2(hd.x, -hd.y)), Coverage(uv + float2(-hd.x, hd.y)))
                    )
                );
                float halo = step(0.5, mostNearby);

                float background = max(1.0 - SpeedLines(d, flash), halo);
                float value = lerp(background, ink * (1.0 - edge), inside);
                value = lerp(value, 1.0 - value, fmod(flash, 2.0));
                return half4(value, value, value, 1.0);
            }
            ENDHLSL
        }
    }
}
