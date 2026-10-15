Shader "Project_N/VFX/ArcSlash"
{
    Properties
    {
        _MainTex ("Energy ribbon", 2D) = "white" {}
        _UseTexture ("Texture influence", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _UseTexture;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 ribbon = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(input.uv.x, lerp(.39,.61,input.uv.y)));
                half edge = smoothstep(0,.18,input.uv.y) * smoothstep(0,.18,1-input.uv.y);
                half4 result = input.color;
                result.rgb *= lerp(half3(1,1,1), ribbon.rgb, _UseTexture);
                result.a *= edge * lerp(1, ribbon.a, _UseTexture);
                return result;
            }
            ENDHLSL
        }
    }
}
