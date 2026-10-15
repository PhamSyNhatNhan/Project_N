Shader "Project_N/VFX/TimeScaleDust"
{
    Properties { _Opacity("Opacity", Range(0,1)) = 1 _GlowTex("Glow mask",2D)="white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "PNVfxDepth.hlsl"
            TEXTURE2D(_GlowTex); SAMPLER(sampler_GlowTex);
            CBUFFER_START(UnityPerMaterial)
            float _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float eyeDepth:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings o; float3 world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world); o.screen = ComputeScreenPos(o.positionCS);
                o.eyeDepth = -TransformWorldToView(world).z; o.color = input.color; o.uv = input.uv; return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_GlowTex,sampler_GlowTex,input.uv).r * _Opacity * input.color.a;
                float rawDepth = PNVfxNearestDepth(input.screen.xy / input.screen.w);
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    rawDepth = 1 - rawDepth;
                    #endif
                    sceneDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                alpha *= saturate((sceneDepth - input.eyeDepth) / 0.08);
                return half4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
