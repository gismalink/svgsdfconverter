//codex-agent
Shader "SVG SDF/SDF"
{
    Properties
    {
        [NoScaleOffset]
        _MainTex ("SDF Texture", 2D) = "white" {}

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
            };

            sampler2D _MainTex;

            float4 _MainTex_ST;

            fixed4 _Color;

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

            fixed4 frag(v2f i) : SV_Target
            {
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
