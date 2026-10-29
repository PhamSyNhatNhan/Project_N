// NOTE: two passes, because the project has two renderers and a camera picks one: the default is the URP 2D Renderer (index 0, draws `Universal2D`) and the Hall's camera uses the
//       3D renderer (index 1, draws `UniversalForward`). A pass tagged for only one of them is silently not drawn by the other (an invisible puppet, no error).
// Visual actor: every part of one puppet is a quad of one mesh. Part colours (skin, eye, ...) and the part alpha live in the vertex colour; the effects that apply to the
// WHOLE actor travel in the second UV set so the shared material keeps batching: x = flash towards white (0..1), y = fade (alpha multiplier).
Shader "PN/VisualActor"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 effect : TEXCOORD1; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 effect : TEXCOORD1; half4 color : COLOR; };
        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv; output.effect = input.effect; output.color = input.color;
#if !defined(UNITY_COLORSPACE_GAMMA)
            half3 c = input.color.rgb; output.color.rgb = c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878);   // sRGB -> linear: part colours are authored as sRGB hex (#ffeddb), the framebuffer is linear
#endif
            return output;
        }
        half4 frag(Varyings input) : SV_Target
        {
            half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            half3 rgb = texel.rgb * input.color.rgb;
            rgb = lerp(rgb, half3(1, 1, 1), saturate(input.effect.x));
            half alpha = texel.a * input.color.a * saturate(input.effect.y);
            return half4(rgb, alpha);
        }
        ENDHLSL
        Pass
        {
            Name "VisualActor2D"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "VisualActor3D"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
