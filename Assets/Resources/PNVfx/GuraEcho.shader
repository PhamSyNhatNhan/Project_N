// A puppet's posed mesh drawn as flat water that melts by height with a lit edge (Gura attack 3: the coat over Gura and the water doubles; her Dash: the flash, the after-images and the held coat).
// `_Fill` < 1 thins the inside and keeps the drawn lines, so the colours below show through. `_Outline` > 0 draws only a ring of light around the parts: drawn behind the puppet, what is left is its outer contour.
// NOTE: two passes, as every effect here: the 2D renderer draws `Universal2D`, the 3D renderer draws `UniversalForward`.
// Samples only inside the sprite's own atlas bounds (uv2), like PN/VisualActor: the quads reach past the art.
Shader "Project_N/VFX/GuraEcho"
{
    Properties
    {
        _MainTex ("Puppet atlas", 2D) = "white" {}
        _NoiseTex ("Flow noise", 2D) = "gray" {}
        _Dissolve ("Melted", Range(0,1)) = 0
        _FromTop ("Melt the top first", Float) = 1
        _Opacity ("Opacity", Float) = 1
        _FxTime ("Action age", Float) = 0
        _Height ("Body height", Float) = .7
        _Waterline ("Boundary by height only", Float) = 0
        _EdgeLight ("Lit boundary", Float) = 1
        _Fill ("Strength of the inside", Range(0,1)) = 1
        _Outline ("Ring only, this wide (metres)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_NoiseTex);
        SAMPLER(sampler_NoiseTex);
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
        float _Dissolve;
        float _FromTop;
        float _Opacity;
        float _FxTime;
        float _Height;
        float _Waterline;
        float _EdgeLight;
        float _Fill;
        float _Outline;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float2 effect : TEXCOORD1;
            float4 bounds : TEXCOORD2;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 effect : TEXCOORD1;
            float4 bounds : TEXCOORD2;
            half4 color : COLOR;
            float2 body : TEXCOORD3;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.effect = input.effect;
            output.bounds = input.bounds;
            output.color = input.color;
            output.body = input.positionOS.xy;
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

        // How much of this height is still there (`x`) and the light on the melting boundary (`y`). Low `keep` goes first.
        float2 Melt(float2 body, float light, float grain)
        {
            float height = saturate(body.y / max(.1, _Height));
            float keep = lerp(height, 1 - height, _FromTop) * .65 + light * .2 + grain * .15;
            // A waterline: a heaving level that ignores the drawing's shades, so it stays where it is put.
            float wave = sin(body.x * 24 + _FxTime * 8) * .022 + sin(body.x * 53 - _FxTime * 11) * .012;
            keep = lerp(keep, lerp(height, 1 - height, _FromTop) + wave, step(.5, _Waterline));
            float melting = step(.001, _Dissolve);
            float cut = lerp(1, smoothstep(_Dissolve - .025, _Dissolve + .025, keep), melting);
            float edge = exp(-pow((keep - _Dissolve) * 24, 2)) * melting * _EdgeLight;
            return float2(saturate(cut + edge * .5) * (1 - smoothstep(.97, 1, _Dissolve)), edge);
        }

        half4 Frag(Varyings input) : SV_Target
        {
            half4 texel = Part(input.uv, input.bounds);
            float grain = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.body * 6.5 + float2(_FxTime * .15, -_FxTime * .7)).r;
            float flow = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.body * float2(2.2, 1.1) + float2(0, _FxTime * 1.4)).r;
            float perMetre = length(fwidth(input.uv * _MainTex_TexelSize.zw)) / max(length(fwidth(input.body)), 1e-6);
            if (_Outline > 0)
            {
                // The quads carry a clear gutter of 16 texels around the art: the ring has to stay inside it.
                float2 wide = min(_Outline * perMetre, 14.0) * _MainTex_TexelSize.xy;
                float inner = 0, outer = 0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float2 side;
                    sincos(i * .785398, side.y, side.x);
                    inner = max(inner, Part(input.uv + side * wide * .55, input.bounds).a);
                    outer = max(outer, Part(input.uv + side * wide, input.bounds).a);
                }
                float ring = saturate(max(inner, outer * .55) - texel.a) * input.color.a * input.effect.y;
                clip(ring - .004);
                float2 rest = Melt(input.body, .5, grain);
                half3 glow = lerp(half3(.10, .52, .92), half3(.62, .96, 1.0), inner);
                glow += smoothstep(.55, .82, flow) * half3(.16, .22, .20) + rest.y * half3(.55, .92, 1.0);
                return half4(glow, ring * rest.x * _Opacity);
            }
            float alpha = texel.a * input.color.a * input.effect.y;
            clip(alpha - .004);
            float light = max(texel.r, max(texel.g, texel.b));
            // The rim of each drawn part, a pixel and a half inside its edge: it keeps the hair, the head and the clothes apart in the water.
            float2 reach = max(fwidth(input.uv), _MainTex_TexelSize.xy) * 1.6;
            float inside = min(min(Part(input.uv + float2(reach.x, 0), input.bounds).a, Part(input.uv - float2(reach.x, 0), input.bounds).a),
                min(Part(input.uv + float2(0, reach.y), input.bounds).a, Part(input.uv - float2(0, reach.y), input.bounds).a));
            float rim = saturate(texel.a - inside);
            float2 left = Melt(input.body, light, grain);
            // The drawing's own shades become the water's: lines stay deep, light areas turn aqua, so the face and the clothes stay readable. Slow bright veins move over it.
            float shade = dot(texel.rgb, half3(.3, .59, .11));
            half3 water = lerp(half3(.004, .028, .14), half3(.012, .24, .64), smoothstep(.02, .34, shade));
            water = lerp(water, half3(.16, .62, .90), smoothstep(.42, .92, shade));
            water += smoothstep(.62, .84, flow) * half3(.04, .14, .18);
            water = lerp(water, half3(.48, .90, 1.0), rim * .85);
            water += left.y * half3(.55, .92, 1.0);
            float thick = lerp(_Fill, 1, rim * lerp(.1, 1, _Fill * _Fill));
            return half4(water, alpha * left.x * thick * _Opacity * .94);
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
