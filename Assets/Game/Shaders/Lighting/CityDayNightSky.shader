Shader "City/Day Night Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _CityDaylight, _CityNight;
            float4 _CitySunDirection;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = saturate(direction.y);
                half3 day = lerp(half3(0.65,0.73,0.79),half3(0.11,0.32,0.65),pow(max(height,0.0001),0.45));
                half3 night = lerp(half3(0.018,0.025,0.055),half3(0.003,0.007,0.025),height);
                half3 color = lerp(night,day,_CityDaylight);
                float3 sun = normalize(_CitySunDirection.xyz);
                float horizon = pow(max(1 - abs(direction.y),0.0001),7);
                float dusk = saturate(1 - abs(sun.y) * 7) * _CityDaylight;
                color += half3(0.7,0.2,0.04) * horizon * dusk * pow(max(saturate(dot(direction,sun) * 0.5 + 0.5),0.0001),4);
                float sunDisk = smoothstep(0.99965,0.99985,dot(direction,sun)) * _CityDaylight;
                color += half3(3.2,2.5,1.5) * sunDisk;
                float moon = smoothstep(0.9996,0.99985,dot(direction,-sun)) * _CityNight;
                color += half3(0.65,0.73,0.9) * moon;
                float2 starUV = float2(atan2(direction.z,direction.x),asin(clamp(direction.y,-1,1))) * 330;
                float2 cell = floor(starUV);
                float random = frac(sin(dot(cell,float2(127.1,311.7))) * 43758.5453);
                float star = step(0.997,random) * (1 - smoothstep(0.03,0.22,length(frac(starUV)-0.5)));
                color += star * _CityNight * smoothstep(0.02,0.2,direction.y) * 0.7;
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
