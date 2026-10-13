Shader "Racing/CarSelectionNeon"
{
    Properties { _Color ("Color", Color) = (0, 0.8, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Attributes { float4 position : POSITION; };
            struct Varyings { float4 position : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
