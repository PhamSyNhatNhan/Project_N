Shader "PN/StarreaperWeapon"
{
    Properties { _Tint("Tint",Color)=(1,1,1,1) _Specular("Soft specular",Range(0,1))=.16 _ChargeGlow("Charge glow",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        struct A { float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR; };
        struct V { float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;half4 color:COLOR; };
        CBUFFER_START(UnityPerMaterial)
        float4 _Tint;float _Specular,_ChargeGlow;
        CBUFFER_END
        V vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;return o;}
        half4 frag(V i):SV_Target
        {
            float3 n=normalize(i.normalWS),view=GetWorldSpaceNormalizeViewDir(i.positionWS);
            Light key=GetMainLight();float power=max(key.color.r,max(key.color.g,key.color.b));
            float3 l=dot(key.direction,key.direction)>.01&&power>.01?normalize(key.direction):normalize(float3(-.45,.7,-.65));
            float diffuse=saturate(dot(n,l)),spec=pow(saturate(dot(n,SafeNormalize(l+view))),36)*_Specular;
            float shade=.32+.72*diffuse;float rim=pow(1-abs(dot(n,view)),2);return half4(i.color.rgb*_Tint.rgb*shade+spec*float3(.85,.68,1)+_ChargeGlow*float3(.38,.065,.85)*(.35+rim*.65),1);
        }
        ENDHLSL
        Pass { Name "Forward" Tags { "LightMode"="UniversalForward" } ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass { Name "TwoD" Tags { "LightMode"="Universal2D" } ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
