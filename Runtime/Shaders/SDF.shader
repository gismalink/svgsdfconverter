//codex-agent
Shader "SVG SDF/SDF"
{
    Properties
    {
        [NoScaleOffset]
        _MainTex ("SDF Texture", 2D) = "white" {}

        [Header(Poster Visibility)]
        _DisolveAlpha ("Visibility", Range(0,1)) = 1
        _DisolveNoiseScale ("Dissolve Noise Scale", Float) = 500

        [Header(Base Settings)]
        _Color ("Color", Color) = (1,1,1,1)

        _Threshold ("Shape Threshold", Range(0,1)) = 0.5

        _Softness ("Softness", Range(0,0.5)) = 0

        _OutlineWidth ("Outline Width", Range(0,0.5)) = 0

        _OutlineColor ("Outline Color", Color) =
            (0,0,0,1)

    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM

            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
            };

            sampler2D _MainTex;

            float4 _MainTex_ST;

            fixed4 _Color;
            float _DisolveAlpha;
            float _DisolveNoiseScale;

            float _Threshold;
            float _Softness;

            float _OutlineWidth;
            fixed4 _OutlineColor;


            v2f vert(appdata v)
            {
                v2f o;

                o.vertex =
                    UnityObjectToClipPos(v.vertex);

                o.uv =
                    TRANSFORM_TEX(
                        v.uv,
                        _MainTex
                    );

                o.color = v.color;
                o.screenPosition = ComputeScreenPos(o.vertex);

                return o;
            }

            float SampleSDF(float2 uv)
            {
                return tex2D(
                    _MainTex,
                    uv
                ).r;
            }

            float SDFAlpha(
                float distance,
                float edge,
                float softness)
            {
                if (softness <= 0.0)
                    return step(edge, distance);

                return smoothstep(
                    edge - softness,
                    edge + softness,
                    distance
                );
            }

            float PosterNoiseHash(float2 cell)
            {
                uint2 value = (uint2)(int2)round(cell);
                value.y ^= 1103515245U;
                value.x += value.y;
                value.x *= value.y;
                value.x ^= value.x >> 5u;
                value.x *= 0x27d4eb2du;
                return (value.x >> 8) * (1.0 / 16777215.0);
            }

            float PosterValueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 weight = frac(uv);
                weight = weight * weight * (3.0 - 2.0 * weight);
                float bottom = lerp(PosterNoiseHash(cell),
                    PosterNoiseHash(cell + float2(1, 0)), weight.x);
                float top = lerp(PosterNoiseHash(cell + float2(0, 1)),
                    PosterNoiseHash(cell + float2(1, 1)), weight.x);
                return lerp(bottom, top, weight.y);
            }

            float PosterSimpleNoise(float2 uv)
            {
                uv *= _DisolveNoiseScale;
                return PosterValueNoise(uv) * 0.125
                    + PosterValueNoise(uv * 0.5) * 0.25
                    + PosterValueNoise(uv * 0.25) * 0.5;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_DisolveAlpha <= 0.0)
                    clip(-1.0);
                if (_DisolveAlpha < 1.0)
                {
                    float2 screenUV = i.screenPosition.xy / i.screenPosition.w;
                    clip(_DisolveAlpha - PosterSimpleNoise(screenUV));
                }

                float sdf =
                    SampleSDF(i.uv);

                /*
                 * Main shape
                 *
                 * SDF:
                 *
                 * 0.0 = outside
                 * 0.5 = contour
                 * 1.0 = inside
                 */

                float alpha =
                    SDFAlpha(
                        sdf,
                        _Threshold,
                        _Softness
                    );

                /*
                 * Outline.
                 *
                 * We extend the shape towards
                 * the outside of the SDF.
                 */

                float outlineEdge =
                    _Threshold -
                    _OutlineWidth;

                float outline =
                    SDFAlpha(
                        sdf,
                        outlineEdge,
                        _Softness
                    );

                float outlineOnly =
                    saturate(
                        outline - alpha
                    );

                fixed4 result =
                    _OutlineColor * outlineOnly;

                result =
                    lerp(
                        result,
                        _Color,
                        alpha
                    );

                result.a =
                    max(
                        result.a,
                        max(
                            outlineOnly *
                            _OutlineColor.a,

                            alpha *
                            _Color.a
                        )
                    );

                result *= i.color;

                return result;
            }

            ENDCG
        }
    }
}
