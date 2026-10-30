// Clone trial: the marks of the PN/VisualActor ink look that are FIXED TO THE IMAGE (grain, wash, dry ink, hatching, ghost trace, broken second contour, cut-edge ink),
// computed once per image when the visual atlas is built. A port of the fragment code of PN/VisualActor with the screen-pixel terms replaced by _Scale art pixels per screen pixel
// (the reference game scale), so the baked image looks like the dynamic shader at that scale. The source image is surrounded by a transparent border so the marks can leave the silhouette.
// Output: straight colour + alpha, no part tint, no flash/fade, no overlap shadow (those stay at run time).
Shader "Hidden/PN/VisualInkBake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Scale ("Art pixels per screen pixel", Float) = 6.17
        _PaperStrength ("Paper grain", Float) = 0.025
        _PaperScale ("Paper fibre scale", Float) = 3
        _OutlineWidth ("Cut edge (screen px)", Float) = 0.55
        _OutlineColor ("Cut edge ink", Color) = (0.16, 0.12, 0.20, 1)
        _SketchStrength ("Static ink irregularity", Float) = 0.12
        _DrawnAmount ("Drawn ink overlay", Float) = 1
        _DryInk ("Dry ink breaks", Float) = 0.9
        _HatchStrength ("Hatching", Float) = 0.95
        _HatchCoverage ("Hatch coverage", Float) = 1
        _PigmentStrength ("Colour wash", Float) = 0.30
        _LineWobble ("Line waviness", Float) = 0.7
        _LooseOutline ("Broken second contour", Float) = 0.32
        _DryFiber ("Dry colour flecks", Float) = 0.12
        _AaScale ("Hatch stroke softness multiplier", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float _AaScale, _Scale, _PaperStrength, _PaperScale, _OutlineWidth, _SketchStrength, _DrawnAmount, _DryInk, _HatchStrength, _HatchCoverage, _PigmentStrength, _LineWobble, _LooseOutline, _DryFiber;
            float4 _OutlineColor;
            float hashPaper(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float paperNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hashPaper(cell), hashPaper(cell + float2(1,0)), f.x), lerp(hashPaper(cell + float2(0,1)), hashPaper(cell + 1), f.x), f.y);
            }
            float4 samplePart(float2 uv)
            {
                float2 lo = _MainTex_TexelSize.xy * .5, hi = 1 - lo;
                float4 c = tex2Dlod(_MainTex, float4(clamp(uv, lo, hi), 0, 0));
                c.a *= step(0, uv.x) * step(0, uv.y) * step(uv.x, 1) * step(uv.y, 1);
                return c;
            }
            float lumOf(float4 c) { return dot(c.rgb, float3(.2126, .7152, .0722)) * c.a; }
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float2 dx = float2(_Scale * _MainTex_TexelSize.x, 0), dy = float2(0, _Scale * _MainTex_TexelSize.y);
                float2 art = uv * _MainTex_TexelSize.zw;
                float2 wobble = float2(paperNoise(art / 17), paperNoise(art / 19 + 31)) - .5;
                float2 drawUv = uv + (dx * wobble.x + dy * wobble.y) * (_LineWobble * _DrawnAmount * 2);
                float4 texel = samplePart(drawUv);
                float2 maxStep = _MainTex_TexelSize.xy * 10;
                float2 ex = clamp(dx * _OutlineWidth, -maxStep, maxStep), ey = clamp(dy * _OutlineWidth, -maxStep, maxStep);
                float edge = 0;
                if (_OutlineWidth > 0)
                    edge = max(max(samplePart(drawUv + ex).a, samplePart(drawUv - ex).a), max(samplePart(drawUv + ey).a, samplePart(drawUv - ey).a));
                float fibreScale = max(1, _PaperScale);
                float footprint = _Scale;
                float fine = (paperNoise(art / fibreScale) - .5) / (1 + footprint / fibreScale);
                float broad = paperNoise(art / 28) - .5;
                float3 rgb = texel.rgb;
                rgb *= 1 + _PaperStrength * (fine * 1.4 + broad * .6);
                if (_DrawnAmount > 0)
                {
                    float lum = dot(rgb, float3(.2126, .7152, .0722));
                    float4 n0 = samplePart(drawUv + dx * 1.6), n1 = samplePart(drawUv - dx * 1.6), n2 = samplePart(drawUv + dy * 1.6), n3 = samplePart(drawUv - dy * 1.6);
                    float neighbour = max(max(lumOf(n0), lumOf(n1)), max(lumOf(n2), lumOf(n3)));
                    float narrowInk = saturate((neighbour - lum) * 5) * (1 - smoothstep(.035, .13, lum));
                    float wear = smoothstep(.52, .78, paperNoise(art / 4.5 + 71));
                    rgb = lerp(rgb, float3(.18, .145, .12), narrowInk * wear * _DryInk * _DrawnAmount * .7);
                    float wash = paperNoise(art / 42 + 11) - .5;
                    rgb *= 1 + wash * _PigmentStrength * _DrawnAmount;
                    float tonal = smoothstep(.025, .07, lum) * (1 - smoothstep(.18, .45, lum));
                    float patch = smoothstep(.43, .65, paperNoise(art / 95 + 19));
                    float strokeCoord = (art.x * .8 + art.y * .6) / 30 + .12 * paperNoise(art / 26);
                    float dist = abs(frac(strokeCoord) - .5);
                    float aa = max(_Scale * (.8 + .6) / 30 * _AaScale, .008);   // fwidth(strokeCoord) at the reference scale: the dynamic shader softens the strokes by one screen pixel
                    float stroke = 1 - smoothstep(.018, .018 + aa, dist);
                    float broken = smoothstep(.23, .42, paperNoise(art / 12 + 3));
                    float brightness = smoothstep(.18, .82, lum);
                    float oneInThree = 1 - step(.5, fmod(abs(floor(strokeCoord)), 3));
                    float density = lerp(1, oneInThree, brightness);
                    float allTonal = lerp(.95, .30, brightness) * smoothstep(.008, .035, lum);
                    float allPatch = .35 + .65 * patch;
                    float minNeighbour = min(min(dot(n0.rgb, float3(.2126,.7152,.0722)), dot(n1.rgb, float3(.2126,.7152,.0722))), min(dot(n2.rgb, float3(.2126,.7152,.0722)), dot(n3.rgb, float3(.2126,.7152,.0722))));
                    float nearFineInk = brightness * (1 - smoothstep(.025, .08, minNeighbour));
                    float allHatch = allTonal * allPatch * density * (1 - nearFineInk * .85);
                    float hatch = lerp(tonal * patch, allHatch, _HatchCoverage);
                    rgb *= 1 - stroke * hatch * broken * _HatchStrength * _DrawnAmount;
                    float4 ghost = samplePart(drawUv + dx * .95 + dy * .55);
                    float ghostLum = dot(ghost.rgb, float3(.2126, .7152, .0722));
                    float trace = (1 - smoothstep(.025, .07, ghostLum)) * ghost.a * smoothstep(.08, .22, lum);
                    rgb = lerp(rgb, _OutlineColor.rgb, trace * .22 * _DrawnAmount);
                    float flecks = smoothstep(.68, .85, paperNoise(float2(art.x * .3, art.y * 1.6) / 3.5 + 7));
                    float paintOnly = smoothstep(.04, .12, lum) * (1 - smoothstep(.65, .95, lum));
                    rgb = lerp(rgb, float3(.82, .79, .74), flecks * paintOnly * _DryFiber * _DrawnAmount / (1 + footprint / 4));
                }
                float ink = saturate(edge - texel.a) * _OutlineColor.a;
                ink *= 1 - _SketchStrength * (.5 + broad);
                float faceAlpha = texel.a + ink * (1 - texel.a);
                float3 facePremul = rgb * texel.a + _OutlineColor.rgb * ink * (1 - texel.a);
                float2 looseShift = clamp(dx * (paperNoise(art / 83 + 13) - .5) * 4 + dy * (paperNoise(art / 79 + 47) - .5) * 4, -maxStep, maxStep);
                float2 looseUv = drawUv + looseShift;
                float looseCentre = samplePart(looseUv).a;
                float looseEdge = max(max(samplePart(looseUv + dx * .55).a, samplePart(looseUv - dx * .55).a), max(samplePart(looseUv + dy * .55).a, samplePart(looseUv - dy * .55).a));
                float dash = smoothstep(.42, .62, paperNoise(art / 19 + 37));
                float loose = saturate(looseEdge - looseCentre) * dash * _LooseOutline * _DrawnAmount;
                float alpha = faceAlpha + loose * (1 - faceAlpha);
                float3 premul = facePremul + _OutlineColor.rgb * loose * (1 - faceAlpha);
                return float4(alpha > .0001 ? premul / alpha : float3(0, 0, 0), alpha);
            }
            ENDCG
        }
    }
}
