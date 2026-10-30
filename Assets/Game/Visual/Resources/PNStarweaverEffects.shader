Shader "PN/StarweaverEffects" {
Properties {
 _Opacity("Opacity",Float)=1 _Mode("Effect mode",Float)=0 _FxTime("Entity time",Float)=0
 _Rise("Burst rise",Float)=0 _Reveal("Reveal",Float)=1 _MainTex("Mask",2D)="white"{} _NoiseTex("Flow noise",2D)="gray"{}
 _Tint("Tint",Color)=(1,1,1,1) _DstBlend("Destination blend",Float)=10
 _Formation("Crystal formation",Float)=1 _Crack("Crystal fractures",Float)=0
 _Charge("Stored energy",Float)=0 _Absorb("Absorption impulse",Float)=0 _FlowHead("Travelling pulse",Float)=0
}
SubShader {
Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float _Opacity, _Mode, _FxTime, _Rise, _Reveal, _Formation, _Crack, _Charge, _Absorb, _FlowHead;
sampler2D _MainTex,_NoiseTex;float4 _Tint;
struct A{float4 positionOS:POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
struct V{float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
V vert(A i){V o;if(_Mode>2.5&&_Mode<3.5)i.positionOS.y*=max(.005,_Rise);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
half4 frag(V i):SV_Target
{
    if(_Mode<.5)return half4(i.color.rgb*_Tint.rgb,i.color.a*_Opacity*_Tint.a);
    if(_Mode>7.5&&_Mode<8.5)
    {
        float noise=tex2D(_NoiseTex,i.uv*1.8).r;
        float front=_Formation-(noise*.8+.08);clip(front);clip(_Formation-.001);
        float edge=1-smoothstep(0,.06,front);
        float fracture=pow(saturate(1-abs(noise-.52)*22),3)*_Crack;
        float2 p=i.uv-.5;float r=length(p);
        float innerNoise=tex2D(_NoiseTex,i.uv*2+float2(-_FxTime*.3,_FxTime*.22)).r;
        float inward=pow(saturate(.5+.5*cos(r*36+_FxTime*9)),8)*innerNoise;
        float core=exp(-r*r*65);
        float3 light=(edge*.8+fracture*.8+inward*_Charge*.4+core*(_Charge*.65+_Absorb*1.3))*float3(1,.62,1);
        return half4(i.color.rgb*_Tint.rgb+light,i.color.a*_Opacity*_Tint.a);
    }
    float2 uv=i.uv;float alpha=1;float3 rgb=1;
    // Opaque dark body and luminous violet seams: alpha blend is essential on bright floors.
    if(_Mode>9.5&&_Mode<10.5)
    {
        float n=tex2D(_NoiseTex,float2(uv.x*3-_FxTime*1.9,uv.y*2)).r;
        float fine=tex2D(_NoiseTex,float2(uv.x*8+_FxTime*.8,uv.y*4)).r;
        float width=.32+(n-.5)*.25;float d=abs(uv.y-.5);
        float body=1-smoothstep(width-.06,width,d);
        float seam=exp(-pow((d-width*.68)*28,2));
        float core=pow(saturate(1-d*12),5)*smoothstep(.38,.7,fine);
        float3 color=lerp(float3(.012,.002,.024),float3(.19,.008,.36),n);
        color+=seam*float3(.72,.08,1.3)+core*float3(1,.5,1.7);
        return half4(color,body*i.color.a*_Opacity);
    }
    if(_Mode>10.5&&_Mode<12.5)
    {
        float2 p=(uv-.5)*2;float r=length(p);float a=atan2(p.y,p.x);
        float n=tex2D(_NoiseTex,p*1.6+float2(_FxTime*.16,-_FxTime*.23)).r;
        float teeth=sin(a*7+n*3)*.035+sin(a*13)*.012;
        float ringRadius=_Mode<11.5?.58:.64;
        float ring=exp(-pow((r-ringRadius+teeth)*(_Mode<11.5?7:9),2));
        float broken=.4+.6*smoothstep(.16,.5,n+.15*sin(a*5));
        float seam=exp(-pow((r-ringRadius+.045+teeth)*35,2));
        float inward=pow(saturate(.5+.5*cos(r*28+_FxTime*13+n*4)),7);
        float3 color=lerp(float3(.025,.002,.045),float3(.3,.015,.6),n);
        color+=seam*float3(.75,.12,1.1)+inward*float3(.18,.015,.4);
        return half4(color,ring*broken*_Opacity*i.color.a);
    }
    if(_Mode>12.5&&_Mode<13.5)
    {
        // Stylized metal: bronze shadow, gold body and a narrow travelling reflection, alpha blended.
        float bevel=smoothstep(.12,.38,uv.y)*(1-smoothstep(.63,.94,uv.y));
        float edge=exp(-pow((uv.y-.24)*24,2));
        float highlight=exp(-pow((uv.x-frac(_FxTime*.7+.2))*12,2))*bevel;
        float3 metal=lerp(float3(.18,.075,.018),float3(.72,.43,.10),bevel);
        metal+=edge*float3(.48,.38,.19)+highlight*float3(.65,.64,.47);
        float noise=tex2D(_NoiseTex,float2(uv.x*8-_FxTime*.4,uv.y*2)).r;
        float dissolve=smoothstep(1-_Reveal,1-_Reveal+.08,uv.x+(noise-.5)*.08);
        return half4(metal*i.color.rgb*_Tint.rgb,i.color.a*_Opacity*_Tint.a*dissolve);
    }
    if(_Mode>8.5)
    {
        clip(_Reveal-uv.x);
        float noise=tex2D(_NoiseTex,float2(uv.x*3-_FxTime*.8,uv.y*.4)).r;
        float fibre=exp(-pow((uv.y-.5+(noise-.5)*.08)*9,2));
        float pulse=exp(-pow((uv.x-_FlowHead)*19,2));
        float flow=.2+pulse*1.7;
        alpha=fibre*flow*smoothstep(0,.05,_Reveal-uv.x);
        return half4(lerp(float3(1,.62,.08),float3(1,.94,.55),fibre*.5)*i.color.rgb*_Tint.rgb,saturate(alpha*i.color.a*_Opacity));
    }
    if(_Mode<1.5)
    {
        // Small sharp four-point fragments; main stars use a faceted mesh, not this mask.
        float2 p=abs((uv-.5)*2);float edge=pow(p.x,.55)+pow(p.y,.55);
        alpha=1-smoothstep(.83,1.02,edge);
    }
    else if(_Mode<2.5)
    {
        clip(_Reveal-uv.x);
        float noise=tex2D(_NoiseTex,float2(uv.x*5-_FxTime*.9,uv.y*1.6+_FxTime*.23)).r;
        float fine=tex2D(_NoiseTex,float2(uv.x*11+_FxTime*.7,uv.y*3-_FxTime*.4)).r;
        float mask=tex2D(_MainTex,float2(.12+uv.x*.75,saturate(uv.y+(noise-.5)*.3))).r;
        float holes=.18+.82*smoothstep(.23,.57,noise);
        alpha=saturate(mask*1.7)*holes*(.65+fine*.65)*smoothstep(0,.07,_Reveal-uv.x);
        float gold=smoothstep(.65,.85,fine)*smoothstep(.5,.75,noise);
        rgb=lerp(float3(.32,.025,1.1),float3(1,.38,1.6),mask*.65+fine*.25);
        rgb=lerp(rgb,float3(1.7,1.05,.2),gold);
    }
    else if(_Mode<3.5)
    {
        float x=abs(uv.x-.5)*2;
        float noise=tex2D(_NoiseTex,float2(uv.x*1.8,uv.y*1.7-_FxTime*1.7)).r;
        float mask=tex2D(_MainTex,float2(uv.y,saturate(uv.x+(noise-.5)*.15))).r;
        float core=pow(saturate(1-x),5);
        float shape=pow(saturate(1-x*x),2)*(1-smoothstep(.65,1,uv.y));
        rgb=lerp(float3(.58,.1,1),float3(1,.82,1),saturate(core*1.3+mask*.25));
        alpha=shape*(mask*.7+noise*.55+core*.6);
    }
    else if(_Mode<4.5)
    {
        rgb=float3(.55,.16,.8);alpha=.055;
    }
    else if(_Mode<5.5)
    {
        // Pack masks are grayscale; black is transparency even where source alpha is opaque.
        float4 mask=tex2D(_MainTex,uv);alpha=mask.r*mask.a;
    }
    else if(_Mode<6.5)
    {
        // One outward shock front. No angular phase, orbit or rotating tail.
        float r=length((uv-.5)*2);float noise=tex2D(_NoiseTex,uv*4).r;
        float ring=exp(-pow((r-.72+(noise-.5)*.06)*21,2));
        alpha=ring*(.6+noise*.4)*(1-smoothstep(.92,1,r));
        rgb=lerp(float3(.55,.08,1),float3(1,.8,1),ring*.7);
    }
    else
    {
        float4 smoke=tex2D(_MainTex,uv);float noise=tex2D(_NoiseTex,uv+float2(_FxTime*.06,-_FxTime*.1)).r;
        alpha=saturate(smoke.r*2)*smoke.a*(.6+noise*.4);
        rgb=lerp(float3(.15,.025,.26),float3(.7,.24,.9),smoke.r);
    }
    return half4(rgb*i.color.rgb*_Tint.rgb,saturate(alpha*i.color.a*_Opacity*_Tint.a));
}
ENDHLSL
Pass { Tags{"LightMode"="UniversalForward"} Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
ENDHLSL
}
Pass { Tags{"LightMode"="Universal2D"} Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
ENDHLSL
}
}}
