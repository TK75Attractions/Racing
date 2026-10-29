Shader "Racing/TubeSpeedPatterns"
{
    Properties
    {
        [HDR] _Cyan ("Cyan", Color) = (0.04, 0.8, 1, 1)
        [HDR] _Violet ("Violet", Color) = (0.6, 0.16, 1, 1)
        _Intensity ("Glow Intensity", Range(0, 6)) = 1.5
        _TubeLength ("Tube Length (meters)", Float) = 1000
        _FlowSpeed ("Light Flow Speed (meters/second)", Range(0, 40)) = 14
        _SurfaceOffset ("Inner Surface Offset (meters)", Range(0.005, 0.1)) = 0.03
        [Toggle] _ShowRibs ("Debug: Ribs", Float) = 1
        [Toggle] _ShowBands ("Debug: Wall Flow Bands", Float) = 1
        [Toggle] _ShowArrows ("Debug: Low Arrows", Float) = 1
        [Toggle] _ShowLabels ("Debug: Section Labels", Float) = 1
        [Toggle] _ShowOuter ("Debug: Outer Wall Emphasis", Float) = 1
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
                float _ShowRibs, _ShowBands, _ShowArrows, _ShowLabels, _ShowOuter;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 wall : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float4 wall : TEXCOORD2; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Overlay normals face outward; offset in world metres toward the interior.
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
                float ribDistance = abs(frac(s / 20.0 + .5) - .5) * 20.0;
                float ribs = Stroke(ribDistance, .055) * _ShowRibs;
                float entry = 1.0 - smoothstep(0.0, 65.0, s);
                float exit = 1.0 - smoothstep(0.0, 90.0, _TubeLength - s);
                float bend = saturate(input.wall.w), outer = saturate(input.wall.y);
                // s increases in race direction: subtract time to move toward the exit.
                float flow = .35 + .65 * pow(.5 + .5 * sin((s - _Time.y * _FlowSpeed) * (6.2831853 / 48.0)), 3.0);
                float rhythm = .72 + .12 * entry + .18 * exit;
                float height = (v < .72 ? .56 - v : v - .882) * 94.25;
                // v=.22 is the ceiling, v=.72 the floor. Nothing except thin ribs
                // is allowed outside this side-wall strip (including AA fringes).
                float wallMask = smoothstep(-2.0, -1.5, height) * (1.0 - smoothstep(8.5, 9.0, height));
                float bands = Stroke(height - 3.5, .16) * wallMask * _ShowBands;
                // Curvature changes the existing band, never adds another layer.
                // Only the outside wall exceeds the straight-section brightness.
                float emphasis = lerp(1.0, .65 + .85 * outer, bend * _ShowOuter);
                int section = min(99, (int)floor(s / 100.0) + 1);
                float labelX = (s - (floor(s / 100.0) * 100.0 + 28.0)) * (v < .72 ? 1.0 : -1.0);
                float labelY = height - 4.5;
                float label = 0;
                float aa = max(max(fwidth(s), fwidth(height)), .018);
                // One large label per section, on one wall. In its longitudinal
                // window both flow bands and arrows yield to the number.
                float labelWindow = (1.0 - smoothstep(5.0, 8.0, abs(labelX))) * _ShowLabels;
                if (v < .72 && abs(labelX) < 5.0 && abs(labelY) < 2.0)
                {
                    float2 p = float2(labelX, labelY);
                    label = max(Bar(p, float2(-2.8, 1.5), float2(.8, .13), aa), Bar(p, float2(-2.8, 0), float2(.13, 1.5), aa));
                    if (section >= 10) label = max(label, Digit(p - float2(-.6, 0), section / 10, aa));
                    label = max(label, Digit(p - float2(section >= 10 ? 1.6 : -.4, 0), section % 10, aa));
                }
                label *= wallMask * _ShowLabels;
                float arrows = chevrons * wallMask * _ShowArrows;
                float route = (bands * flow * emphasis + arrows * (.8 + .2 * flow)) * rhythm * (1.0 - labelWindow);
                float visibility = 1.0 - smoothstep(70.0, 150.0, distance(GetCameraPositionWS(), input.positionWS));
                float detail = 1.0 - smoothstep(.4, 1.6, fwidth(s));
                half3 wallColor = lerp(_Cyan.rgb, _Violet.rgb, smoothstep(.6, .85, v));
                half3 warm = half3(1.0, .46, .16);
                half3 routeColor = lerp(wallColor, warm, saturate(entry * .35 + exit * .55));
                // At most two leading layers: bands + low arrows, or a label.
                // Ribs remain a faint architectural line, including on the ceiling.
                half3 color = routeColor * route + wallColor * ribs * .055 * (1.0 - labelWindow) +
                    lerp(wallColor, warm, .2) * label * .85;
                // Emission-only wall overlay: no dark alpha-only grid underneath.
                return half4(color * (_Intensity * visibility * detail), 0);
            }
            ENDHLSL
        }
    }
}
