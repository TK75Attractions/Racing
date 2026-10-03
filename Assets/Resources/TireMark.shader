Shader "Racing/TireMark"
{
    Properties
    {
        _Color ("Mark", Color) = (0.025, 0.025, 0.03, 0.72)
    }
    SubShader
    {
        // 路面より後、他の半透明より先に描き、煙や火花の下に来るようにします。
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // 路面とのちらつきを防ぐため、深度をわずかに手前へずらします。
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // x=幅方向（0〜1）。端をぼかし、タイヤの溝のような細い縞を入れます。
                float across = input.uv.x;
                float edge = abs(across * 2.0 - 1.0);
                half soft = 1.0h - edge * edge * edge * edge;
                half grooves = 0.82h + 0.18h * frac(sin(floor(across * 9.0) * 91.7) * 437.58);
                half alpha = _Color.a * input.color.a * soft * grooves;
                half3 color = MixFog(_Color.rgb, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
