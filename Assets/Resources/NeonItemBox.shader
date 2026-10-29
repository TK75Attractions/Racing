Shader "Racing/Neon Item Box"
{
    Properties
    {
        [HDR] _HueColor ("Cycling hue", Color) = (1, 0.1, 0.8, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "NeonItemBox"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HueColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 rgb = lerp(half3(1, 1, 1), _HueColor.rgb, input.color.r);
                // HDR color makes the frame and glyph read as self-lit without adding realtime lights.
                rgb *= lerp(2.2h, 1.65h, input.color.r);
                return half4(rgb, input.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
