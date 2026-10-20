Shader "Project_N/VFX/GlassGlint"
{
    // A small additive sparkle drawn without a texture: a hot core, a soft round glow around it and a thin four-point star. It is the "glow" of a glass glint that does not
    // depend on a bloom volume. The particle's colour alpha drives its brightness; values are HDR (above 1) so a bloom volume, when there is one, makes it glow further.
    Properties { }
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
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0; float r = length(p);
                float core = exp(-r * r * 38.0);
                float halo = exp(-r * r * 5.5) * 0.45;
                float star = (exp(-abs(p.x) * 26.0) * exp(-abs(p.y) * 3.2) + exp(-abs(p.y) * 26.0) * exp(-abs(p.x) * 3.2)) * (1.0 - smoothstep(0.55, 1.0, r));
                float v = core * 2.6 + halo + star * 1.4;
                half3 color = lerp(half3(0.55, 0.92, 1.0), half3(1.0, 1.0, 1.0), saturate(core));
                return half4(color * v, saturate(i.color.a));
            }
            ENDHLSL
        }
    }
}
