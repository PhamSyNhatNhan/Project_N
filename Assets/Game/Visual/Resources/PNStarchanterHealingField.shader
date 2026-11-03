Shader "PN/StarchanterHealingField"
{
 Properties { _FxTime("Entity time",Float)=0 _Opacity("Opacity",Float)=1 _Radius("Field radius",Float)=2 _FieldCenter("Field center",Vector)=(0,0,0,0) _CloudTex("Cloud mask",2D)="white"{} _FlowTex("Cloud flow",2D)="gray"{} _Layer("Cloud / glow / wave / smoke",Float)=0 _DstBlend("Destination blend",Float)=10 }
 SubShader
 {
  Tags {"RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline"}
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  float _FxTime,_Layer,_Opacity,_Radius;float4 _FieldCenter;sampler2D _CloudTex,_FlowTex;
  struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
  struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 positionWS:TEXCOORD1;};
  V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
  float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
  float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
  half4 frag(V i):SV_Target
  {
   if(_Layer>2.5)
   {
    // Preserve the authored smoke silhouette. Particle lifetime, size and rotation supply motion.
    float4 smoke=tex2D(_CloudTex,i.uv);
    float edge=1-smoothstep(_Radius-.18,_Radius+.04,distance(i.positionWS.xz,_FieldCenter.xz));
    return half4(i.color.rgb,smoke.r*smoke.a*i.color.a*_Opacity*edge);
   }
   float2 flowUV=i.uv*float2(1.5,2)+float2(-_FxTime*.10,_FxTime*.055);
   float flow=tex2D(_FlowTex,frac(flowUV)).r;
   if(_Layer>1.5)
   {
    float angle=i.uv.x*6.2831853;
    float n=noise(float2(cos(angle),sin(angle))*8+_FxTime*.15);
    float rim=exp(-pow((i.uv.y-.52+(n-.5)*.13)*4.0,2));
    return half4(i.color.rgb*(.85+n*.6),i.color.a*rim*(.45+.55*flow));
   }
   float2 uv=saturate(i.uv+float2((flow-.5)*.06,(flow-.5)*.12));
   float mask=tex2D(_CloudTex,uv).r*.5+tex2D(_CloudTex,saturate(uv+float2(.14,.10))).r*.25+tex2D(_CloudTex,saturate(uv+float2(-.14,.08))).r*.25;
   float detail=tex2D(_FlowTex,frac(i.uv*3+float2(_FxTime*.07,-_FxTime*.045))).r;
   // An irregular elliptical falloff vanishes before every card edge, including its corners.
   float2 edgeP=(i.uv-.5)*2;
   float contour=length(edgeP)+.14*(flow-.5)+.06*(detail-.5);
   float envelope=1-smoothstep(.48,.98,contour);
   mask=lerp(mask,.28+.72*flow,.55)*envelope;
   float alpha=mask*i.color.a*(.4+.6*flow);
   if(_Layer>.5) return half4(lerp(i.color.rgb,float3(.66,1,.84),detail*.4),alpha*smoothstep(.08,.9,detail)*.5);
   return half4(i.color.rgb*(.7+detail*.5),alpha);
  }
  ENDHLSL
  Pass {Tags {"LightMode"="UniversalForward"} Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   ENDHLSL
  }
  Pass {Tags {"LightMode"="Universal2D"} Blend SrcAlpha [_DstBlend] ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   ENDHLSL
  }
 }
}
