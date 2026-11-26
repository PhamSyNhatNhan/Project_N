Shader "Project_N/VFX/GuraWater"
{
    Properties
    {
        _Trail ("Namu water edge", 2D) = "white" {}
        _Caustics ("Namu water ridges", 2D) = "white" {}
        [HideInInspector] _Solid ("Use solid shading", Float) = 0
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        [HideInInspector] _Waterline ("Clip below this world height", Float) = -10000
        [HideInInspector] _DepthWrite ("Write Depth", Float) = 0
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

        CBUFFER_START(UnityPerMaterial)
        float _Solid;
        float _Opacity;
        float _Waterline;
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
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.color = input.color;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.y = TransformObjectToWorld(input.positionOS.xyz).y;
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            if (_Solid > .5)
            {
                clip(input.y - _Waterline);
                half3 normal = normalize(input.normalWS);
                half light = .48 + .52 * abs(dot(normal, normalize(half3(.36, .84, -.40))));
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
