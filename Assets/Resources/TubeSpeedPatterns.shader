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
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Inner Wall Lights"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Cyan, _Violet;
                float _Intensity, _TubeLength, _FlowSpeed, _SurfaceOffset;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // The FBX normals face outward. Offset in world meters toward the interior.
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) -
                    TransformObjectToWorldNormal(input.normalOS) * _SurfaceOffset;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            float Stroke(float distance, float halfWidth)
            {
                float aa = max(fwidth(distance), 0.012);
                return 1.0 - smoothstep(halfWidth, halfWidth + aa, abs(distance));
            }
            float Rail(float profile, float a, float b)
            {
                float d = min(abs(profile - a), abs(profile - b)) * 94.25;
                return Stroke(d, 0.07) + 0.14 * Stroke(d, 0.32);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float s = input.uv.x * _TubeLength;
                float v = input.uv.y;
                // These bands sit on the two walls above the road, away from the floor.
                float side = abs(v - 0.56) < abs(v - 0.882) ? v - 0.56 : v - 0.882;
                float y = side * 94.25;
                float cell = (frac((s + 3.0) / 12.0) - 0.5) * 12.0;
                float chevronDistance = cell + abs(y) * 1.25;
                float chevrons = max(Stroke(chevronDistance, 0.12), Stroke(chevronDistance + 1.1, 0.12)) *
                    (1.0 - smoothstep(1.0, 1.25, abs(y)));
                float rails = Rail(v, 0.529, 0.912) + 0.65 * Rail(v, 0.588, 0.853);

                float ribDistance = abs(frac(s / 20.0 + 0.5) - 0.5) * 20.0;
                float upperWall = 1.0 - smoothstep(0.595, 0.615, v) + smoothstep(0.835, 0.855, v);
                float ribs = (Stroke(ribDistance, 0.06) + 0.12 * Stroke(ribDistance, 0.35)) * upperWall;
                // A broad smooth packet travels toward the approaching car. No hard flashing.
                float phase = (s + _Time.y * _FlowSpeed) * (6.2831853 / 48.0);
                float flow = 0.24 + 0.76 * pow(0.5 + 0.5 * sin(phase), 3.0);
                float visibility = 1.0 - smoothstep(70.0, 150.0, distance(GetCameraPositionWS(), input.positionWS));
                // Fade tiny strokes at a distance instead of letting them alias into flashing lines.
                float detail = 1.0 - smoothstep(0.4, 1.6, fwidth(s));
                half3 wallColor = lerp(_Cyan.rgb, _Violet.rgb, smoothstep(0.6, 0.85, v));
                half3 color = wallColor * (rails * (0.35 + 0.65 * flow) + chevrons * (0.55 + 0.45 * flow)) +
                    lerp(_Cyan.rgb, _Violet.rgb, 0.35) * ribs * 0.42;
                return half4(color * (_Intensity * visibility * detail), 0.0);
            }
            ENDHLSL
        }
    }
}
