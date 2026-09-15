Shader "Racing/Race Barrier Hex Frost"
{
    Properties
    {
        _GridMask ("Hex grid mask (alpha)", 2D) = "white" {}
        [HDR] _GridColor ("Grid color", Color) = (0.15, 0.85, 1, 1)
        _GridBrightness ("Grid brightness", Range(0, 4)) = 1.4
        _GridOpacity ("Grid opacity", Range(0, 1)) = 0.96
        _CellTint ("Frost tint", Color) = (0.45, 0.75, 0.85, 1)
        _CellTintAmount ("Frost tint amount", Range(0, 1)) = 0.14
        _CellOpacity ("Frost opacity", Range(0, 1)) = 0.88
        _BlurRadius ("Background blur (pixels)", Range(0, 8)) = 3
        _GridTiling ("Grid tiling", Range(0.02, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "RaceBarrierFrost"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_GridMask);
            SAMPLER(sampler_GridMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _GridColor;
                half4 _CellTint;
                half _GridBrightness;
                half _GridOpacity;
                half _CellTintAmount;
                half _CellOpacity;
                float _BlurRadius;
                float _GridTiling;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                output.uv = input.uv;
                return output;
            }

            half3 BlurredOpaqueScene(float2 uv)
            {
                float2 offset = _CameraOpaqueTexture_TexelSize.xy * _BlurRadius;
                half3 color = SampleSceneColor(uv) * 4.0h;
                color += SampleSceneColor(uv + float2(offset.x, 0)) * 2.0h;
                color += SampleSceneColor(uv - float2(offset.x, 0)) * 2.0h;
                color += SampleSceneColor(uv + float2(0, offset.y)) * 2.0h;
                color += SampleSceneColor(uv - float2(0, offset.y)) * 2.0h;
                color += SampleSceneColor(uv + offset);
                color += SampleSceneColor(uv - offset);
                color += SampleSceneColor(uv + float2(offset.x, -offset.y));
                color += SampleSceneColor(uv + float2(-offset.x, offset.y));
                return color * (1.0h / 16.0h);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UVは壁の実距離で生成しているため、範囲外も明示的に反復する。
                // サンプラー設定に依存しないので、長い壁の中央でもグリッドが消えない。
                float2 gridUV = frac(input.uv * _GridTiling);
                half hexLineMask = SAMPLE_TEXTURE2D(_GridMask, sampler_GridMask, gridUV).a;
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                half3 frosted = BlurredOpaqueScene(screenUV);
                frosted = lerp(frosted, _CellTint.rgb, _CellTintAmount);
                half3 color = lerp(frosted, _GridColor.rgb * _GridBrightness, hexLineMask);
                half alpha = lerp(_CellOpacity, _GridOpacity, hexLineMask);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
