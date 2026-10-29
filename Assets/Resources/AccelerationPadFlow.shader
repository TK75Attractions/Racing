Shader "Racing/AccelerationPadFlow"
{
 Properties{_PreviewTime("Preview time",Float)=-1}
 SubShader{Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+2"}
 Pass{ZWrite Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _PreviewTime;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
 V Vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
 half4 Frag(V i):SV_Target{
 float t=_PreviewTime>=0?_PreviewTime:_Time.y;float x=abs(i.uv.x);
 float cell=abs(frac((i.uv.y-t*4.0+x*1.5)/2.8+.5)-.5)*2.8;
 float aa=max(fwidth(cell),.02);float arrow=(1-smoothstep(.16-aa,.16+aa,cell))*(1-smoothstep(.75,.86,x));
 half3 c=lerp(half3(.025,.15,.20),half3(.45,1.7,2.2),arrow);
 c=lerp(c,half3(.1,1.1,1.65),smoothstep(.87,.99,x));return half4(c,1);}
 ENDHLSL
 }}
}
