Shader "Racing/TireSmoke"
{
    Properties
    {
        _Color ("Smoke", Color) = (0.86, 0.86, 0.88, 1)
        _LightInfluence ("Main Light Influence", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _LightInfluence;
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
                float2 p = input.uv * 2.0 - 1.0;
                // 円に低周波のゆらぎを重ね、輪郭が丸すぎない煙の塊にします。
                float wobble = sin(p.x * 4.1 + p.y * 2.3) * sin(p.y * 3.7 - p.x * 1.9);
                float radius = length(p) * (1.0 + 0.12 * wobble);
                half puff = saturate(1.0 - radius);
                puff = puff * puff * (3.0h - 2.0h * puff);

                // 夜は暗く、昼は明るく見えるよう、環境光と太陽光の色で照らします。
                half3 lighting = saturate(unity_AmbientSky.rgb + _MainLightColor.rgb * _LightInfluence);
                half3 color = _Color.rgb * input.color.rgb * lighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, input.color.a * puff);
            }
            ENDHLSL
        }
    }
}
