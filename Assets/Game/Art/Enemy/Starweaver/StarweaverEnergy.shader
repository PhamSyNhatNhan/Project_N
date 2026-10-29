Shader "Project_N/Starweaver/AstralEnergy"
{
    Properties { [HDR] _Tint ("Violet energy", Color) = (.62,.22,1.2,.8) _Glow ("Soft halo", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" }
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
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _Glow;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normal:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.normal=TransformObjectToWorldNormal(a.normal);return o;}
            half4 Frag(V i):SV_Target
            {
                float edge=smoothstep(0,.35,i.uv.y)*smoothstep(0,.35,1-i.uv.y);
                if(_Glow>1.5)
                {
                    float2 p=(i.uv-.5)*2;
                    float r=dot(p,p);
                    if(_Glow>2.5)return half4(_Tint.rgb,_Tint.a*exp(-r*5)*.5);
                    float shape=exp(-pow(abs(p.x)*abs(p.y)*50,1.2)-r*2.2);
                    float glow=exp(-r*8)*.18;
                    return half4(_Tint.rgb*1.25,_Tint.a*(shape+glow));
                }
                if(_Glow>.5)
                {
                    float2 p=(i.uv-.5)*2.5;
                    float d=abs(p.x)/.6+abs(p.y);
                    float halo=exp(-abs(d-1)*9)*.18;
                    return half4(_Tint.rgb,_Tint.a*halo);
                }
                float shade=.45+.55*abs(dot(i.normal,float3(.3,.6,.74)));
                return half4(_Tint.rgb*shade,_Tint.a*edge);
            }
            ENDHLSL
        }
    }
}
