Shader "Racing/Night Skyline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "RenderType"="TransparentCutout" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.position = TransformObjectToHClip(input.position.xyz);
                // This is a distant backdrop. Keep it inside any gameplay camera far plane.
                #if UNITY_REVERSED_Z
                    o.position.z = max(o.position.z, 0.00001 * o.position.w);
                #else
                    o.position.z = min(o.position.z, 0.99999 * o.position.w);
                #endif
                o.uv = input.uv;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            half4 frag(Varyings input) : SV_Target
            {
                float a = input.uv.x * 6.2831853;
                float y = input.uv.y;
                // Periodic harmonics join without a seam; ridge sits 15-60 m above the course minimum.
                float ridge = 116 + 15*sin(a*3+0.4) + 10*sin(a*7) + 5*sin(a*17+1.2);
                float building = floor(input.uv.x * 260);
                float roof = 85 + hash(float2(building, 7)) * 24;
                clip(max(ridge, roof) - y);
                float city = step(y, roof);
                float2 grid = float2(input.uv.x * 1300, y / 2.2);
                float2 cell = floor(grid), f = frac(grid);
                float window = step(0.82, hash(cell)) * step(0.30,f.x) * step(f.x,0.65)
                    * step(0.25,f.y) * step(f.y,0.57) * step(80,y) * city;
                // Unlit, with no fog variant: the ridge stays legible in rainy weather.
                float3 color = lerp(float3(0.012,0.014,0.028),float3(0.006,0.008,0.017),city);
                color += window * float3(2.0,1.05,0.34);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
