// EN: The flying slash of Gura (drawn by PNGuraSlashWave), drawn lying flat and flying toward +x; whoever draws it turns the whole sheet. A dome of water coming round side first: a white
//     rim with a glow, a see-through body in which curling waves (the texture _Glyph) stream back and ripple, and a soft wavering tail. uv in half heights of the dome: x along the flight, y across.
// VI: Nhat chem bay cua Gura, dung nam ngang bay ve +x: vom nuoc vien trang, song cuon troi trong than, duoi nuoc mem uon luon.
Shader "Project_N/VFX/GuraSlashComet"
{
    Properties
    {
        [NoScaleOffset] _Glyph ("Curling waves (distance)", 2D) = "white" {}
        [HideInInspector] _Age ("Age", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+22" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        // Premultiplied: the glow adds light, the water covers what is behind it.
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Age;
            float _Seed;
            CBUFFER_END
            TEXTURE2D(_Glyph);
            SAMPLER(sampler_Glyph);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p) { return Noise(p) * .67 + Noise(p * 2.1 + 7.3) * .33; }

            // Noise without creases.
            float Soft(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // How far (in uv) from the edge of a curling wave: below 0 inside it. The tile repeats along x; the lower half is the upper one mirrored and half a tile on.
            float Waves(float u, float y)
            {
                return (SAMPLE_TEXTURE2D_LOD(_Glyph, sampler_Glyph, float2(u / 2.4 + (y < 0.0 ? .47 : 0.0), abs(y) / 1.28), 0).r - .5) * .32;
            }

            // Laid over what is already there (both premultiplied).
            void Over(inout float3 colour, inout float alpha, float3 top, float cover)
            {
                colour = top * cover + colour * (1.0 - cover);
                alpha = cover + alpha * (1.0 - cover);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                float t = _Age + _Seed;

                const float3 pale = float3(.52, .86, 1.0), azure = float3(.05, .46, 1.0), blue = float3(.012, .13, .82), navy = float3(.004, .028, .3), night = float3(.001, .008, .09);

                float3 colour = 0;
                float alpha = 0;
                float pixel = fwidth(p.x);

                // The water moves: everything inside is read through this slow swell and a quicker ripple running back.
                float2 swell = float2(Soft(p * 1.7 + float2(t * 2.6, 0.0)), Soft(p * 1.7 + float2(t * 2.6 + 5.2, 1.7))) - .5;
                float2 ripple = float2(Soft(p * 4.3 + float2(t * 7.0, t * .7)), Soft(p * 4.3 + float2(t * 7.0 + 9.1, 3.3 - t * .7))) - .5;
                float2 q = p + swell * .26 + ripple * .07;
                q.y += sin(p.x * 4.2 + t * 15.0) * .035;

                // How far outside the dome a point is (below 0: inside): a half oval in front, widest at its back, so it never draws in; behind that it only streams on.
                float2 cap = float2(p.x + .6, p.y);
                float2 radii = float2(1.52, 1.17);
                float ahead = cap.x > 0.0 ? (length(cap / radii) - 1.0) * radii.y : abs(cap.y) - radii.y;
                ahead += (Soft(float2(p.x * 2.6 + t * 6.0, p.y * 2.6)) - .5) * .09 * smoothstep(.75, -.5, p.x);

                // The tail: soft water streaming back, waving as one body.
                {
                    float back = saturate((-p.x - .8) / 3.7);
                    float y = p.y + sin(p.x * 2.3 + t * 12.0 + p.y * 1.3) * (.03 + .17 * back) + swell.y * (.1 + .3 * back);
                    float envelope = 1.0 - smoothstep(.4, 1.08, abs(y) / lerp(1.14, .68, back));
                    float reach = smoothstep(-.1, -1.2, p.x) * (1.0 - smoothstep(.4, 1.0, back));

                    float puff = Soft(float2(p.x * .95 + t * 4.4, y * 2.2)) * .65 + Soft(float2(p.x * 2.4 + t * 7.6 + 7.0, y * 4.6)) * .35;
                    float3 tint = lerp(night, navy, smoothstep(.32, .6, puff));
                    tint = lerp(tint, blue, smoothstep(.52, .82, puff) * (1.0 - back * .6));
                    Over(colour, alpha, tint, smoothstep(.2, .75, puff) * envelope * reach * .42);

                    // Threads of light, long and bending, feathered at every side.
                    float wide = Soft(float2(p.x * .85 + t * 5.0 + 3.1, y * 5.4 + swell.x * 2.0));
                    float fine = Soft(float2(p.x * 1.4 + t * 8.2 + 17.0, y * 10.0 + swell.x * 3.0 + ripple.y));
                    float carry = envelope * reach * smoothstep(.15, .6, puff + .15);
                    Over(colour, alpha, lerp(blue, azure, 1.0 - back), smoothstep(.52, .9, wide) * carry * .52);
                    Over(colour, alpha, lerp(azure, pale, smoothstep(.75, .95, fine) * (1.0 - back)), pow(smoothstep(.58, .93, fine), 1.6) * carry * (1.0 - back * .6) * .7);
                }

                // The body: see-through water with waves curling in it.
                {
                    float depth = saturate((.92 - p.x) / 2.7);
                    // Behind, it comes apart into long wavering tongues.
                    float fray = Fbm(float2(q.x * 1.3 + t * 5.0, q.y * 3.2));
                    float whole = 1.0 - smoothstep(-.18, .22, smoothstep(-.35, -2.1, p.x) - fray);
                    float inside = (1.0 - smoothstep(-.04, .01, ahead)) * whole * smoothstep(-2.3, -1.4, p.x);

                    float3 water = lerp(azure, blue, smoothstep(.1, .45, depth));
                    water = lerp(water, navy, smoothstep(.5, .85, depth));
                    // Thicker toward its round side, as a dome of water is, and thin in the middle.
                    float round_ = smoothstep(.75, .0, -ahead);
                    Over(colour, alpha, water, inside * saturate(lerp(.68, .2, smoothstep(.2, .9, depth)) + round_ * .18));

                    // Wide soft sheets of light and dark moving through.
                    float sheet = Soft(q * float2(1.5, 2.2) + float2(t * 3.2, 4.0));
                    Over(colour, alpha, night, smoothstep(.5, .2, sheet) * smoothstep(.1, .4, depth) * inside * .8);
                    Over(colour, alpha, lerp(pale, azure, smoothstep(.1, .6, depth)), smoothstep(.55, .85, sheet) * inside * (1.0 - depth) * .55);

                    // Far waves: smaller and faint, slower - they give it depth.
                    float far = Waves((q.x + q.y * q.y * .2) * 1.45 + t * 2.3 + 1.1, q.y * 1.45 + .34);
                    float fade = 1.0 - smoothstep(.62, .98, depth);
                    Over(colour, alpha, azure, (1.0 - smoothstep(-.02, .06, far)) * inside * fade * .32);

                    // Near waves, wrapped round the dome and streaming back.
                    float2 bend = q + ripple * .05;
                    float wave = Waves(bend.x + bend.y * bend.y * .45 + t * 2.9, bend.y);
                    float3 lit = lerp(pale, azure, smoothstep(.2, .62, depth));
                    lit = lerp(lit, blue, smoothstep(.74, .98, depth));
                    float within = 1.0 - smoothstep(-pixel, pixel, wave);
                    // A clear body, brighter toward its edge, with the bright line of the water's skin on it.
                    Over(colour, alpha, lit, within * inside * fade * lerp(.2, .72, smoothstep(-.09, -.005, wave)));
                    Over(colour, alpha, azure, exp(-abs(wave) * 26.0) * inside * fade * .45);
                    float width = .006 + .016 * smoothstep(.25, .75, Soft(q * 2.3 + float2(t * 4.0, 8.0)));
                    Over(colour, alpha, lerp(lit, float3(1.0, 1.0, 1.0), (1.0 - depth) * .45), (1.0 - smoothstep(width - pixel, width + pixel, abs(wave))) * inside * fade);

                    // Light gathered by the moving surface: a fine net near the front.
                    float net = abs(Soft(q * 4.6 + float2(t * 5.5, 0.0)) - .5);
                    Over(colour, alpha, pale, (1.0 - smoothstep(.0, .07, net)) * inside * smoothstep(.55, .1, depth) * .45);
                    // Soft bright patches behind the nose.
                    float glint = smoothstep(.55, .86, Soft(q * 3.0 + float2(t * 3.4 + 11.0, 0.0))) * smoothstep(-.3, .6, p.x);
                    Over(colour, alpha, pale, glint * inside * .75);
                }

                // The rim: one round sweep, thick in front, gone before the dome is at its widest - no straight pieces trailing. Its inner side ripples.
                {
                    float behind = max(0.0, -ahead);
                    float along = smoothstep(-.55, .45, p.x);
                    float thick = lerp(.04, .5, smoothstep(-.45, .88, p.x)) * (.7 + .6 * Soft(float2(p.y * 3.4 + swell.x * 2.0, t * 5.0)));
                    float inside = (1.0 - smoothstep(-.014, .018, ahead)) * along;
                    Over(colour, alpha, azure, (1.0 - smoothstep(thick * .25, thick * 1.8, behind)) * inside * .92);
                    Over(colour, alpha, pale, (1.0 - smoothstep(thick * .2, thick * 1.05, behind)) * inside);
                    Over(colour, alpha, float3(1.0, 1.0, 1.0), (1.0 - smoothstep(thick * .12, thick * .62, behind)) * inside);
                }

                // The glow round it: light only, wide.
                {
                    float away = max(0.0, ahead);
                    float glow = (exp(-away * 9.0) * .62 + exp(-away * 3.0) * .26) * smoothstep(-.02, .016, ahead);
                    // Gone toward the back and before the sheet ends.
                    glow *= smoothstep(-1.2, .45, p.x) * (1.0 - smoothstep(1.3, 1.64, abs(p.y))) * (1.0 - smoothstep(1.15, 1.5, p.x));
                    colour += lerp(blue, azure, exp(-away * 5.0)) * glow;
                }

                return half4(colour * input.color.a, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
