Shader "Project_N/VFX/EveBlade"
{
    Properties { _Intensity("Energy intensity",Float)=1 _Opacity("Opacity",Range(0,1))=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float _Intensity, _Opacity;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.color=v.color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                clip(_Opacity-.01);
                float shade=.85+.15*saturate(dot(normalize(i.normalWS),normalize(float3(-.3,.85,-.4))));
                return half4(i.color.rgb*shade*_Intensity,i.color.a*_Opacity);
            }
            ENDHLSL
        }
    }
}
