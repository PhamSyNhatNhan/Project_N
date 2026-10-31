Shader "PN/StarweaverEffects" {
Properties {
 _Opacity("Opacity",Float)=1 _Mode("Effect mode",Float)=0 _FxTime("Entity time",Float)=0
 _Rise("Burst rise",Float)=0 _Reveal("Reveal",Float)=1 _MainTex("Mask",2D)="white"{} _NoiseTex("Flow noise",2D)="gray"{}
 _Tint("Tint",Color)=(1,1,1,1) _DstBlend("Destination blend",Float)=10
 _Formation("Crystal formation",Float)=1 _Crack("Crystal fractures",Float)=0
 _Charge("Stored energy",Float)=0 _Absorb("Absorption impulse",Float)=0 _FlowHead("Travelling pulse",Float)=0
 _SlashFlowTex("Slash stretched flow",2D)="gray"{} _SlashGrainTex("Slash torn grain",2D)="gray"{}
 _SlashPhase("Slash lifetime",Float)=0 _SlashLayer("Slash ink/color/edge",Float)=0 _SlashDissolveTex("Slash dissolve",2D)="white"{}
}
SubShader {
Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float _Opacity, _Mode, _FxTime, _Rise, _Reveal, _Formation, _Crack, _Charge, _Absorb, _FlowHead;
float _SlashPhase,_SlashLayer; sampler2D _SlashDissolveTex,_SlashFlowTex,_SlashGrainTex;
sampler2D _MainTex,_NoiseTex;float4 _Tint;
struct A{float4 positionOS:POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
struct V{float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
V vert(A i){V o;if(_Mode>2.5&&_Mode<3.5)i.positionOS.y*=max(.005,_Rise);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
half4 frag(V i):SV_Target
{
    if(_Mode>24.5)
    {
        // Textured body wake: torn broad silhouette, stretched flow and sparse inner light.
        float cross=abs(i.uv.y-.5)*2;
        float coverage=1-smoothstep(.55,1,cross);
        float layer=step(.5,_SlashLayer);
        float flow=tex2D(_SlashFlowTex,float2(i.uv.x*.72-_FxTime*.9,i.uv.y*2.4+layer*.17)).r;
        float2 maskUV=float2(i.uv.x*.72-_FxTime*.75+layer*.23,lerp(.08,.92,i.uv.y)+(flow-.5)*.07);
        float mask=tex2D(_MainTex,maskUV).r;
        float strands=smoothstep(.20,.70,flow);
        float torn=smoothstep(.12,.67,mask);
        float ends=smoothstep(0,.025,i.uv.x)*(1-smoothstep(.82,1,i.uv.x));
        float light=lerp(.48+strands*.45,.2+strands*1.1,layer);
        float alpha=torn*lerp(.78,.35+strands*.55,layer)*coverage*ends*i.color.a*_Opacity;
        return half4(_Tint.rgb*light,alpha);
    }
    if(_Mode>23.5)
    {
        // A soft curved pressure sheath around the spear tip; open behind, never a solid cone.
        float noise=tex2D(_NoiseTex,float2(i.uv.x*2.2-_FxTime*.8,i.uv.y*1.3)).r;
        float rim=exp(-pow((i.uv.y-.76+(noise-.5)*.10)*9,2));
        float leading=exp(-pow((i.uv.y-.10)*7,2));
        float veil=.4+.6*smoothstep(.18,.6,noise);
        return half4(_Tint.rgb*(.7+rim*.6)+rim*float3(.6,.45,.9)+leading*float3(.7,.5,1), (rim*.7+leading*.24)*veil*i.color.a*_Opacity);
    }
    if(_Mode>22.5)
    {
        // A frozen puppet atlas snapshot; borrowed atlas is sampled, never edited.
        float4 art=tex2D(_MainTex,i.uv);float light=dot(art.rgb,float3(.299,.587,.114));
        return half4(_Tint.rgb*(.55+.45*light),art.a*i.color.a*_Opacity);
    }
    if(_Mode>21.5)
    {
        float cross=abs(i.uv.y-.5)*2;
        float core=exp(-cross*cross*9), halo=exp(-cross*cross*2.8);
        float flow=tex2D(_NoiseTex,float2(i.uv.x*1.5-_FxTime*.6,i.uv.y*.5+.2)).r;
        float ends=smoothstep(0,.035,i.uv.x)*(1-smoothstep(.82,1,i.uv.x));
        return half4(_Tint.rgb*(.7+core*.8),(.42*core+.09*halo)*(.6+.4*flow)*ends*i.color.a*_Opacity);
    }
    if(_Mode>19.5)
    {
        float along=i.uv.y;
        float reveal=1-smoothstep(_Reveal-.05,_Reveal+.04,along);
        float core=exp(-pow((i.uv.x-.5)*24,2));
        float flow=tex2D(_NoiseTex,float2(along*3-_FxTime*.75,.35)).r;
        float edge=pow(saturate(abs(i.uv.x-.5)*2),3);
        float3 color=_Tint.rgb*(.7+flow*.3)+core*float3(.5,.3,.9)+edge*float3(.4,.12,.65);
        return half4(color,(.38+core*.30+edge*.20)*reveal*_Opacity);
    }
    if(_Mode>18.5)
    {
        float flow=tex2D(_NoiseTex,float2(i.uv.x*2-_FxTime*1.6,i.uv.y*2)).r;
        float core=pow(saturate(1-i.uv.y),.4);
        return half4(lerp(float3(.32,.035,.8),float3(1.15,.5,2),flow),core*(.24+flow*.45)*i.color.a*_Opacity);
    }
    if(_Mode>17.5)
    {
        float cross=abs(i.uv.x-.5)*2;
        float along=smoothstep(0,.12,i.uv.y)*(1-smoothstep(.88,1,i.uv.y));
        float aura=exp(-cross*cross*4)*along;
        return half4(_Tint.rgb,aura*_Opacity);
    }
    if(_Mode>16.5)
    {
        // Ignore the authored gold vertex RGB: the whole shaft/blade/tail receives violet light.
        return half4(_Tint.rgb*(.9+.1*sin(_FxTime*11)),_Opacity*_Tint.a);
    }
    if(_Mode>15.5)
    {
        // Warning filament: luminous core, soft shoulders and phase-driven broken reveal.
        float2 uv=i.uv;
        float noise=tex2D(_NoiseTex,float2(uv.x*2.7-_Reveal*.15,.38)).r;
        float thin=exp(-pow((uv.y-.5)*15,2));
        float aura=exp(-pow((uv.y-.5)*4,2));
        float charge=smoothstep(.68,1,_Reveal);
        float broken=lerp(smoothstep(.32,.62,noise),1,charge*.8);
        float ends=smoothstep(0,.025,uv.x)*(1-smoothstep(.975,1,uv.x));
        float alpha=(thin*.65+aura*.14)*broken*ends;
        return half4(lerp(float3(1.2,.48,.055),float3(2.3,1.65,.55),thin),alpha*i.color.a*_Opacity);
    }
    if(_Mode>14.5)
    {
        // Luminous starlight, not a lit metal sphere. Halo is visible without camera bloom.
        float2 p=(i.uv-.5)*2;float r2=dot(p,p);
        float core=exp(-r2*240), inner=exp(-r2*45), halo=exp(-r2*4.5);
        float flare=exp(-p.y*p.y*280-p.x*p.x*9)*.13;
        float dust=0;
        [unroll]for(int k=0;k<6;k++)
        {
            float a=k*2.39996, radius=.38+.18*sin(k*3.1)-_Reveal*.17;
            float2 centre=float2(cos(a),sin(a))*radius;
            float2 d=p-centre; dust+=exp(-dot(d,d)*1800)*(.3+.7*_Reveal);
        }
        float charge=.7+.7*smoothstep(.78,1,_Reveal);
        float3 color=core*float3(4,3.5,2.4)+inner*float3(2,1.2,.28);
        color+=(halo*.7+flare+dust*.6)*float3(1.35,.6,.07);
        float alpha=saturate(core+inner*.8+halo*.38+flare+dust*.65);
        return half4(color*charge*i.color.rgb,alpha*i.color.a*_Opacity);
    }
    // V7 slash appearance restored; luminous warning modes above remain unchanged.
    if(_Mode>13.5)
    {
        float2 uv=float2(i.uv.x,1-i.uv.y);
        float phase=_SlashPhase;
        float mask=tex2D(_MainTex,uv).r;
        float erosion=tex2D(_SlashDissolveTex,uv).r;
        float flow=tex2D(_SlashFlowTex,float2(i.uv.x*1.8-phase*.65,i.uv.y*3.2)).r;
        float grain=tex2D(_SlashGrainTex,float2(i.uv.x*1.8,i.uv.y*1.6)).r;
        float fine=tex2D(_SlashFlowTex,float2(i.uv.x*3-phase*.9,i.uv.y*8+flow*.18)).r;
        float vanish=saturate((phase-.36)/.64);
        float cutoff=vanish*1.15-.08;
        float keep=smoothstep(cutoff,cutoff+.07,erosion*.72+flow*.28);
        float ends=smoothstep(0,.045,i.uv.x)*(1-smoothstep(.97,1,i.uv.x));
        float envelope=ends*(1-smoothstep(.82,1,phase));
        float torn=smoothstep(.14,.37,grain*.55+flow*.45);
        float alpha=mask*keep*envelope;
        float3 color=0;
        if(_SlashLayer<.5)
        {
            alpha*=torn*.30;
            color=lerp(float3(.018,.003,.035),float3(.15,.014,.24),flow*grain);
        }
        else if(_SlashLayer<1.5)
        {
            float veins=smoothstep(.47,.76,fine)*smoothstep(.28,.56,flow);
            alpha*=torn*(.18+veins*.25);
            color=lerp(float3(.12,.012,.26),float3(.63,.13,.95),flow);
            color+=veins*float3(.55,.29,.8);
        }
        else if(_SlashLayer<2.5)
        {
            float rim=exp(-pow((i.uv.y-.956+(flow-.5)*.025)*43,2));
            float streak=pow(saturate(fine),3)*smoothstep(.6,.9,i.uv.y);
            alpha=keep*envelope*(rim*(.3+.7*flow)+streak*.35);
            color=lerp(float3(.85,.24,1.1),float3(1.6,1.1,.46),smoothstep(.64,.83,grain));
        }
        else if(_SlashLayer<3.5)
        {
            float stars=0;
            [unroll] for(int k=0;k<3;k++)
            {
                float2 centre=k==0?float2(.29,.68):(k==1?float2(.57,.86):float2(.82,.59));
                float2 d=abs(i.uv-centre)/float2(.015,.046);
                float star=1-smoothstep(.65,1.0,pow(d.x,.6)+pow(d.y,.6));
                float appear=.23+k*.095;
                float pulse=smoothstep(appear,appear+.09,phase)*(1-smoothstep(appear+.18,appear+.39,phase));
                stars+=star*pulse;
            }
            alpha=saturate(stars)*envelope*.72;
            color=float3(1.65,1.12,.46);
        }
        else
        {
            alpha*=torn*.12*(1-smoothstep(.20,.52,phase));
            color=float3(.35,.09,.55);
        }
        return half4(color*i.color.rgb*_Tint.rgb,alpha*i.color.a*_Opacity*_Tint.a);
    }
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
