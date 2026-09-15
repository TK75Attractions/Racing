Shader "Racing/UI/LogoWhiteKey"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            sampler2D _MainTex;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // Key only neutral near-white; retain blue highlights and metallic details.
                float neutralWhite = min(c.r, min(c.g, c.b));
                c.a *= 1 - smoothstep(0.88, 0.985, neutralWhite);
                c.rgb = max(c.rgb, fixed3(0.18, 0.22, 0.27));
                return c * i.color;
            }
            ENDCG
        }
    }
}
