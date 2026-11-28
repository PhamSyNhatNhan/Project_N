// EN: The wave of Gura's rush (a mesh drawn by PNGuraWave). Texture is channel packed (NamuFX caustics): lines in B, soft blotches in G.
// VI: Con song cua cu luot (luoi do PNGuraWave ve).
Shader "Project_N/VFX/GuraWave"
{
    Properties
    {
        _Caustics ("Water caustics", 2D) = "white" {}
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        [HideInInspector] _FxTime ("Time", Float) = 0
        [HideInInspector] _Dissolve ("Dissolve", Float) = 0
        [HideInInspector] _Floor ("Floor strip", Float) = 0
        [HideInInspector] _Flow ("Way of travel", Vector) = (0, 0, 1, 0)
        [HideInInspector] _Curl ("Curl of the crest", Float) = 0
        [HideInInspector] _Calm ("Spent water: foam gone", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+19" "RenderType"="Transparent" }
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Caustics); SAMPLER(sampler_Caustics);
        CBUFFER_START(UnityPerMaterial)
        float _Opacity;
        float _FxTime;
        float _Dissolve;
        float _Floor;
        float4 _Flow;
        float _Curl;
        float _Calm;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 flat : TEXCOORD1; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; float2 flat : TEXCOORD3; float height : TEXCOORD4; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 position = input.positionOS.xyz;
            // The mesh is water, not a solid: slow wide swells run back along it and its sides breathe. Gentle, so the outline stays smooth. uv.x: 0 tail, 1 nose.
            float u = input.uv.x, s = input.uv.y * 2.0 - 1.0, t = _FxTime;
            if (_Floor < .5)
            {
            position.y *= 1.0 + (sin(u * 9.0 + t * 13.0) * .14 + sin(u * 5.0 + t * 8.0 + s * 2.5) * .10) * smoothstep(.1, .5, u);
            position.x *= 1.0 + sin(u * 7.0 + t * 10.0) * .045;
            position.z += sin(s * 3.0 + t * 11.0) * .018 * u * (1.0 - smoothstep(.72, .95, u));
            // The crest leans forward over the nose, more the higher it is: a wave about to break.
            position.z += _Curl * .32 * input.positionOS.y * input.positionOS.y * smoothstep(.35, .85, u);
            }
            output.positionWS = TransformObjectToWorld(position);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            output.flat = input.flat;
            output.height = input.positionOS.y;
            return output;
        }

        float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }
        // Water on the floor: long thin strands along the way of travel. Taken from world positions, so the apron and the strip share one pattern and it stays where it is. `thin`: 0 full, 1 nearly gone.
        half4 StrandsAt(float along, float across, half thin)
        {
            across += (Noise(float2(along * 1.6, across * 3.0)) - .5) * .16;
            half s = Noise(float2(along * .85, across * 8.5)) * .62 + Noise(float2(along * 1.9 + 7.3, across * 21.0)) * .38;
            half strand = smoothstep(.44 + thin * .28, .72 + thin * .18, s);
            half core = smoothstep(.64 + thin * .18, .82 + thin * .14, s);
            // (linear values)
            half3 colour = lerp(half3(.03, .27, .78), half3(.30, .74, 1.0), strand);
            colour = lerp(colour, half3(.92, 1.0, 1.0), core);
            return half4(colour, saturate(.08 + strand * .56 + core * .34));
        }

        half4 Strands(float3 world, half thin, float2 flow)
        {
            return StrandsAt(dot(world.xz, flow) + _FxTime * .35, dot(world.xz, float2(flow.y, -flow.x)), thin);
        }

        // The strip left behind. uv: metres along the way, 0..1 across; flat: x how old (0..1), y alpha.
        half4 FloorWater(Varyings input)
        {
            half old = input.flat.x;
            half4 water = Strands(input.positionWS, old, _Flow.xz);
            half edge = abs(input.uv.y * 2.0 - 1.0);
            // Soft ragged sides: no outline.
            edge += (Noise(float2(input.uv.x * 2.6, input.uv.y * 4.0)) - .5) * .45;
            water.a *= (1.0 - smoothstep(.45, .98, edge)) * (1.0 - smoothstep(.35, 1.0, old)) * input.flat.y * _Opacity;
            clip(water.a - .015);
            return water;
        }

        // A band of water in the air. uv: metres along it, 0..1 across; flat: x 0 its end .. 1 its head, y alpha.
        half4 Frag(Varyings input) : SV_Target
        {
            if (_Floor > .5) return FloorWater(input);
            float u = input.uv.x, v = input.uv.y, t = _FxTime;
            half side = abs(v * 2.0 - 1.0);
            // The water streams from the nose to the tail at least as fast as the wave travels, so the pattern is left behind instead of riding along; the lines are drawn out along the way.
            half4 slow = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, float2(u * .6 + t * 2.4, v * 1.35));
            half warp = slow.g - .55;
            half4 fast = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, float2(u * .55 + t * 3.3 + warp * .10, v * 2.3 + warp * .10));
            // (linear values: the project renders in linear space)
            half3 deep = half3(.001, .02, .22), middle = half3(.001, .13, .66), bright = half3(.008, .40, .94);
            half3 colour = lerp(deep, middle, saturate(u * 1.25 - .12 + warp * .45));
            colour = lerp(colour, bright, smoothstep(.45, .90, u + warp * .18) * .85);
            half3 normal = normalize(input.normalWS);
            half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            half fresnel = 1.0 - saturate(abs(dot(normal, view)));
            colour = lerp(colour, half3(.08, .66, 1.0), smoothstep(.40, .90, fresnel) * .55);
            // Light lines flowing back over the surface.
            colour = lerp(colour, half3(.14, .80, 1.0), saturate(fast.b * smoothstep(.10, .55, u) * .70));
            // Foam: one soft band hugging the outline, thick round the nose and thinning away along the sides, with a wavering inner edge; a thin arc follows behind it.
            half edge = max(side, saturate((u - .72) / .28));
            half band = lerp(0.0, .30, smoothstep(.50, .85, u)) * (1.0 - .72 * _Calm);
            half wave = sin(v * 17.0 + t * 9.0) * .035 + warp * .16;
            half foam = smoothstep(1.0 - band - .05, 1.0 - band + .05, edge + wave) * smoothstep(.55, .78, u);
            // Ripples born behind the nose run back to the tail, swept back at the sides.
            half ripple = frac((u + side * side * .22 + wave * .4) * 2.2 + t * 4.2);
            half arc = smoothstep(.76, .88, ripple) * (1.0 - smoothstep(.92, 1.0, ripple)) * smoothstep(.18, .45, u) * (1.0 - smoothstep(.70, .86, u)) * .5;
            arc *= 1.0 - _Calm;
            colour = lerp(colour, half3(.93, 1.0, 1.0), saturate(max(foam, arc)));
            half alpha = smoothstep(0.0, .36, u) * saturate(.74 + foam * .26 + arc * .2 + fast.b * .15 + fresnel * .2);
            alpha *= 1.0 - smoothstep(.975, 1.0, side) * .5;
            // The crest: its lip tears into foam that keeps breaking and closing.
            half h = input.height;
            half lipZone = smoothstep(.58, .92, h) * smoothstep(.50, .82, u);
            half crestFoam = lipZone * smoothstep(.38, .62, Noise(float2(u * 11.0 - t * 7.0, v * 13.0 + t * 2.0)));
            // The waterline: broken foam where the water meets the floor, running back with the flow.
            half shore = (1.0 - smoothstep(.03, .14, h)) * smoothstep(.12, .45, u);
            shore *= smoothstep(.32, .58, Noise(float2(u * 10.0 + t * 6.0, v * 4.0)));
            // Bubbles inside the water, carried back.
            float2 g = float2(u * 10.0 + t * 2.2, v * 7.0);
            float2 cell = floor(g), f = frac(g);
            float2 spot = float2(Hash(cell + 3.1), Hash(cell + 7.7)) * .5 + .25;
            half d = length(f - spot), r = .18 + .12 * Hash(cell + 1.3);
            half bubble = step(.74, Hash(cell)) * smoothstep(r * .55, r * .8, d) * (1.0 - smoothstep(r * .85, r, d)) * smoothstep(.2, .5, u) * (1.0 - lipZone);
            half white = saturate(max(max(crestFoam * (1.0 - _Calm), shore * (1.0 - .45 * _Calm)), bubble * .7 * (1.0 - _Calm)));
            colour = lerp(colour, half3(.93, 1.0, 1.0), white);
            alpha = max(alpha, white * .92 * smoothstep(0.0, .3, u));
            alpha *= smoothstep(_Dissolve - .08, _Dissolve + .08, slow.g * .8 + u * .2 + .001);
            alpha *= _Opacity;
            clip(alpha - .02);
            return half4(colour, alpha);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
