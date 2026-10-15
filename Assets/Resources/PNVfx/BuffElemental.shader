Shader "Project_N/VFX/BuffElemental"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend("Destination blend", Float) = 10
        _BuffTime("Entity effect time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DestinationBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 shape:TEXCOORD1; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float2 shape:TEXCOORD3; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float _DestinationBlend;
            float _BuffTime;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.shape=v.shape;o.color=v.color;return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            half4 frag(Varyings i):SV_Target
            {
                float alpha=1;float3 rgb=i.color.rgb;
                if(i.shape.x<.5) alpha=1-smoothstep(.35,.5,abs(i.uv.y-.5));
                else if(i.shape.x<1.5)
                {
                    float y=i.uv.y;
                    float turbulence=noise(float2(i.uv.x*4,y*5-_BuffTime*2+i.shape.y*17));
                    float bend=sin(y*6-_BuffTime*4+i.shape.y*13)*.13*y;
                    float width=pow(saturate(1-y),.65)*.43*(.68+.32*sin(y*3.14159));
                    float edge=abs(i.uv.x-.5-bend)+(turbulence-.5)*.22*y;
                    alpha=(1-smoothstep(width-.045,width+.025,edge))*smoothstep(0,.3,y)*(1-smoothstep(.87,1,y));
                    float core=(1-smoothstep(width*.12,width*.60,edge))*(1-smoothstep(.25,.8,y));
                    rgb=lerp(rgb,float3(1.4,.68,.12),core*.88);
                }
                else if(i.shape.x<2.5)
                {
                    float2 q=(i.uv-.5)*2;
                    float n=noise(i.uv*3+float2(_BuffTime*.18,i.shape.y));
                    alpha=pow(saturate(1-dot(q,q)),2)*lerp(.55,1,n);
                }
                else
                {
                    float3 normal=normalize(i.normalWS),view=normalize(GetWorldSpaceViewDir(i.positionWS));
                    float rim=pow(1-saturate(abs(dot(normal,view))),2);
                    rgb+=float3(.2,.4,.55)*rim;
                }
                return half4(rgb,i.color.a*alpha);
            }
            ENDHLSL
        }
    }
}
