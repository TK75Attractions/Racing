Shader "Racing/LightningBolt"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // LineRenderer の幅方向（uv.y）だけを使い、白く飽和した芯と色付きの光の縁を作ります。
                half across = abs(input.uv.y * 2.0h - 1.0h);
                half core = saturate(1.0h - across * 3.0h);
                core *= core;
                half halo = saturate(1.0h - across);
                halo *= halo;
                half3 rgb = input.color.rgb * halo * 2.5h + core * 4.0h;
                return half4(rgb, input.color.a * saturate(halo + core));
            }
            ENDHLSL
        }
    }
}
