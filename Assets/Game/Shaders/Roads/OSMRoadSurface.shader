Shader "City/Asphalt With Lane Markings"
{
    Properties
    {
        _BaseMap("Asphalt", 2D) = "white" {}
        _DetailAtlas("Wear Atlas (RGBA)", 2D) = "black" {}
        _WearStrength("Wear Strength", Range(0,1)) = 0.8
        _DetailSpacing("Wear Spacing (m)", Float) = 7
        _BaseColor("Asphalt Tint", Color) = (1,1,1,1)
        _PaintColor("Lane Paint", Color) = (0.82,0.81,0.74,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.12
        _LineWidth("Line Width (m)", Float) = 0.12
        _DashLength("Dash Length (m)", Float) = 3
        _DashGap("Dash Gap (m)", Float) = 3
        _JunctionClearance("End Clearance (m)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DetailAtlas); SAMPLER(sampler_DetailAtlas);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _PaintColor;
            half _Smoothness;
            float _LineWidth, _DashLength, _DashGap, _JunctionClearance;
            float _WearStrength, _DetailSpacing;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 layout : TEXCOORD1; // width, lanes, median width, one way
            float4 limits : TEXCOORD2; // chunk length, markings enabled
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float4 layout : TEXCOORD3;
            float2 limits : TEXCOORD4;
            half fog : TEXCOORD5;
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            output.layout = input.layout;
            output.limits = input.limits.xy;
            output.fog = ComputeFogFactor(position.positionCS.z);
            return output;
        }
        float Stripe(float x, float center, float width, float aa)
        {
            return 1.0 - smoothstep(width * 0.5 - aa, width * 0.5 + aa, abs(x - center));
        }
        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }
        half4 RoadWear(float2 world)
        {
            float spacing = max(_DetailSpacing, 3);
            float2 cell = floor(world / spacing);
            float2 local = frac(world / spacing) * spacing;
            float seed = Hash(cell);
            float2 center = (0.28 + 0.44 * float2(Hash(cell + 3.7), Hash(cell + 9.2))) * spacing;
            float2 p = local - center;
            float angle = seed * 6.283185;
            float sn, cs; sincos(angle, sn, cs);
            p = float2(cs * p.x - sn * p.y, sn * p.x + cs * p.y);
            float kind = floor(Hash(cell + 21.3) * 4);
            float size = kind == 2 ? 0.86 : lerp(1.1, 2.5, Hash(cell + 8.1));
            float2 uv = p / size + 0.5;
            float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
            float2 tile = float2(fmod(kind, 2), floor(kind / 2));
            // Atlas padding prevents adjacent details bleeding through mip levels.
            float2 atlasUV = (tile + lerp(0.02, 0.98, saturate(uv))) * 0.5;
            half4 detail = SAMPLE_TEXTURE2D(_DetailAtlas, sampler_DetailAtlas, atlasUV);
            detail.a *= inside * step(0.34, seed) * _WearStrength;
            return detail;
        }
        half Marking(Varyings input)
        {
            if (input.limits.y < 0.5 || input.layout.x < 0.01) return 0;
            float roadWidth = input.layout.x;
            float lanes = input.layout.y;
            float median = input.layout.z;
            float x = (input.uv.x - 0.5) * roadWidth;
            float distance = input.uv.y * 4.0;
            float aa = max(fwidth(x), 0.002);
            float longitudinalAA = max(fwidth(distance), 0.002);
            float cycle = max(_DashLength + _DashGap, 0.1);
            float phase = frac(distance / cycle) * cycle;
            float dash = smoothstep(0, longitudinalAA, phase) *
                (1.0 - smoothstep(_DashLength - longitudinalAA, _DashLength + longitudinalAA, phase));
            float mask = 0;
            float halfLanes = lanes * 0.5;
            bool evenLanes = abs(halfLanes - round(halfLanes)) < 0.1;
            [unroll] for (int divider = 1; divider < 8; divider++)
            {
                if (divider >= lanes) continue;
                bool center = input.layout.w < 0.5 && evenLanes && abs(divider - halfLanes) < 0.1;
                if (center && median > 0) continue;
                float offset = -roadWidth * 0.5 + roadWidth * divider / lanes;
                if (median > 0 && evenLanes)
                {
                    float carriageway = (roadWidth - median) * 0.5;
                    offset = divider < halfLanes ? -roadWidth * 0.5 + carriageway * divider / halfLanes :
                        median * 0.5 + carriageway * (divider - halfLanes) / halfLanes;
                }
                mask = max(mask, Stripe(x, offset, _LineWidth, aa) * (center ? 1.0 : dash));
            }
            if (roadWidth >= 7)
            {
                mask = max(mask, Stripe(x, -roadWidth * 0.5 + 0.25, _LineWidth * 0.83, aa));
                mask = max(mask, Stripe(x, roadWidth * 0.5 - 0.25, _LineWidth * 0.83, aa));
            }
            float ends = smoothstep(_JunctionClearance, _JunctionClearance + longitudinalAA, distance) *
                (1.0 - smoothstep(input.limits.x - _JunctionClearance - longitudinalAA,
                    input.limits.x - _JunctionClearance, distance));
            // Fade subpixel paint at a distance instead of flickering.
            return saturate(mask * ends * saturate(_LineWidth / max(aa, 0.001)));
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 Frag(Varyings input) : SV_Target
            {
                // World mapping keeps asphalt grain and wear continuous at intersections.
                half3 asphalt = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    input.positionWS.xz * 0.5 * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * _BaseColor.rgb;
                half4 wear = RoadWear(input.positionWS.xz);
                asphalt = lerp(asphalt, wear.rgb, wear.a);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(asphalt, _PaintColor.rgb, Marking(input));
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = half3(0,0,1);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = normalize(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.vertexLighting = VertexLighting(input.positionWS, lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 NormalFrag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
