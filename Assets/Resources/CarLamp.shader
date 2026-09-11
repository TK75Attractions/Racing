Shader "Racing/CarLamp"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half facing : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(position.positionWS));
                output.positionCS = position.positionCS;
                output.uv = input.uv;
                // 板の裏表を問わないため絶対値を取り、真横から見たときだけ減光します。
                output.facing = saturate(abs(dot(normalWS, viewWS)));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 offset = input.uv * 2.0 - 1.0;
                half glow = saturate(1.0 - dot(offset, offset));
                glow *= glow;
                half strength = _Intensity * lerp(0.3h, 1.0h, input.facing);
                return half4(_Color.rgb * strength, glow * _Color.a * saturate(strength));
            }
            ENDHLSL
        }
    }
}
