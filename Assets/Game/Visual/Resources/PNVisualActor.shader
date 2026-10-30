// NOTE: two passes, because the project has two renderers and a camera picks one: the default is the URP 2D Renderer (index 0, draws `Universal2D`) and the Hall's camera uses the
//       3D renderer (index 1, draws `UniversalForward`). A pass tagged for only one of them is silently not drawn by the other (an invisible puppet, no error).
// Visual actor: every part of one puppet is a quad of one mesh. Part colours (skin, eye, ...) and the part alpha live in the vertex colour; the effects that apply to the
// WHOLE actor travel in the second UV set so the shared material keeps batching: x = flash towards white (0..1), y = fade (alpha multiplier).
Shader "PN/VisualActor"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _ShadeTex ("Pre-baked shade tiles", 2D) = "white" {}
        _PaperStrength ("Paper grain", Range(0, 0.3)) = 0.025
        _PaperScale ("Paper fibre scale (art pixels)", Range(1, 16)) = 3
        _OutlineWidth ("Cut edge (screen pixels)", Range(0, 2)) = 0.85
        _OutlineColor ("Cut edge ink", Color) = (0.16, 0.12, 0.20, 1)
        _SketchStrength ("Static ink irregularity", Range(0, 0.5)) = 0.12
        _ShadowOffset ("Layer shadow offset (screen pixels)", Vector) = (2, 2.5, 0, 0)
        _ShadowStrength ("Layer shadow opacity", Range(0, 0.6)) = 0.08
        _ShadowColor ("Layer shadow tint", Color) = (0.16, 0.12, 0.20, 1)
        _DrawnAmount ("Drawn ink overlay", Range(0, 1)) = 0.7
        _DryInk ("Dry ink breaks", Range(0, 1)) = 0.35
        _HatchStrength ("Sparse pen hatching", Range(0, 1)) = 0.38
        _HatchCoverage ("Whole drawing hatch coverage", Range(0, 1)) = 1
        _PigmentStrength ("Uneven colour wash", Range(0, 0.6)) = 0.135
        _LineWobble ("Static line waviness (screen px)", Range(0, 1)) = 0.35
        _LooseOutline ("Broken offset sketch contour", Range(0, 0.7)) = 0.32
        _DryFiber ("Dry colour flecks", Range(0, 0.3)) = 0.12
        _OverlapShadow ("Overlap-only paper shadow", Range(0, 0.6)) = 0.34
        _OverlapOffset ("Overlap shadow (screen px)", Vector) = (2,-3.6,0,0)
        _OverlapSoftness ("Overlap shadow softness (screen px)", Range(0,2)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;
        TEXTURE2D(_ShadeTex); SAMPLER(sampler_ShadeTex); float4 _ShadeTex_TexelSize;
        CBUFFER_START(UnityPerMaterial)
        float _PaperStrength, _PaperScale, _OutlineWidth, _SketchStrength, _ShadowStrength;
        float _DrawnAmount, _DryInk, _HatchStrength, _PigmentStrength, _LineWobble, _HatchCoverage;
        float _LooseOutline, _DryFiber;
        float _OverlapShadow,_OverlapSoftness;float4 _OverlapOffset;
        float4 _OutlineColor, _ShadowColor, _ShadowOffset;
        CBUFFER_END
        float _PaperCasterCount;
        float4 _PaperCasterA[128],_PaperCasterB[128],_PaperCasterC[128],_PaperCasterBounds[128],_PaperCasterMeta[128];
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 effect : TEXCOORD1; float4 bounds : TEXCOORD2; float2 layer : TEXCOORD3; half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 effect : TEXCOORD1; float4 bounds : TEXCOORD2; float2 layer : TEXCOORD3;float2 localXY:TEXCOORD4; half4 color : COLOR; };
        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv; output.effect = input.effect; output.color = input.color; output.bounds = input.bounds;
            output.layer=input.layer;output.localXY=input.positionOS.xy;
#if !defined(UNITY_COLORSPACE_GAMMA)
            half3 c = input.color.rgb; output.color.rgb = c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878);   // sRGB -> linear: part colours are authored as sRGB hex (#ffeddb), the framebuffer is linear
#endif
            return output;
        }
        float hashPaper(float2 p)
        {
            p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float paperNoise(float2 p)
        {
            float2 cell = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(hashPaper(cell), hashPaper(cell + float2(1,0)), f.x),
                        lerp(hashPaper(cell + float2(0,1)), hashPaper(cell + 1), f.x), f.y);
        }
        // Sample only this sprite. Expanded gutters must never borrow another atlas tile.
        half4 samplePart(float2 uv, float4 bounds)
        {
            float2 lo = bounds.xy + _MainTex_TexelSize.xy * .5;
            float2 hi = bounds.zw - _MainTex_TexelSize.xy * .5;
            half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uv, lo, hi));
            c.a *= step(bounds.x, uv.x) * step(bounds.y, uv.y) * step(uv.x, bounds.z) * step(uv.y, bounds.w);
            return c;
        }
        // The same as samplePart, on the pre-baked shade tiles (layer.y = 1 marks a vertex whose uv points into them).
        half4 sampleShade(float2 uv, float4 bounds)
        {
            float2 lo = bounds.xy + _ShadeTex_TexelSize.xy * .5, hi = bounds.zw - _ShadeTex_TexelSize.xy * .5;
            half4 c = SAMPLE_TEXTURE2D(_ShadeTex, sampler_ShadeTex, clamp(uv, lo, hi));
            c.a *= step(bounds.x, uv.x) * step(bounds.y, uv.y) * step(uv.x, bounds.z) * step(uv.y, bounds.w);
            return c;
        }
        float paperCasterAlpha(float2 uv,float4 bounds)
        {
            float2 lo=bounds.xy+_MainTex_TexelSize.xy*.5,hi=bounds.zw-_MainTex_TexelSize.xy*.5;
            float alpha=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,clamp(uv,lo,hi),0).a;
            return alpha*step(bounds.x,uv.x)*step(bounds.y,uv.y)*step(uv.x,bounds.z)*step(uv.y,bounds.w);
        }
        float paperOverlap(float2 queryXY,float2 px,float2 py,float receiverLayer)
        {
            float coverage=0;
            [loop]for(int k=0;k<(int)_PaperCasterCount;k++)
            {
                if(_PaperCasterMeta[k].x<=receiverLayer||_PaperCasterMeta[k].y<=0)continue;
                float4 a=_PaperCasterA[k],b=_PaperCasterB[k],c=_PaperCasterC[k];
                float2 e=b.xy-a.xy,f=c.xy-a.xy,q=queryXY-a.xy;
                float det=e.x*f.y-e.y*f.x;if(abs(det)<1e-12)continue;
                float u=(q.x*f.y-q.y*f.x)/det,v=(e.x*q.y-e.y*q.x)/det;
                if(u<0||v<0||u+v>1)continue;
                float2 uv=a.zw+u*(b.zw-a.zw)+v*(c.zw-a.zw);
                float2 soft=(px+py)*_OverlapSoftness;
                float du=(soft.x*f.y-soft.y*f.x)/det,dv=(e.x*soft.y-e.y*soft.x)/det;
                float2 uvSoft=du*(b.zw-a.zw)+dv*(c.zw-a.zw);
                float alpha=paperCasterAlpha(uv,_PaperCasterBounds[k])*.5;
                alpha+=paperCasterAlpha(uv+uvSoft,_PaperCasterBounds[k])*.25;
                alpha+=paperCasterAlpha(uv-uvSoft,_PaperCasterBounds[k])*.25;
                coverage=max(coverage,alpha*_PaperCasterMeta[k].y);
                if(coverage>.999)break;
            }
            return coverage;
        }
        half4 fragDynamic(Varyings input)
        {
            // Derivatives keep the ink/shadow in screen pixels. Cap offsets to fit the 16-art-pixel gutter.
            float2 dx = ddx(input.uv), dy = ddy(input.uv);
            float2 art = input.uv * _MainTex_TexelSize.zw;
            float2 wobble = float2(paperNoise(art / 17), paperNoise(art / 19 + 31)) - .5;
            float2 drawUv = input.uv + (dx * wobble.x + dy * wobble.y) * (_LineWobble * _DrawnAmount * 2);
            half4 texel = samplePart(drawUv, input.bounds);
            float2 maxStep = _MainTex_TexelSize.xy * 10;
            float2 ex = clamp(dx * _OutlineWidth, -maxStep, maxStep);
            float2 ey = clamp(dy * _OutlineWidth, -maxStep, maxStep);
            half edge = 0;
            if (_OutlineWidth > 0)
            {
                edge = max(max(samplePart(drawUv + ex, input.bounds).a, samplePart(drawUv - ex, input.bounds).a),
                           max(samplePart(drawUv + ey, input.bounds).a, samplePart(drawUv - ey, input.bounds).a));
            }
            float fibreScale = max(1, _PaperScale);
            float footprint = max(length(dx * _MainTex_TexelSize.zw), length(dy * _MainTex_TexelSize.zw));
            float fine = (paperNoise(art / fibreScale) - .5) / (1 + footprint / fibreScale);
            float broad = paperNoise(art / 28) - .5;
            half3 rgb = texel.rgb * input.color.rgb;
            rgb *= 1 + _PaperStrength * (fine * 1.4 + broad * .6);
            // Ink-on-paper overlay: line wear, faint second pen stroke, sparse tonal hatching.
            // Source PNG remains untouched; the pattern follows each part, never time.
            if (_DrawnAmount > 0)
            {
                half lum = dot(rgb, half3(.2126, .7152, .0722));
                half4 n0 = samplePart(drawUv + dx * 1.6, input.bounds);
                half4 n1 = samplePart(drawUv - dx * 1.6, input.bounds);
                half4 n2 = samplePart(drawUv + dy * 1.6, input.bounds);
                half4 n3 = samplePart(drawUv - dy * 1.6, input.bounds);
                half neighbour = max(max(dot(n0.rgb * input.color.rgb, half3(.2126,.7152,.0722)) * n0.a,
                                         dot(n1.rgb * input.color.rgb, half3(.2126,.7152,.0722)) * n1.a),
                                     max(dot(n2.rgb * input.color.rgb, half3(.2126,.7152,.0722)) * n2.a,
                                         dot(n3.rgb * input.color.rgb, half3(.2126,.7152,.0722)) * n3.a));
                half narrowInk = saturate((neighbour - lum) * 5) * (1 - smoothstep(.035, .13, lum));
                float wear = smoothstep(.52, .78, paperNoise(art / 4.5 + 71));
                rgb = lerp(rgb, half3(.18,.145,.12), narrowInk * wear * _DryInk * _DrawnAmount * .7);
                float wash = paperNoise(art / 42 + 11) - .5;
                rgb *= 1 + wash * _PigmentStrength * _DrawnAmount;
                // Cross-stroke is restrained to colour midtones, leaving the mask face readable.
                float tonal = smoothstep(.025,.07,lum) * (1 - smoothstep(.18,.45,lum));
                float patch = smoothstep(.43,.65,paperNoise(art / 95 + 19));
                float strokeCoord = (art.x * .8 + art.y * .6) / 30 + .12 * paperNoise(art / 26);
                float dist = abs(frac(strokeCoord) - .5);
                float aa = max(fwidth(strokeCoord), .008);
                float stroke = 1 - smoothstep(.018, .018 + aa, dist);
                float broken = smoothstep(.23,.42,paperNoise(art / 12 + 3));
                // Whole-drawing version: light areas retain every third stroke, fainter;
                // dark/mid colours keep denser strokes. No hatch is laid over solid ink.
                float brightness = smoothstep(.18,.82,lum);
                float oneInThree = 1 - step(.5, fmod(abs(floor(strokeCoord)),3));
                float density = lerp(1,oneInThree,brightness);
                float allTonal = lerp(.95,.30,brightness) * smoothstep(.008,.035,lum);
                float allPatch = .35 + .65 * patch;
                float minNeighbour = min(min(dot(n0.rgb * input.color.rgb,half3(.2126,.7152,.0722)),dot(n1.rgb * input.color.rgb,half3(.2126,.7152,.0722))),
                                         min(dot(n2.rgb * input.color.rgb,half3(.2126,.7152,.0722)),dot(n3.rgb * input.color.rgb,half3(.2126,.7152,.0722))));
                float nearFineInk = brightness * (1 - smoothstep(.025,.08,minNeighbour));
                float allHatch = allTonal * allPatch * density * (1 - nearFineInk * .85);
                float hatch = lerp(tonal * patch,allHatch,_HatchCoverage);
                rgb *= 1 - stroke * hatch * broken * _HatchStrength * _DrawnAmount;
                half4 ghost = samplePart(drawUv + dx * .95 + dy * .55, input.bounds);
                float ghostLum = dot(ghost.rgb * input.color.rgb, half3(.2126,.7152,.0722));
                float trace = (1 - smoothstep(.025,.07,ghostLum)) * ghost.a * smoothstep(.08,.22,lum);
                rgb = lerp(rgb, _OutlineColor.rgb, trace * .22 * _DrawnAmount);
                float flecks = smoothstep(.68,.85,paperNoise(float2(art.x*.3,art.y*1.6)/3.5+7));
                float paintOnly = smoothstep(.04,.12,lum) * (1-smoothstep(.65,.95,lum));
                rgb = lerp(rgb,half3(.82,.79,.74),flecks * paintOnly * _DryFiber * _DrawnAmount / (1+footprint/4));
            }
            if(_OverlapShadow>0&&texel.a>.001)
            {
                float2 px=ddx(input.localXY),py=ddy(input.localXY);
                float2 queryXY=input.localXY-px*_OverlapOffset.x-py*_OverlapOffset.y;
                float overlap=paperOverlap(queryXY,px,py,input.layer.x);
                rgb=lerp(rgb,rgb*half3(.48,.43,.58),overlap*_OverlapShadow);
            }
            half ink = saturate(edge - texel.a) * _OutlineColor.a;
            ink *= 1 - _SketchStrength * (.5 + broad); // fixed to the art; never crawls with time
            half faceAlpha = texel.a + ink * (1 - texel.a);
            half3 facePremul = rgb * texel.a + _OutlineColor.rgb * ink * (1 - texel.a);
            float2 offset = clamp(dx * _ShadowOffset.x + dy * _ShadowOffset.y, -maxStep, maxStep);
            half shadow = _ShadowStrength > 0 ? samplePart(input.uv - offset, input.bounds).a * _ShadowStrength * _ShadowColor.a : 0;
            // A second, thin, interrupted pen contour; alternating offsets put pieces
            // inside and outside the main contour rather than making a uniform drop shadow.
            float2 looseShift = clamp(dx * (paperNoise(art/83+13)-.5)*4 + dy * (paperNoise(art/79+47)-.5)*4,-maxStep,maxStep);
            float2 looseUv = drawUv + looseShift;
            half looseCentre = samplePart(looseUv,input.bounds).a;
            half looseEdge = max(max(samplePart(looseUv+dx*.55,input.bounds).a,samplePart(looseUv-dx*.55,input.bounds).a),
                                 max(samplePart(looseUv+dy*.55,input.bounds).a,samplePart(looseUv-dy*.55,input.bounds).a));
            float dash = smoothstep(.42,.62,paperNoise(art/19+37));
            half loose = saturate(looseEdge-looseCentre)*dash*_LooseOutline*_DrawnAmount;
            half backAlpha = shadow + loose*(1-shadow);
            half3 backPremul = _ShadowColor.rgb*shadow + _OutlineColor.rgb*loose*(1-shadow);
            half alpha = faceAlpha + backAlpha * (1 - faceAlpha);
            half3 premul = facePremul + backPremul * (1 - faceAlpha);
            rgb = alpha > .0001 ? premul / alpha : half3(0,0,0);
            rgb = lerp(rgb, half3(1, 1, 1), saturate(input.effect.x));
            return half4(rgb, alpha * input.color.a * saturate(input.effect.y));
        }
        // PN_BAKED: the atlas was baked with the ink marks (PNInkBake), so only what must stay dynamic is done here: part tint, overlap shadow, flash and fade.
        half4 frag(Varyings input) : SV_Target
        {
#if defined(PN_BAKED)
            half4 texel = input.layer.y > .5 ? sampleShade(input.uv, input.bounds) : samplePart(input.uv, input.bounds);
            half3 rgb = texel.rgb * input.color.rgb;
            if (_OverlapShadow > 0 && texel.a > .001)
            {
                float2 px = ddx(input.localXY), py = ddy(input.localXY);
                float2 queryXY = input.localXY - px * _OverlapOffset.x - py * _OverlapOffset.y;
                float overlap = paperOverlap(queryXY, px, py, input.layer.x);
                rgb = lerp(rgb, rgb * half3(.48,.43,.58), overlap * _OverlapShadow);
            }
            rgb = lerp(rgb, half3(1, 1, 1), saturate(input.effect.x));
            return half4(rgb, texel.a * input.color.a * saturate(input.effect.y));
#else
            return fragDynamic(input);
#endif
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
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ PN_BAKED
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
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ PN_BAKED
            ENDHLSL
        }
    }
}
