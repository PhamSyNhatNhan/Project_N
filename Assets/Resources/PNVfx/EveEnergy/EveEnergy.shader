Shader "Project_N/VFX/EveEnergy"
{
    Properties { _Tint ("Layer tint", Color) = (1,1,1,1) _Volume ("World volume", Float) = 0 _Particle ("Particle billboard", Float) = 0 _Silhouette ("Sprite silhouette", Float) = 0 _MainTex ("Sprite texture", 2D) = "white" {} }
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
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD0; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Particle;
            float _Volume;
            float _Silhouette;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.color=v.color; o.normalWS=TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Varyings i):SV_Target
            {
                float radial = saturate(1-length((i.uv-.5)*2));
                float edge = pow(saturate(sin(i.uv.y*3.14159265)), .7);
                if (_Volume > .5)
                {
                    edge=.3+.7*edge;
                    float face=.55+.7*saturate(dot(normalize(i.normalWS),normalize(float3(-.3,.85,-.4))));
                    i.color.rgb*=face;
                }
                float alpha = lerp(edge, radial*radial, saturate(_Particle));
                if (_Silhouette > .5) alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                return half4(i.color.rgb*_Tint.rgb, i.color.a*alpha*_Tint.a);
            }
            ENDHLSL
        }
    }
}
