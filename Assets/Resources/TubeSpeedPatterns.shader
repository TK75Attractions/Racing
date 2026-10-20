Shader "Racing/TubeSpeedPatterns"
{
    Properties
    {
        [HDR] _Cyan ("Cyan", Color) = (0.04, 0.8, 1, 1)
        [HDR] _Violet ("Violet", Color) = (0.6, 0.16, 1, 1)
        _Intensity ("Glow Intensity", Range(0, 6)) = 2.4
        _TubeLength ("Tube Length (meters)", Float) = 1000
        _FlowSpeed ("Light Flow Speed (meters/second)", Range(0, 40)) = 14
        _SurfaceOffset ("Inner Surface Offset (meters)", Range(0.005, 0.1)) = 0.03
        [HideInInspector] _PortalMode ("Portal vertex colors", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Inner Wall Lights"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Cyan, _Violet;
                float _Intensity, _TubeLength, _FlowSpeed, _SurfaceOffset, _PortalMode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 wall : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float4 wall : TEXCOORD2; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // FBX normals face outward; offset in world metres toward the interior.
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) - TransformObjectToWorldNormal(input.normalOS) * _SurfaceOffset;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.wall = input.wall;
                output.color = input.color;
                return output;
            }
            float Stroke(float d, float width)
            {
                return 1.0 - smoothstep(width, width + max(fwidth(d), .012), abs(d));
            }
            float Rail(float v, float a, float b)
            {
                float d = min(abs(v - a), abs(v - b)) * 94.25;
                return Stroke(d, .07) + .14 * Stroke(d, .32);
            }
            float Bar(float2 p, float2 center, float2 size, float aa)
            {
                float2 d = abs(p - center) - size;
                return 1.0 - smoothstep(0.0, aa, max(d.x, d.y));
            }
            // Seven-segment digits, no font atlas or extra wall geometry.
            float Digit(float2 p, int digit, float aa)
            {
                float mask = digit == 0 ? 119 : digit == 1 ? 36 : digit == 2 ? 93 : digit == 3 ? 109 :
                    digit == 4 ? 46 : digit == 5 ? 107 : digit == 6 ? 123 : digit == 7 ? 37 : digit == 8 ? 127 : 111;
                float value = 0;
                if (fmod(floor(mask / 1.0), 2.0) > .5) value = max(value, Bar(p, float2(0, 1.5), float2(.65, .12), aa));
                if (fmod(floor(mask / 2.0), 2.0) > .5) value = max(value, Bar(p, float2(-.65, .75), float2(.12, .75), aa));
                if (fmod(floor(mask / 4.0), 2.0) > .5) value = max(value, Bar(p, float2(.65, .75), float2(.12, .75), aa));
                if (fmod(floor(mask / 8.0), 2.0) > .5) value = max(value, Bar(p, float2(0, 0), float2(.65, .12), aa));
                if (fmod(floor(mask / 16.0), 2.0) > .5) value = max(value, Bar(p, float2(-.65, -.75), float2(.12, .75), aa));
                if (fmod(floor(mask / 32.0), 2.0) > .5) value = max(value, Bar(p, float2(.65, -.75), float2(.12, .75), aa));
                if (fmod(floor(mask / 64.0), 2.0) > .5) value = max(value, Bar(p, float2(0, -1.5), float2(.65, .12), aa));
                return value;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_PortalMode > .5)
                {
                    // One opaque, unlit draw for frame, neon trim, reflective stripes and approach lamps.
                    // Alpha marks the silver reflector faces for a restrained view-dependent sheen.
                    float sheen = pow(saturate(abs(dot(normalize(GetCameraPositionWS() - input.positionWS),
                        normalize(cross(ddx(input.positionWS), ddy(input.positionWS)))))), 8.0);
                    return half4(input.color.rgb * (1.0 + input.color.a * sheen * .35), 1);
                }
                float s = input.wall.x; // true arc distance from entry
                float v = input.uv.y;
                float side = abs(v - .56) < abs(v - .882) ? v - .56 : v - .882;
                float y = side * 94.25;
                float cell = (frac((s + 3.0) / 12.0) - .5) * 12.0;
                float chevronDistance = -cell + abs(y) * 1.25;
                float chevrons = max(Stroke(chevronDistance, .12), Stroke(chevronDistance + 1.1, .12)) *
                    (1.0 - smoothstep(1.0, 1.25, abs(y)));
                float rails = Rail(v, .529, .912) + .65 * Rail(v, .588, .853);
                float wallMask = 1.0 - smoothstep(.595, .615, v) + smoothstep(.835, .855, v);
                float ribDistance = abs(frac(s / 20.0 + .5) - .5) * 20.0;
                float ribs = Stroke(ribDistance, .16);
                float ribEdge = Stroke(ribDistance - .25, .055);
                float seamS = abs(frac(s / 5.0 + .5) - .5) * 5.0;
                float seamV = abs(frac(v * 16.0 + .5) - .5) * (94.25 / 16.0);
                float seams = max(Stroke(seamS, .035), Stroke(seamV, .035)) * wallMask;
                float entry = 1.0 - smoothstep(0.0, 65.0, s);
                float exit = 1.0 - smoothstep(0.0, 90.0, _TubeLength - s);
                float bend = saturate(input.wall.w), outer = saturate(input.wall.y);
                float phase = (s + _Time.y * _FlowSpeed) * (6.2831853 / 48.0);
                float flow = .24 + .76 * pow(.5 + .5 * sin(phase), 3.0);
                float rhythm = .62 + .22 * entry + .42 * exit;
                float route = rails * (.65 + .35 * flow * (1.0 - .6 * bend)) + chevrons * (.7 + .3 * flow);
                float outerLight = outer * (.08 + .12 * Stroke(cell, .4)) * wallMask;
                float height = (v < .72 ? .56 - v : v - .882) * 94.25;
                // Left/right turn glyphs in the same reading frame as the section labels.
                float turnX = cell * (v < .72 ? 1.0 : -1.0);
                float turnArrow = Stroke(-turnX * sign(input.wall.z) + abs(height - 3.5) * 1.4, .13) *
                    (1.0 - smoothstep(.9, 1.2, abs(height - 3.5))) * outer;
                int section = min(99, (int)floor(s / 100.0) + 1);
                float labelX = (s - (floor(s / 100.0) * 100.0 + 28.0)) * (v < .72 ? 1.0 : -1.0);
                float labelY = height - 7.0;
                float label = 0;
                float aa = max(max(fwidth(s), fwidth(height)), .018);
                if (abs(labelX) < 6.0 && abs(labelY) < 2.0)
                {
                    float2 p = float2(labelX, labelY);
                    label = max(Bar(p, float2(-2.8, 1.5), float2(.8, .13), aa), Bar(p, float2(-2.8, 0), float2(.13, 1.5), aa));
                    if (section >= 10) label = max(label, Digit(p - float2(-.6, 0), section / 10, aa));
                    label = max(label, Digit(p - float2(section >= 10 ? 1.6 : -.4, 0), section % 10, aa));
                }
                float visibility = 1.0 - smoothstep(70.0, 150.0, distance(GetCameraPositionWS(), input.positionWS));
                float detail = 1.0 - smoothstep(.4, 1.6, fwidth(s));
                half3 wallColor = lerp(_Cyan.rgb, _Violet.rgb, smoothstep(.6, .85, v));
                half3 warm = half3(1.0, .46, .16);
                half3 routeColor = lerp(wallColor, warm, saturate(entry * .35 + exit * .55));
                // Architecture stays below route rails/arrows in intensity.
                half3 color = routeColor * (route * rhythm + turnArrow * .24) + wallColor * (ribEdge * .1 + outerLight) +
                    lerp(wallColor, warm, .35) * label * .24;
                float alpha = saturate(ribs * .65 + seams * .28) * visibility * detail;
                return half4(color * (_Intensity * visibility * detail), alpha);
            }
            ENDHLSL
        }
    }
}
