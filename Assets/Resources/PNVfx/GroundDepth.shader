Shader "Project_N/Environment/SpriteGroundDepth"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4x4 _PNVfxFloorViewProjection;
        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings GroundVertex(Attributes input)
        {
            Varyings o = (Varyings)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = mul(_PNVfxFloorViewProjection,float4(TransformObjectToWorld(input.positionOS),1)); o.uv = input.uv;
            return o;
        }
        half4 GroundColor(Varyings input) : SV_Target
        {
            return 0;
        }
        float4 GroundDepth(Varyings input) : SV_Target
        { return float4(input.positionCS.z,0,0,0); }
        ENDHLSL
        Pass
        {
            Name "VFXFloorDepth" Tags { "LightMode"="PNVfxFloorDepth" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex GroundVertex
            #pragma fragment GroundDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
