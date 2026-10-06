Shader "Racing/UI/TutorialReferenceLogo"
{
    Properties { [PerRendererData] _MainTex ("Reference", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
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
            sampler2D _MainTex; fixed4 _Color;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c=tex2D(_MainTex,i.uv);
                // Within the tightly framed logo, preserve white brushwork and hot pink ink;
                // the dark photographed scenery remains fully transparent over the live course.
                float white=smoothstep(.22,.50,min(c.r,min(c.g,c.b)));
                float pink=smoothstep(.35,.70,c.r-max(c.g,c.b))*smoothstep(.40,.68,c.r);
                c.a*=max(white,pink);
                return c*i.color;
            }
            ENDCG
        }
    }
}
