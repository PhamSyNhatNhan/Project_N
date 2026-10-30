// NOTE: two passes, because the project has two renderers and a camera picks one (see PNVisualActor.shader): the URP 2D Renderer draws `Universal2D`, the Hall's 3D renderer `UniversalForward`.
// The strings of EVERY puppet are one mesh drawn with this one material: a camera-facing ribbon per string. Drawn BEFORE the puppets (queue Transparent-1) so the strings run behind them. Per vertex: colour = core (rgb) and alpha; uv0 = (across 0..1, along 0..1, width m, knot);
// uv1 = halo rgb + how much of the top fades; uv2 = knot rgb. The line is a bright core with a soft halo, a glow where it is tied, and it fades out towards the top of the view.
Shader "PN/VisualStrings"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float4 uv3:TEXCOORD3;half4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float4 uv3:TEXCOORD3;half4 color : COLOR; };
        half3 ToLinear(half3 c)
        {
#if !defined(UNITY_COLORSPACE_GAMMA)
            return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878);   // the colours are authored as sRGB hex, the framebuffer is linear
#else
            return c;
#endif
        }
        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformWorldToHClip(input.positionOS.xyz);          // the batch is built in world space
            output.uv0 = input.uv0; output.uv1 = input.uv1; output.uv2 = input.uv2;output.uv3=input.uv3;
            output.color = half4(ToLinear(input.color.rgb), input.color.a);
            output.uv1.rgb = ToLinear(input.uv1.rgb); output.uv2.rgb = ToLinear(input.uv2.rgb);
            return output;
        }
        half4 frag(Varyings input) : SV_Target
        {
            if(input.uv2.w>.5)
            {
                float2 q=input.uv0.xy*2-1;
                float radial=pow(saturate(1-dot(q,q)),3);
                if(input.uv2.w>1.5)radial=max(radial*.6,exp(-q.x*q.x*95-q.y*q.y*4)+exp(-q.y*q.y*95-q.x*q.x*4));
                return half4(input.color.rgb,saturate(radial)*input.color.a);
            }
            float d = abs(input.uv0.x * 2 - 1);                                          // 0 on the centre line, 1 on the edge
            float along = input.uv0.y;
            half core = smoothstep(.62, .30, d);
            half halo = smoothstep(1.0, .20, d) * .55;
            half3 rgb = lerp(input.uv1.rgb, input.color.rgb, core);
            half alpha = max(core, halo) * input.color.a;
            half fade = 1 - smoothstep(1 - input.uv1.w, 1, along);                       // fades out towards the top
            // A broad, short-lived hit ripple, not a travelling star/bead.
            float wave=exp(-pow((along-input.uv3.x)/.07,2))*input.uv3.y;
            rgb=lerp(rgb,lerp(input.uv1.rgb,half3(.8,.84,1),.65),wave*.65);
            alpha=saturate(alpha+wave*halo*.5*input.color.a)*fade;
            half knot = input.uv0.w * smoothstep(.07, 0, along) * (1 - d * .5);          // a small glow where the string is tied
            rgb = lerp(rgb, input.uv2.rgb, saturate(knot));
            alpha = max(alpha, saturate(knot) * .9);
            return half4(rgb, alpha);
        }
        ENDHLSL
        Pass
        {
            Name "VisualStrings2D"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "VisualStrings3D"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            ENDHLSL
        }
    }
}
