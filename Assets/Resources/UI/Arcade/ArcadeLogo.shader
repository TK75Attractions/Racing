Shader "Racing/Arcade Logo Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo",2D)="white" {}
        _Color ("Tint",Color)=(1,1,1,1)
        _StencilComp ("Stencil Comparison",Float)=8
        _Stencil ("Stencil ID",Float)=0
        _StencilOp ("Stencil Operation",Float)=0
        _StencilWriteMask ("Stencil Write Mask",Float)=255
        _StencilReadMask ("Stencil Read Mask",Float)=255
        _ColorMask ("Color Mask",Float)=15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True"}
        Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;};
            sampler2D _MainTex;float4 _MainTex_TexelSize;fixed4 _Color;float4 _ClipRect;
            v2f vert(appdata v){v2f o;o.local=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o;}
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 original=tex2D(_MainTex,i.uv);float2 px=_MainTex_TexelSize.xy*5;
                float edge=0;
                edge=max(edge,tex2D(_MainTex,i.uv+float2(px.x,0)).a);edge=max(edge,tex2D(_MainTex,i.uv-float2(px.x,0)).a);
                edge=max(edge,tex2D(_MainTex,i.uv+float2(0,px.y)).a);edge=max(edge,tex2D(_MainTex,i.uv-float2(0,px.y)).a);
                edge=max(edge,tex2D(_MainTex,i.uv+px*.7).a);edge=max(edge,tex2D(_MainTex,i.uv-px*.7).a);
                edge=max(edge,tex2D(_MainTex,i.uv+float2(px.x,-px.y)*.7).a);edge=max(edge,tex2D(_MainTex,i.uv+float2(-px.x,px.y)*.7).a);
                fixed4 result=fixed4(lerp(fixed3(.95,.97,1),original.rgb,original.a),max(original.a,edge))*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
