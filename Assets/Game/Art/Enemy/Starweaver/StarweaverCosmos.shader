Shader "Project_N/Starweaver/CosmicHood"
{
    Properties { _Phase ("Animation phase", Float) = 0 _Mode ("Layer: interior, surface", Float) = 0 _NoiseTex ("Pack flow noise", 2D) = "gray" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Phase;
            float _Mode;
            CBUFFER_END
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normal:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A a) { V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz+a.normal*(_Mode==1?.008:0));o.uv=a.uv;return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y); }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv;float t=_Phase;
                float flow=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,p*1.7+float2(t*.025,-t*.07)).r;
                p.x+=(flow-.5)*.055;
                // A pointed cloth silhouette, broad shoulders, a slightly bent tip.
                float centre=.5+.07*smoothstep(.65,1,p.y)+.012*sin(t*.65+p.y*5);
                float width=.47*sqrt(saturate(1-pow((p.y-.38)/.62,2)));
                float edge=width-abs(p.x-centre);
                float hem=.045+.025*sin(p.x*19+t*.5);
                float mask=smoothstep(0,.035,edge)*smoothstep(hem,hem+.09,p.y)*(1-smoothstep(.96,1,p.y));
                float n=Noise(p*5+float2(t*.025,-t*.035));
                float cloud=n*.65+Noise(p*12-float2(t*.018,t*.025))*.35;
                float fold=pow(saturate(sin((p.x-centre)*24+p.y*3+n*2)*.5+.5),5);
                float rim=exp(-max(edge,0)*80);
                float3 col=lerp(float3(.008,.003,.022),float3(.11,.025,.23),smoothstep(.27,.83,cloud));
                col+=float3(.08,.015,.16)*fold*.5+float3(.28,.07,.48)*rim*.65;
                float2 cell=floor(p*float2(24,29)),f=frac(p*float2(24,29))-.5;
                float seed=Hash(cell);
                float star=(exp(-dot(f,f)*350)+.08*exp(-dot(f,f)*35))*step(.945,seed)*(.45+.55*pow(sin(t*.65+seed*30)*.5+.5,2));
                col+=float3(.65,.37,1)*star;
                if(_Mode>.5 && _Mode<1.5)
                    return half4(col*.65+float3(.04,.008,.12)*cloud,.12+cloud*.10);
                float interior=1-smoothstep(.40,.5,length((i.uv-.5)*float2(1,1)));
                return half4(col,interior*.99);
            }
            ENDHLSL
        }
    }
}
