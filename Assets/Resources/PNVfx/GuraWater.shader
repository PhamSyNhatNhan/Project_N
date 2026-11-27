Shader "Project_N/VFX/GuraWater"
{
    Properties
    {
        _Trail ("Namu water edge", 2D) = "white" {}
        _Caustics ("Namu water ridges", 2D) = "white" {}
        [HideInInspector] _SharkAlbedo ("Shark markings", 2D) = "white" {}
        [HideInInspector] _SharkFlowAge ("Shark flow age", Float) = 0
        [HideInInspector] _SharkFlowDirection ("Shark flow direction", Vector) = (0, 0, 1, 0)
        [HideInInspector] _SharkFlowAcross ("Shark right direction", Vector) = (1, 0, 0, 0)
        [HideInInspector] _SharkFlowOrigin ("Shark flow origin and inverse radius", Vector) = (0, 0, 0, 1)
        [HideInInspector] _Solid ("Use solid shading", Float) = 0
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        [HideInInspector] _Waterline ("Clip below this world height", Float) = -10000
        [HideInInspector] _DepthWrite ("Write Depth", Float) = 0
        [HideInInspector] _WaterSkin ("Water body", Float) = 0
        [HideInInspector] _DepthOnly ("Depth only copy", Float) = 0
        [HideInInspector] _Fin ("A lone fin", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Cull Off
        ZWrite [_DepthWrite]
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Trail);
        SAMPLER(sampler_Trail);
        TEXTURE2D(_Caustics);
        SAMPLER(sampler_Caustics);
        TEXTURE2D(_SharkAlbedo);
        SAMPLER(sampler_SharkAlbedo);

        CBUFFER_START(UnityPerMaterial)
        float _Solid;
        float _Opacity;
        float _Waterline;
        float _WaterSkin;
        float _DepthOnly;
        float _Fin;
        float _SharkFlowAge;
        float4 _SharkFlowDirection;
        float4 _SharkFlowAcross;
        float4 _SharkFlowOrigin;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            half3 normalWS : TEXCOORD1;
            float y : TEXCOORD2;
            float3 positionWS : TEXCOORD3;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.color = input.color;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            // A body of water has no still outline: swells run down the water shark.
            if (_WaterSkin > 1.5)
            {
                float3 p = output.positionWS;
                float swell = sin(p.y * 9.0 + _SharkFlowAge * 17.0 + p.x * 5.0) * .62 + sin(p.y * 19.0 + _SharkFlowAge * 23.0 - p.z * 13.0) * .38;
                output.positionWS += output.normalWS * swell * .042;
                output.positionCS = TransformWorldToHClip(output.positionWS);
            }
            output.y = output.positionWS.y;
            return output;
        }

        half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            if (_Solid > .5)
            {
                clip(input.y - _Waterline);
                half3 normal = normalize(input.normalWS);
                half light = .48 + .52 * abs(dot(normal, normalize(half3(.36, .84, -.40))));
                // The water shark: one see-through skin, the nearest one only (a depth copy is drawn first), so nothing inside it shows.
                if (_WaterSkin > 1.5)
                {
                    if (_DepthOnly > .5) return half4(0, 0, 0, 0);
                    float rise = input.y - _Waterline;
                    half foam = 1.0 - smoothstep(.02, .20, rise * (1.0 + _Fin * 4.0));
                    half3 mouth = half3(.004, .040, .15);
                    // Back faces are only seen through the open mouth or the cut at the water.
                    if (IS_FRONT_VFACE(face, 1.0, -1.0) < 0 && _Fin < .5)
                        return half4(lerp(mouth, half3(.62, .96, 1.0), foam), .93);
                    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half fresnel = 1.0 - saturate(abs(dot(normal, view)));
                    float3 local = (input.positionWS - _SharkFlowOrigin.xyz) * _SharkFlowOrigin.w;
                    float along = dot(local, _SharkFlowDirection.xyz);
                    float across = dot(local, _SharkFlowAcross.xyz);
                    float side = dot(local, cross(_SharkFlowDirection.xyz, _SharkFlowAcross.xyz));
                    // Water pouring off the body: streams stretched along it, running from the nose down.
                    float2 flow = float2(along * .52 + _SharkFlowAge * 1.55, across * 1.05 + side * .62);
                    half warp = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, flow * .55).g - .45;
                    half lines = smoothstep(.30, .68, SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, flow * .74 + warp * .30).b);
                    half fine = smoothstep(.42, .80, SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics,
                        flow * 1.9 + float2(_SharkFlowAge * .9, .31) - warp * .2).b);
                    half3 marking = SAMPLE_TEXTURE2D(_SharkAlbedo, sampler_SharkAlbedo, input.uv).rgb;
                    half luminance = dot(marking, half3(.299,.587,.114));
                    half belly = smoothstep(.04, .42, luminance);
                    half lit = saturate(dot(normal, normalize(half3(-.31,.82,-.47))));
                    half3 body = lerp(half3(.004, .115, .40), half3(.015, .39, .72), saturate(lit * .75 + along * .18 + warp));
                    body = lerp(body, half3(.20, .72, .92), belly * .62);
                    body = lerp(body, half3(.50, .93, 1.0), saturate(lines * .85 + fine * .34));
                    half glint = pow(saturate(dot(normal, normalize(normalize(half3(-.31,.82,-.47)) + view))), 38.0);
                    half edge = smoothstep(.48, .78, fresnel + warp * .45);
                    body = lerp(body, half3(.62, .97, 1.0), smoothstep(.18, .62, fresnel) * .55);
                    body = lerp(body, half3(.95, 1.0, 1.0), saturate(edge + glint));
                    body = lerp(body, lerp(half3(.06, .55, .92), half3(.62, .96, 1.0), saturate(lines + fine * .5 + rise * 1.6)), _Fin * .62);
                    half alpha = saturate(.80 + belly * .06 + lines * .26 + edge + glint + smoothstep(.2, .7, fresnel) * .2);
                    // Mouth, gums and teeth have their own places in the atlas; the eye is a light in the water.
                    half cavity = 1.0 - step(1.0, length((input.uv - float2(.654, .264)) / float2(.066, .100)));
                    half mouthPart = step(.900, input.uv.x) * step(input.uv.x, .960) * step(.936, input.uv.y);
                    half tooth = mouthPart * step(.36, luminance) * step(marking.r - marking.g, .12);
                    half inside = saturate(cavity + mouthPart) * (1.0 - _Fin);
                    body = lerp(body, mouth, inside);
                    body = lerp(body, half3(.94, 1.0, 1.0), tooth);
                    alpha = lerp(alpha, .94, inside);
                    alpha = lerp(alpha, 1.0, tooth);
                    float eye = length((input.uv - float2(.896, .654)) / float2(.0060, .0092));
                    half socket = 1.0 - smoothstep(1.35, 1.9, eye);
                    half shine = 1.0 - smoothstep(.75, 1.0, eye);
                    body = lerp(body, mouth, socket);
                    body = lerp(body, half3(.86, 1.0, 1.0), shine);
                    alpha = lerp(alpha, 1.0, socket);
                    body = lerp(body, half3(.88, 1.0, 1.0), saturate(foam + step(.74, warp + .45) * (1.0 - smoothstep(.1, .55, rise * (1.0 + _Fin * 4.0)))));
                    alpha = saturate(alpha + foam);
                    return half4(body, _Opacity * alpha);
                }
                if (_WaterSkin > .5)
                {
                    clip(IS_FRONT_VFACE(face, 1.0, -1.0));
                    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half rim = pow(1.0 - saturate(abs(dot(normal, view))), 2.2);
                    half pattern = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, input.positionWS.xz * .76 + input.y * float2(.29,-.19)).g;
                    half detail = smoothstep(.30,.78,pattern) * .27;
                    half clarity = saturate(input.color.a);
                    half3 skin = lerp(input.color.rgb * (.76 + light * .32), half3(.40,.89,.98), (rim * .48 + detail) * (1.1 - clarity * .52));
                    return half4(skin, _Opacity * saturate(clarity * (.79 + rim * .25)));
                }
                return half4(input.color.rgb * light, _Opacity);
            }
            if (input.uv.y > 1.5) return input.color;
            half4 mask = SAMPLE_TEXTURE2D(_Trail,sampler_Trail, float2(input.uv.x, lerp(.22, .82, input.uv.y)));
            half3 ridges = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, input.uv * float2(1.87, .94)).rgb;
            half foam = saturate(.12 + ridges.b * .34 + smoothstep(.60, .94, input.uv.y) * .46);
            half coverage = lerp(.30, 1.0, mask.a * mask.r) * lerp(.71, 1, ridges.g);
            return half4(lerp(input.color.rgb * .85, half3(.73, .97, 1), foam * .78), input.color.a * coverage);
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
