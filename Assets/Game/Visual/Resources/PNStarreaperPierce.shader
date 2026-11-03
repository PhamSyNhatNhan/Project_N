Shader "PN/StarreaperPierce" {
Properties { _MainTex("Echo atlas",2D)="white"{} _EchoMode("Echo",Float)=0 _EchoOpacity("Retreat coat opacity",Float)=1 _EchoDissolve("Echo dissolve",Float)=0 _EchoGlow("Echo bow glow",Float)=0 _EchoHeight("Echo height",Float)=.7 _RainNoiseTex("Rain organic noise",2D)="gray"{} _RainImpactTex("Rain impact sheet",2D)="black"{} _RainDarkTex("Rain dark sheet",2D)="black"{} _RainSmokeTex("Rain smoke sheet",2D)="black"{} _NoiseTex("Flow",2D)="gray"{} _BeamTex("Namu beam",2D)="black"{} _FlowTex("Namu flowing wake",2D)="black"{} _CoverTex("Hovl fan cover",2D)="black"{} _GlobalOpacity("Owned batch fade",Float)=1 _FxTime("Projectile or entity age",Float)=0 _DstBlend("Destination",Float)=1 }
SubShader {
Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float _EchoOpacity,_EchoMode,_EchoDissolve,_EchoGlow,_EchoHeight; sampler2D _MainTex,_RainNoiseTex,_RainImpactTex,_RainDarkTex,_RainSmokeTex, _NoiseTex,_BeamTex,_FlowTex,_CoverTex; float _FxTime,_GlobalOpacity;
struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 kind:TEXCOORD1; float4 color:COLOR; };
struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 kind:TEXCOORD1; float4 color:COLOR; float2 localXY:TEXCOORD2; };
V vert(A i) { V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.kind=i.kind;o.color=i.color;o.localXY=i.positionOS.xy;return o; }
float sheetMask(sampler2D sheet,float2 uv,float frame,float rows) {
 float f=clamp(frame,0,rows*4-1),a=floor(f),b=min(a+1,rows*4-1);
 float2 cellA=float2(fmod(a,4),rows-1-floor(a/4));
 float2 cellB=float2(fmod(b,4),rows-1-floor(b/4));
 float2 local=clamp(uv,.003,.997);
 float4 x=tex2D(sheet,(local+cellA)/float2(4,rows));
 float4 y=tex2D(sheet,(local+cellB)/float2(4,rows));
 return lerp(x.r*x.a,y.r*y.a,frac(f));
}
half4 frag(V i):SV_Target {
 if(_EchoMode>.5) {
  float4 tex=(_EchoMode>1.5&&_EchoMode<2.5)?float4(i.color.rgb,1):tex2D(_MainTex,i.uv);
  if(_EchoMode>2.5) {
   float n=tex2D(_RainNoiseTex,i.localXY*2.2+float2(-_FxTime*1.5,_FxTime*.35)).r;
   float bands=.5+.5*sin(i.localXY.x*28-_FxTime*28+i.localXY.y*8);
   float3 coat=lerp(float3(.012,.003,.030),float3(.34,.07,.72),smoothstep(.28,.82,n)*(.25+bands*.75));
   return half4(coat,tex.a*i.color.a*i.kind.y*_EchoOpacity*(.45+n*.40));
  }
  float light=max(tex.r,max(tex.g,tex.b));
  float n=tex2D(_RainNoiseTex,i.localXY*8+float2(_FxTime*.12,0)).r;
  float h=saturate(i.localXY.y/max(.1,_EchoHeight));
  float keep=h*.65+light*.20+n*.15;
  float cut=smoothstep(_EchoDissolve-.025,_EchoDissolve+.025,keep);
  if(_EchoDissolve<.001)cut=1;
  float edge=exp(-pow((keep-_EchoDissolve)*32,2))*step(.001,_EchoDissolve);
  float3 ink=lerp(float3(.012,.004,.030),float3(.32,.22,.49),pow(light,5));
  if(_EchoMode>1.5)ink=float3(.06,.015,.13)+_EchoGlow*float3(.30,.07,.58);
  return half4(ink+edge*float3(.28,.06,.55),tex.a*i.color.a*cut*(1-smoothstep(.97,1,_EchoDissolve)));
 }
 float2 p=(i.uv-.5)*2;float mode=i.kind.x;float alpha=0;float3 rgb=i.color.rgb;
 if(mode>19.5) {
  float f=i.kind.y;
  if(mode<20.5) {
   float mask=sheetMask(_RainImpactTex,i.uv,f,4);
   float n=tex2D(_RainNoiseTex,i.uv*1.7+float2(_FxTime*.22,-_FxTime*.31)).r;
   alpha=mask*(.55+n*.45);
   rgb*=.25+n*.95;
  }
  else if(mode<22.5) {
   float mask=sheetMask(_RainDarkTex,i.uv,f,4);
   alpha=mode>21.5?pow(mask,.65):mask;
  } else {
   float mask=sheetMask(_RainSmokeTex,i.uv,f,3);
   float n=tex2D(_RainNoiseTex,i.uv*1.8+float2(-_FxTime*.2,0)).r;
   alpha=mask*(.55+n*.45);
   if(mode>23.5)alpha*=.35+n*.35;
  }
 } else if(mode>17.5) {
  // Dense angular blast fragments: black interior, light only at the torn rim.
  float a=atan2(p.y,p.x),r=length(p);
  float n=tex2D(_NoiseTex,p*.7+float2(_FxTime*.12,0)).r;
  float sector=(a+3.141593)*1.432394;
  float k=floor(sector),f=frac(sector);
  float r0=.48+.35*frac(sin(k*12.9898+4.1)*43758.5453);
  float r1=.48+.35*frac(sin(fmod(k+1,9)*12.9898+4.1)*43758.5453);
  float boundary=lerp(r0,r1,f)+(n-.5)*.035;
  float d=r-boundary;
  alpha=mode>18.5?exp(-pow(d*42,2))*(.20+n*.60):1-smoothstep(-.045,.025,d);
 } else if(mode>15.5) {
  // Ground-only astral scar. Soft noise erodes the edge, with a quiet centre;
  // impact mode17 is a broken ink deposit, never an expanding ring.
  float r=length(p);
  float n=tex2D(_NoiseTex,p*.46+float2(_FxTime*.035,-_FxTime*.024)).r;
  float n2=tex2D(_NoiseTex,p*1.8+float2(-_FxTime*.07,.2)).r;
  float edge=1-smoothstep(.78,.98,r+(n-.5)*.10);
  if(mode<16.5) {
   float rim=exp(-pow((r-.82+(n-.5)*.055)*22,2));
   alpha=(rim*(.48+n*.52)+(1-smoothstep(.15,.76,r))*.055)*edge;
  } else {
   alpha=edge*(1-smoothstep(.1,.91,r))*(.22+n*.48+n2*.30);
  }
 } else if(mode>13.5) {
  // Small five-point black stars, with only a fine violet silhouette rim.
  float angle=abs(fmod(atan2(p.y,p.x)+6.911504,1.256637)-.628319);
  float boundary=.16388/(.19985*cos(angle)+.54494*sin(angle));
  float distance=length(p)-boundary;
  alpha=mode>14.5?exp(-pow(distance*45,2))*.65:1-smoothstep(-.025,.02,distance);
 } else if(mode>9.5) {
  // Even modes are opaque ink bodies; odd modes only light narrow shoulders.
  // Keeping light off the centre lets the black mass remain visible.
  float n=tex2D(_NoiseTex,float2(i.uv.x*1.6-_FxTime*.55,i.uv.y*1.1)).r;
  float ends=saturate(sin(i.uv.x*3.141593));
  float taper=pow(ends,.38);
  float cross=abs(p.y+(n-.5)*.15)/max(.06,taper);
  float boundary=.62+(n-.5)*.30;
  if(fmod(floor(mode),2)>.5) {
   float rim=exp(-pow((cross-boundary)*11,2));
   alpha=rim*ends*(.5+n*.5);
  } else {
   alpha=(1-smoothstep(.20,boundary+.18,cross))*ends*(.78+n*.22);
   rgb*=.5+n*.5;
  }
 } else if(mode>8.5) {
  // Broad velvet-like energy with a continuous soft silhouette. The texture is
  // detail inside the plume, never a set of isolated white lightning contours.
  float n=tex2D(_NoiseTex,float2(i.uv.x*1.8-_FxTime*.65,i.uv.y*1.4)).r;
  float flow=tex2D(_FlowTex,float2(i.uv.x-_FxTime*.8,i.uv.y*.7+.15)).r;
  float cross=abs(p.y+(n-.5)*.16);
  float ends=pow(saturate(sin(i.uv.x*3.141593)),.55);
  alpha=(1-smoothstep(.12,.92,cross))*(.48+n*.36+flow*.16)*ends;
  rgb*=.35+.4*saturate(1-cross)+flow*.25;
 } else if(mode>4.5) {
  float noise=tex2D(_NoiseTex,float2(i.uv.x*2-_FxTime*.6,i.uv.y*.8)).r;
  if(mode<5.5) {
   float taper=.9*pow(saturate(sin(i.uv.x*3.141593)),.8);
   float2 uv=float2(i.uv.x,.5+(i.uv.y-.5)/max(taper,.015));
   float4 beam=tex2D(_BeamTex,uv);
   alpha=beam.r*beam.a*smoothstep(0,.10,i.uv.x)*(1-smoothstep(.98,1,i.uv.x));
   rgb*=.8+noise*.25;
  } else if(mode<6.5) {
   float2 uv=float2(i.uv.x*1.1-_FxTime*.9,i.uv.y+(noise-.5)*.10);
   float4 flow=tex2D(_FlowTex,uv);float ends=smoothstep(0,.07,i.uv.x)*(1-smoothstep(.88,1,i.uv.x));
   alpha=flow.r*flow.a*ends*(1-smoothstep(.82,1,abs(p.y)));
   rgb*=.65+noise*.55;
  } else {
   // Trail48's authored tip is on the left. Flip only U so the fan trails behind.
   float2 uv=float2(1-i.uv.x,i.uv.y+(noise-.5)*.045);
   float4 fan=tex2D(_CoverTex,uv);alpha=fan.r*fan.a*(.55+noise*.45);
   if(mode>7.5)alpha*=.4+.6*tex2D(_FlowTex,float2(i.uv.x-_FxTime*2,i.uv.y)).r;
   rgb*=.7+noise*.4;
  }
 } else if(mode<.5) {
  float n=tex2D(_NoiseTex,float2(i.uv.x*.7-_FxTime*.55,i.uv.y*.65)).r;
  float n2=tex2D(_NoiseTex,float2(i.uv.x*1.4+_FxTime*.22,i.uv.y*.8)).r;
  float envelope=pow(saturate(sin(i.uv.x*3.141593)),.7);
  float width=envelope*(.58+(n-.5)*.42);
  alpha=(1-smoothstep(width*.35,width,abs(p.y)))*(.55+.45*smoothstep(.05,.85,n2))*envelope;
  rgb*=.42+n*.85;
 } else if(mode<1.5) {
  float n=tex2D(_NoiseTex,float2(i.uv.x*3-_FxTime*1.6,.4)).r;
  float edge=exp(-pow((p.y+(n-.5)*.10)*10,2));
  alpha=edge*pow(saturate(sin(i.uv.x*3.141593)),.5);
 } else if(mode<2.5) {
  float r=dot(p,p);alpha=exp(-r*5)*(1-smoothstep(.75,1,r));
  rgb*=.5+exp(-r*55)*1.8;
 } else if(mode>3.5) {
  float n=tex2D(_NoiseTex,float2(i.uv.x*2-_FxTime*.8,i.uv.y*2+_FxTime*.12)).r;
  float radius=length(p);
  float shell=exp(-pow((radius-.88+(n-.5)*.07)*16,2));
  float haze=(1-smoothstep(.25,1,radius))*(.035+n*.07);
  float broken=smoothstep(.18,.8,n);
  alpha=(haze+shell*(.045+broken*.16))*(1-smoothstep(.96,1.04,radius));
  rgb*=.6+n*.6;
 } else {
  float r=dot(p,p);float core=exp(-r*60);
  float rays=exp(-p.y*p.y*200-p.x*p.x*12)+exp(-p.x*p.x*200-p.y*p.y*12);
  alpha=saturate(core+rays*.30+exp(-r*8)*.1);
 }
 return half4(rgb,saturate(alpha*i.color.a*_GlobalOpacity));
}
ENDHLSL
Pass { Tags { "LightMode"="UniversalForward" } Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
ENDHLSL
}
Pass { Tags { "LightMode"="Universal2D" } Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
ENDHLSL
}
}}
