Shader "Project_N/Starweaver/FacetedModel"
{
 Properties { _BaseColor ("Surface color", Color) = (.08,.06,.14,1) }
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
   half4 _BaseColor;
   CBUFFER_END
   struct A { float4 positionOS:POSITION;float3 normal:NORMAL; };
   struct V { float4 positionCS:SV_POSITION;float3 normal:TEXCOORD0; };
   V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.normal=TransformObjectToWorldNormal(a.normal);return o;}
   half4 Frag(V i):SV_Target
   {
    float3 n=normalize(i.normal);
    float light=.62+.5*abs(dot(n,normalize(float3(-.4,.75,-.6))));
    light+=.08*abs(n.z);
    return half4(_BaseColor.rgb*light,_BaseColor.a);
   }
   ENDHLSL
  }
 }
}
