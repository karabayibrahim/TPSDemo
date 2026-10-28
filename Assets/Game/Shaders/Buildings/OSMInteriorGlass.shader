Shader "City/Interior Mapped Glass"
{
    Properties
    {
        _BaseColor("Glass Tint", Color) = (0.3,0.42,0.5,1)
        _RoomColor("Room Walls", Color) = (0.6,0.55,0.44,1)
        _RoomGrid("Rooms Per Window", Vector) = (1,1,0,0)
        _RoomDepth("Room Depth (m)", Range(1,8)) = 3.5
        _InteriorStrength("Interior Visibility", Range(0,1)) = 0.5
        _ReflectionStrength("Glass Reflection", Range(0,1)) = 0.6
        _InteriorType("Interior Type (Home / Shop / Office)", Float) = 0
        _FadeDistance("Interior Fade Distance (m)", Float) = 100
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        float _CityNight, _CityDaylight, _CityHour;
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _RoomColor;
            float4 _RoomGrid;
            float _RoomDepth, _InteriorStrength, _ReflectionStrength, _FadeDistance, _InteriorType;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 room : TEXCOORD1;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            nointerpolation float3 room : TEXCOORD3;
            half fog : TEXCOORD4;
        };
        Varyings Vert(Attributes input)
        {
            Varyings o;
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = input.uv;
            o.room = input.room.xyz;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }
        float Random(float seed) { return frac(sin(seed * 127.1 + 311.7) * 43758.5453); }
        float Rect(float2 p, float2 center, float2 extent)
        {
            float2 d = abs(p - center) - extent;
            float aa = max(max(fwidth(p.x), fwidth(p.y)), 0.003);
            return 1 - smoothstep(-aa, aa, max(d.x, d.y));
        }
        half3 RoomSurface(float3 hit, float3 distances, float seed)
        {
            bool back = distances.z <= min(distances.x, distances.y);
            bool side = distances.x < distances.y && !back;
            float2 p = back ? hit.xy : side ? float2(hit.z, hit.y) : float2(hit.x, hit.z);
            float style = floor(Random(seed + 11) * 4);
            float shift = lerp(-0.4,0.4,Random(seed + 8));
            half3 accent = lerp(half3(0.13,0.2,0.22),half3(0.32,0.19,0.12),Random(seed + 12));
            half3 wall = _RoomColor.rgb * lerp(0.48,0.9,Random(seed + 4));
            wall *= lerp(half3(1,0.87,0.72),half3(0.75,0.88,1),Random(seed + 7));
            if (!back && !side)
            {
                if (hit.y < 0)
                {
                    float floorPattern = _InteriorType > 0.5 ?
                        step(0.08,frac((hit.x + 1) * 5)) * step(0.08,frac((hit.z + 1) * 5)) :
                        smoothstep(0.03,0.07,frac((hit.x + 1) * 8));
                    wall = lerp(half3(0.12,0.1,0.08),wall * 0.5,floorPattern);
                }
                else
                {
                    float lamp = Rect(p,float2(shift,-0.1),float2(_InteriorType > 1.5 ? 0.5 : 0.16,0.22));
                    wall = lerp(wall * 0.6,half3(0.72,0.65,0.5),lamp * 0.5);
                }
            }
            else
            {
                wall *= back ? 0.66 : 0.8;
                wall *= lerp(0.4,1,smoothstep(-0.93,-0.84,hit.y));
            }
            if (back)
            {
            float furniture = 0;
            if (_InteriorType > 1.5)
            {
                // Offices: desks, monitors, storage or meeting tables.
                furniture = Rect(p,float2(shift,-0.56),float2(0.6,0.07));
                furniture = max(furniture,Rect(p,float2(shift-0.2,-0.29),float2(0.17,0.17)));
                furniture = max(furniture,Rect(p,float2(0.72,0),float2(0.16,0.68)) * step(1.5,style));
                wall = lerp(wall,accent,furniture);
                wall = lerp(wall,half3(0.12,0.18,0.22),Rect(p,float2(shift-0.2,-0.27),float2(0.13,0.12)));
            }
            else if (_InteriorType > 0.5)
            {
                // Shops: shelving, display counter, cafe seating or hanging stock.
                if (style < 2)
                {
                    float shelf = step(0.84,frac((p.y + 1) * 3));
                    float stock = step(0.3,frac((p.x + 1) * 5)) * step(0.25,frac((p.y + 1) * 3));
                    float region = Rect(p,float2(0,0.03),float2(0.82,0.7));
                    wall = lerp(wall,accent,region * max(shelf,stock * 0.65));
                }
                furniture = Rect(p,float2(shift,-0.68),float2(style < 2 ? 0.78 : 0.35,0.22));
                wall = lerp(wall,accent,furniture);
                wall = lerp(wall,wall * 0.35,Rect(p,float2(-0.5,0.33),float2(0.18,0.22)) * step(1.5,style));
            }
            else
            {
                // Homes: living room, bedroom, kitchen or a mostly empty room.
                if (style < 1)
                    furniture = Rect(p,float2(shift,-0.64),float2(0.6,0.22));
                else if (style < 2)
                    furniture = max(Rect(p,float2(shift,-0.7),float2(0.64,0.16)),
                        Rect(p,float2(shift,-0.52),float2(0.54,0.12)));
                else if (style < 3)
                    furniture = max(Rect(p,float2(0,-0.65),float2(0.83,0.28)),
                        Rect(p,float2(-0.36,0.38),float2(0.44,0.24)));
                else
                    furniture = Rect(p,float2(0.6,-0.1),float2(0.19,0.75));
                wall = lerp(wall,accent,furniture);
                float art = Rect(p,float2(-shift,0.28),float2(0.2,0.24));
                wall = lerp(wall,accent * 0.7,art * step(0.35,Random(seed + 14)));
            }
            }
            return wall;
        }
        ENDHLSL
        Pass
        {
            Name "InteriorGlass"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fog
            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                float3 toCamera = GetWorldSpaceViewDir(input.positionWS);
                float distance = length(toCamera);
                half3 view = toCamera / max(distance, 0.001);
                half fresnel = pow(1 - saturate(dot(normal, view)), 5);
                half3 reflection = max(SampleSH(reflect(-view, normal)), half3(0.06,0.075,0.09));
                half3 glass = reflection * _BaseColor.rgb;
                float2 grid = max(_RoomGrid.xy, 1);
                float2 roomUV = input.uv * grid;
                float seed = input.room.z * 91 + dot(floor(min(roomUV, grid - 0.001)), float2(13.7,29.3));
                // Stable per-window occupancy and staggered dusk thresholds.
                float threshold = lerp(0.12,0.7,Random(seed + 51));
                float chance = _InteriorType > 1.5 ? 0.46 : _InteriorType > 0.5 ? 0.78 : 0.68;
                chance *= _InteriorType > 1.5 && _CityHour < 6 ? 0.4 : 1;
                float lit = step(Random(seed + 53),chance) * smoothstep(threshold,threshold + 0.2,_CityNight);
                half3 lampColor = lerp(half3(1,0.55,0.22),half3(0.65,0.78,1),
                    _InteriorType > 1.5 ? Random(seed + 55) * 0.75 : Random(seed + 55) * 0.2);
                half3 glow = lampColor * lit * lerp(0.2,0.42,Random(seed + 57));
                // Skip box intersection and room decoration on distant buildings.
                if (distance > _FadeDistance || _InteriorStrength < 0.001)
                    return half4(MixFog(glass + glow, input.fog), 1);
                float3 right = normalize(cross(normal, float3(0,1,0)));
                float3 up = cross(right, normal);
                float2 dimensions = max(input.room.xy / grid, float2(1,1));
                float depth = _RoomDepth * lerp(0.85,1.2,Random(seed));
                float3 ray = float3(dot(-view,right) / dimensions.x,
                    dot(-view,up) / dimensions.y, -max(dot(view,normal),0.001) / depth);
                float3 pos = float3(frac(roomUV) * 2 - 1, 0.999);
                float3 safeRay = (step(0,ray) * 2 - 1) * max(abs(ray), 0.0001);
                float3 times = ((step(0,ray) * 2 - 1) - pos) / safeRay;
                float3 hit = pos + ray * min(times.x,min(times.y,times.z));
                half3 interior = RoomSurface(hit,times,seed);
                half occupied = lerp(0.28,1,step(0.22,Random(seed + 21)));
                interior *= occupied * 0.65;
                // Curtains and blinds stay on the glass plane, obscuring the room.
                float curtainChoice = Random(seed + 32);
                float2 frontUV = frac(roomUV);
                float curtain = 0;
                if (_InteriorType < 0.5 && curtainChoice > 0.32)
                {
                    float opening = lerp(0.12,0.65,Random(seed + 33));
                    curtain = smoothstep(opening,opening + 0.04,abs(frontUV.x - 0.5) * 2);
                    if (curtainChoice > 0.8) curtain = 0.92;
                    half3 cloth = _RoomColor.rgb * (0.23 + 0.035 * sin(frontUV.x * 110));
                    interior = lerp(interior,cloth,curtain * 0.9);
                }
                else if (_InteriorType > 1.5 && curtainChoice > 0.42)
                {
                    float blinds = step(0.22,frac(frontUV.y * 20));
                    interior = lerp(interior,_RoomColor.rgb * 0.22,blinds * 0.72);
                }
                interior *= lerp(0.035,1,saturate(_CityDaylight));
                interior += glow * (0.55 + saturate(interior) * 0.8);
                float fade = 1 - smoothstep(_FadeDistance * 0.65, _FadeDistance, distance);
                half reflectionAmount = saturate(_ReflectionStrength + fresnel * 0.65);
                half3 color = lerp(glass, lerp(interior * lerp(half3(1,1,1),_BaseColor.rgb,0.2),
                    glass, reflectionAmount), fade * _InteriorStrength);
                color += glow * (1 - fade * 0.6);
                return half4(MixFog(color,input.fog),1);
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
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
