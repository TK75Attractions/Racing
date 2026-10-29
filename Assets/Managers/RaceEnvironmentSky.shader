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
                if (_Night > 0.0)
                {
                    // Fixed celestial directions: no swimming as the camera moves.
                    float2 starUV = float2(atan2(d.z, d.x) / 6.2831853 + 0.5,
                        asin(clamp(d.y, -1.0, 1.0)) / 3.1415927 + 0.5) * float2(640,320);
                    float2 cell = floor(starUV);
                    float seed = hash(cell);
                    float2 offset = float2(hash(cell + 17.1), hash(cell + 43.7)) * 0.6 + 0.2;
                    float star = (1.0 - smoothstep(0.015, 0.10, length(frac(starUV) - offset)))
                        * step(0.987, seed) * smoothstep(0.02, 0.20, d.y);
                    float angle = acos(clamp(dot(d, normalize(float3(-0.45,0.42,-0.78))), -1.0, 1.0));
                    float moon = 1.0 - smoothstep(0.032, 0.036, angle);
                    float halo = exp(-angle * angle / 0.009) * 0.18;
                    float visibility = _Night * (1.0 - clouds) * lerp(1.0, 0.2, _CloudCover);
                    color += visibility * (star * float3(0.75,0.83,1.0) * 1.8
                        + (moon * 1.8 + halo) * float3(0.82,0.88,1.0));
                }
                return float4(color,1);
            }
            ENDHLSL
        }
    }
}
