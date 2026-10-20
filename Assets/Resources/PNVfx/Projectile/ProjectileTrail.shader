Shader "Project_N/VFX/ProjectileTrail"
{
    Properties { _MainTex ("Glow texture", 2D) = "white" {} _Tint ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.color=v.color; return o; }
            half4 frag(Varyings i):SV_Target
            {
                half3 glow = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                return half4(glow * i.color.rgb * _Tint.rgb, saturate(i.color.a * _Tint.a));
            }
            ENDHLSL
        }
    }
}
