Shader "Project_N/VFX/GlassShard"
{
    // One shader, two looks, picked by the material's _Mirror (0 = clear glass, 1 = silvered mirror). It is ONE pass (URP does not draw several passes of one material for
    // particles, which is why the earlier three-pass version never showed its reflection layer) that composes three layers with premultiplied blending:
    // colour = the layers blended over each other plus the additive glint, alpha = the combined coverage.
    //   1. Body: glass = a very pale blue tint, more opaque at grazing angles (Fresnel); mirror = a dark steel backing that hides most of the background.
    //   2. Reflection (blended over the body): the vector reflect(-view, normal) looks up an environment map (_EnvTex, painted in code: blue sky, bright horizon band, hard-edged
    //      windows, a dark ground). A facet turned toward the dark ground becomes darker than the background and one turned toward a window becomes bright: the contrast between
    //      facets is what reads as glass. Mirror look: much higher reflectivity, stronger facet contrast, and the entity's own sprite is reflected: every shard carries its position
    //      in the pane (uv1 = shard centre), so it shows the part of the sprite it covered (_SpriteTex, _Rect = its uv rectangle, _Map = pane-to-sprite scale, x negative = mirrored).
    //   3. Glint (added on top, HDR): a sharp specular point and a thin bright rim; values above 1 so a bloom volume, when present, makes them glow.
    // (Method: environment reflection and the Fresnel effect, "The Unity Shaders Bible", chapter II, sections 7.0.5-7.0.6.)
    Properties
    {
        _EnvTex ("Environment (equirectangular)", 2D) = "gray" {}
        _Mirror ("Mirror amount", Range(0, 1)) = 0
        _SpriteTex ("Reflected sprite", 2D) = "black" {}
        _Rect ("Sprite uv rect (x, y, w, h)", Vector) = (0, 0, 1, 1)
        _Map ("Pane to sprite uv scale", Vector) = (0.5, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Shard"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 centre:TEXCOORD1; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2; float2 pane:TEXCOORD3; };
            TEXTURE2D(_EnvTex); SAMPLER(sampler_EnvTex);
            TEXTURE2D(_SpriteTex); SAMPLER(sampler_SpriteTex);
            float _Mirror; float4 _Rect; float4 _Map;
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.uv = v.uv; o.color = v.color; o.pane = v.centre + v.positionOS.xy; return o;
            }
            half3 Env(float3 refl, float seed)
            {
                float2 uv = float2(atan2(refl.z, refl.x) * 0.15915494 + 0.5 + seed, asin(clamp(refl.y, -1.0, 1.0)) * 0.31830989 + 0.5);
                return SAMPLE_TEXTURE2D_LOD(_EnvTex, sampler_EnvTex, uv, 0).rgb;
            }
            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 normal = normalize(i.normalWS); normal = IS_FRONT_VFACE(face, true, false) ? normal : -normal;
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float3 refl = reflect(-view, normal);
                float fresnel = pow(1.0 - saturate(dot(normal, view)), 2.0);
                float rim = 1.0 - smoothstep(0.0, 0.12, i.uv.x);          // uv.x is 1 at the shard centre and 0 on its outline
                // Layer 1: body.
                float edge = 1.0 - smoothstep(0.0, 0.40, i.uv.x);
                half3 tint = lerp(half3(0.62, 0.80, 0.92), half3(0.45, 0.78, 0.82), edge);
                float glassAlpha = 0.10 + fresnel * 0.40 + edge * 0.14 + rim * 0.18;
                half3 bodyColor = lerp(tint, half3(0.10, 0.14, 0.19), _Mirror);
                float bodyAlpha = saturate(lerp(glassAlpha, 0.62 + fresnel * 0.2, _Mirror));
                // Layer 2: reflection. Mirror facets get a hard light/dark split by their reflection direction, and show the sprite part this shard covered.
                half3 env = Env(refl, i.color.r);
                float facet = saturate(dot(refl, normalize(float3(-0.3, 0.7, -0.5))) * 0.5 + 0.5);
                half3 mirrorEnv = env * lerp(0.45, 1.35, facet);
                float2 s = float2(0.5, 0.5) + i.pane * _Map.xy;
                float inside = step(0.0, s.x) * step(s.x, 1.0) * step(0.0, s.y) * step(s.y, 1.0);
                half4 sprite = SAMPLE_TEXTURE2D_LOD(_SpriteTex, sampler_SpriteTex, _Rect.xy + saturate(s) * _Rect.zw, 0);
                float cover = saturate(sprite.a * inside * _Mirror);
                half3 mirrored = lerp(mirrorEnv, sprite.rgb * lerp(0.7, 1.25, facet) + env * 0.12, cover * 0.92);
                half3 reflected = lerp(env, mirrored, _Mirror);
                float reflectivity = saturate(lerp(0.45 + fresnel * 0.45, 0.88 + fresnel * 0.12, _Mirror));
                // Layer 3: glint, turned per shard by a random seed (vertex colour red).
                float seed = i.color.r * 6.2831853; float cs = cos(seed), sn = sin(seed);
                float3 r = refl; r.xz = float2(cs * refl.x - sn * refl.z, sn * refl.x + cs * refl.z);
                float glint = pow(saturate(dot(r, normalize(float3(-0.45, 0.8, -0.4)))), 90.0);
                float glint2 = pow(saturate(dot(r, normalize(float3(0.6, 0.3, -0.7)))), 140.0);
                half3 add = (glint * 4.0 + glint2 * 3.0) * half3(0.85, 1.0, 1.0) + rim * half3(0.8, 1.0, 1.0) * 1.4;   // HDR: above 1 on purpose
                float a = i.color.a;
                half3 premultiplied = (reflected * reflectivity + bodyColor * bodyAlpha * (1.0 - reflectivity) + add) * a;
                float coverage = (reflectivity + bodyAlpha * (1.0 - reflectivity)) * a;
                return half4(premultiplied, coverage);
            }
            ENDHLSL
        }
    }
}
