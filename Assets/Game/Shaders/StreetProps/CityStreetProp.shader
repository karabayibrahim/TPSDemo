Shader "City/Street Props"
{
    Properties { _Smoothness("Smoothness",Range(0,1))=0.28 _LampColor("Lamp Color",Color)=(1,0.72,0.38,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _Smoothness;
            half4 _LampColor;
        CBUFFER_END
        float _CityNight;
        struct A { float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 color:COLOR;half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
        V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 Frag(V i):SV_Target{
                UNITY_SETUP_INSTANCE_ID(i);
                InputData d=(InputData)0;d.positionWS=i.positionWS;d.normalWS=normalize(i.normalWS);d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);d.bakedGI=SampleSH(d.normalWS);d.vertexLighting=VertexLighting(i.positionWS,d.normalWS);d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);d.shadowMask=1;
                SurfaceData s=(SurfaceData)0;s.albedo=i.color.rgb;s.smoothness=_Smoothness;s.alpha=1;s.occlusion=1;s.normalTS=half3(0,0,1);s.emission=_LampColor.rgb*i.color.a*smoothstep(0.2,0.75,_CityNight)*2;
                half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,i.fog);return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment Zero
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            V ShadowVert(A i){V o=Vert(i);float3 direction=_LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction=normalize(_LightPosition-o.positionWS);
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;}
            half4 Zero(V i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            half4 Depth(V i):SV_Target{return i.positionCS.z;}
            ENDHLSL
        }
    }
}
