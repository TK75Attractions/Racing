Shader "Racing/NeonAndon"
{
    Properties
    {
        _ColorBottom ("Bottom Color", Color) = (1, 0.18, 0.55, 1)
        _ColorTop ("Top Color", Color) = (0.12, 0.85, 1, 1)
        _Intensity ("Intensity", Range(0, 12)) = 1.6
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimBoost ("Rim Boost", Range(0, 4)) = 1.4
        _Flow ("Flow", Range(0, 1)) = 0.15
        _FlowSpeed ("Flow Speed", Range(0, 4)) = 0.6
        _FlowScale ("Flow Scale", Range(0.5, 8)) = 2
        _Alpha ("Alpha", Range(0, 1)) = 0.93
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            // 障子紙は面として濃度を持つため、奥行きは書き込んで枠が透けすぎないようにします。
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorBottom;
                half4 _ColorTop;
                half _Intensity;
                half _RimPower;
                half _RimBoost;
                half _Flow;
                half _FlowSpeed;
                half _FlowScale;
                half _Alpha;
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
                // 真横から見た面ほど 0 に近づき、縁の光り方に使います。
                output.facing = saturate(abs(dot(normalWS, viewWS)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 下から上へ2色のネオンをつなぎます。ネオン管には同じ色を2つ渡して単色にします。
                half3 color = lerp(_ColorBottom.rgb, _ColorTop.rgb, smoothstep(0.0h, 1.0h, input.uv.y));

                // 紙の内側を光がゆっくり上る演出。振れ幅は平均が変わらないように ±_Flow に収めます。
                half wave = sin((input.uv.y * _FlowScale - _Time.y * _FlowSpeed) * 6.2831853h);
                color *= 1.0h + _Flow * wave;

                // 縁の立ち上がりで、紙越しに光が透けている感じを出します。
                half rim = pow(1.0h - input.facing, _RimPower);
                color += color * rim * _RimBoost;

                half alpha = saturate(_Alpha + rim * (1.0h - _Alpha)) * _ColorBottom.a;
                return half4(color * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
