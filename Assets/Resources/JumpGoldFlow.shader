Shader "Racing/JumpGoldFlow"
{
    Properties
    {
        _Direction ("Race direction", Float) = 1
        _PreviewTime ("Preview time (-1 uses game time)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+2" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Direction, _PreviewTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 approach : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 approach : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.approach = input.approach; return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.approach.x * _Direction);
                float time = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                float s = input.uv.y * _Direction;
                float x = abs(input.uv.x);
                // The arms trail the centre tip; phase translation moves the entire arrow toward takeoff.
                float cell = abs(frac((s - time * 5.0 + x * 3.0) / 5.5 + .5) - .5) * 5.5;
                float aa = max(fwidth(cell), .025);
                float arrow = 1 - smoothstep(.22 - aa, .22 + aa, cell);
                float rim = 1 - smoothstep(.40 - aa, .40 + aa, cell);
                float lane = 1 - smoothstep(.76, .85, x);
                float sheen = pow(.5 + .5 * sin(s * .35 - time * 1.6), 8);
                half3 gold = half3(1.35, .64, .055) * (1 + .28 * sheen);
                gold = lerp(gold, half3(.11, .055, .012), rim * lane);
                gold = lerp(gold, half3(2.1, 1.75, .65), arrow * lane);
                float edge = smoothstep(.86, .98, x);
                gold = lerp(gold, half3(2.0, 1.24, .18), edge);
                return half4(gold, 1);
            }
            ENDHLSL
        }
    }
}
