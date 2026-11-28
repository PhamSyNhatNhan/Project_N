// Light on a puppet (Gura in Burst). One shader, two uses chosen by `_Mode`: 0 = a drawn eye turned into light, 1 = a soft round or ribbon of light with no texture (uv runs -1..1 across it). Premultiplied: alpha 0 adds light, alpha 1 covers.
// NOTE: two passes, as every effect here: the 2D renderer draws `Universal2D`, the 3D renderer draws `UniversalForward`.
Shader "Project_N/VFX/GuraGlow"
{
    Properties
    {
        _MainTex ("Puppet atlas", 2D) = "white" {}
        _Mode ("0 eye, 1 soft light", Float) = 0
        _Color ("Light", Vector) = (0.0, 0.68, 1.0, 0)
        _Core ("Hot centre", Vector) = (0.75, 1.0, 1.0, 0)
        _Opacity ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
        float _Mode;
        float4 _Color;
        float4 _Core;
        float _Opacity;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 bounds : TEXCOORD2;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 bounds : TEXCOORD2;
            half4 color : COLOR;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.bounds = input.bounds;
            output.color = input.color;
            return output;
        }

        half4 Part(float2 uv, float4 bounds)
        {
            float2 low = bounds.xy + _MainTex_TexelSize.xy * .5;
            float2 high = bounds.zw - _MainTex_TexelSize.xy * .5;
            half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uv, low, high));
            texel.a *= step(bounds.x, uv.x) * step(bounds.y, uv.y) * step(uv.x, bounds.z) * step(uv.y, bounds.w);
            return texel;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            if (_Mode > .5)
            {
                float far = saturate(length(input.uv));
                float soft = pow(1 - far, 2) * input.color.a * _Opacity;
                return half4(lerp(_Color.rgb, _Core.rgb, pow(1 - far, 4)) * soft, soft * .2);
            }
            half4 texel = Part(input.uv, input.bounds);
            // The light of the drawing is the lamp, its dark outline stays: the eye keeps its shape and its expression.
            float lit = texel.a * smoothstep(.35, .75, max(texel.r, max(texel.g, texel.b)));
            float cover = lit * input.color.a * _Opacity;
            clip(cover - .004);
            // Deep light at the rim of the eye, hot in the middle of the drawing: a white eye has to read as lit, not as the same white.
            float hot = smoothstep(.9, 1.0, min(Part(input.uv + float2(_MainTex_TexelSize.x * 14, 0), input.bounds).r, Part(input.uv - float2(_MainTex_TexelSize.x * 14, 0), input.bounds).r)
                * min(Part(input.uv + float2(0, _MainTex_TexelSize.y * 18), input.bounds).r, Part(input.uv - float2(0, _MainTex_TexelSize.y * 18), input.bounds).r));
            return half4(lerp(_Color.rgb, _Core.rgb, hot * .8) * cover, cover);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
