Shader "Racing/SakuraPetal"
{
    Properties
    {
        _Color ("Petal", Color) = (1, 0.78, 0.88, 1)
        _Intensity ("Glow", Range(0, 8)) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
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
                // 色は頂点ストリームに頼らずマテリアルから受け取ります。xy=UV、z=向きによる明暗。
                float3 uvShade : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(position.positionWS));
                // 回転して裏返るたびに明るさが変わり、ひらひら舞う印象になります。
                float shade = lerp(0.72, 1.0, saturate(abs(dot(normalWS, viewWS))));
                output.positionCS = position.positionCS;
                output.uvShade = float3(input.uv, shade);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uvShade.xy * 2.0 - 1.0;
                float top = saturate(p.y * 0.5 + 0.5);
                // 上ほど幅が広い楕円から、先端の切れ込みを丸く削って桜の花びらの形にします。
                float2 shaped = p / float2(0.62 + 0.34 * top, 1.0);
                float body = 1.0 - length(shaped);
                float notch = length(p - float2(0.0, 1.12)) - 0.42;
                float alpha = saturate(min(body, notch) * 12.0);
                clip(alpha - 0.01);

                // 1を超える明るさを返すことで、暗いシーンでもBloomがのって淡く光ります。
                half3 color = _Color.rgb * _Intensity * input.uvShade.z * lerp(0.9h, 1.12h, top);
                return half4(color, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
