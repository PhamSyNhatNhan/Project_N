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
                if (_WaterSkin > .5)
                {
                    clip(IS_FRONT_VFACE(face, 1.0, -1.0));
                    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half rim = pow(1.0 - saturate(abs(dot(normal, view))), 2.2);
                    if (_WaterSkin > 1.5)
                    {
                        half3 marking = SAMPLE_TEXTURE2D(_SharkAlbedo, sampler_SharkAlbedo, input.uv).rgb;
                        half luminance = dot(marking, half3(.299,.587,.114));
                        float3 local = (input.positionWS - _SharkFlowOrigin.xyz) * _SharkFlowOrigin.w;
                        float along = dot(local, _SharkFlowDirection.xyz);
                        float across = dot(local, _SharkFlowAcross.xyz);
                        float side = dot(local, cross(_SharkFlowDirection.xyz, _SharkFlowAcross.xyz));
                        float2 flowUV = float2(along*.82 + _SharkFlowAge*1.18, across*.78 + side*.44);
                        half ripple = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, flowUV * .79).g;
                        float2 warpedFlow = flowUV + float2((ripple - .45)*.16, (ripple - .45)*.24);
                        half4 current = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, warpedFlow);
                        // The texture packs its branching caustic lines into blue.
                        half branches = smoothstep(.22,.66,current.b);
                        half ripples = smoothstep(.25,.72,SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics,
                            warpedFlow*1.66 + float2(-_SharkFlowAge*.53,.26)).b);
                        half sheet = 1.0 - smoothstep(.08,.49,abs(along + _SharkFlowAge*4.10 - 1.84 + (ripple - .45)*.25));
                        half facingLight = saturate(dot(normal, normalize(half3(-.31,.82,-.47))));
                        half volume = saturate(luminance*.46 + facingLight*.42 + current.g*.14);
                        half3 depth = lerp(half3(.005,.095,.24), half3(.018,.37,.57), volume);
                        half3 waterColor = lerp(depth, half3(.28,.91,1.0), saturate(rim*.37 + sheet*.42 + current.g*.18));
                        half veinLight = saturate(branches*(.34 + facingLight*.19) + ripples*.15);
                        waterColor = lerp(waterColor, half3(.57,.96,1.0), veinLight);
                        half alpha = .91 - rim*.32 + sheet*.06 + veinLight*.08;
                        half teeth = step(.904,input.uv.x) * step(input.uv.x,.956) * step(.940,input.uv.y) * smoothstep(.28,.57,luminance);
                        waterColor = lerp(waterColor, half3(.51,.89,.98), teeth*.50);
                        half submerge = smoothstep(0, .14, input.y - _Waterline);
                        return half4(waterColor, _Opacity * saturate(alpha) * submerge);
                    }
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
