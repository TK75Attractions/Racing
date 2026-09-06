Shader "Racing/Environment Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.08, 0.36, 0.75, 1)
        _HorizonColor ("Horizon", Color) = (0.65, 0.8, 0.95, 1)
        _CloudCover ("Cloud cover", Range(0,1)) = 0.12
        _Night ("Night", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _TopColor, _HorizonColor;
            float _CloudCover, _Night;
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.position = UnityObjectToClipPos(vertex);
                o.direction = vertex.xyz;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float3 color = lerp(_HorizonColor.rgb, _TopColor.rgb, saturate(d.y));
                float2 p = d.xz / max(0.2, d.y + 0.3) * 3 + _Time.y * 0.003;
                float n = noise(p)*0.57 + noise(p*2.1)*0.28 + noise(p*4.3)*0.15;
                float clouds = smoothstep(1-_CloudCover, 1.15-_CloudCover, n) * smoothstep(-0.05, 0.2, d.y);
                color = lerp(color, lerp(float3(0.76,0.79,0.82),float3(0.055,0.065,0.09),_Night), clouds);
                return float4(color,1);
            }
            ENDHLSL
        }
    }
}
