Shader "Racing/Environment Solid"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 position : POSITION; half4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; half4 color : COLOR; float fog : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.position = TransformObjectToHClip(i.position.xyz);
                o.color = i.color;
                o.fog = ComputeFogFactor(o.position.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(MixFog(i.color.rgb, i.fog), 1); }
            ENDHLSL
        }
    }
}
