Shader "Project_N/VFX/TimeScaleArea"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "PNVfxDepth.hlsl"
            TEXTURE2D(_AreaNoise); SAMPLER(sampler_AreaNoise);
            StructuredBuffer<float4> _AreaSpheres;
            int _AreaCount;
            float4x4 _AreaInvViewProjection;
            float4x4 _AreaViewProjection;
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float depth = PNVfxNearestDepth(uv);
                #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 surfacePosition = ComputeWorldSpacePosition(uv, depth, _AreaInvViewProjection);
                float nearDepth = UNITY_REVERSED_Z ? 1 : UNITY_NEAR_CLIP_VALUE;
                float3 origin = ComputeWorldSpacePosition(uv, nearDepth, _AreaInvViewProjection);
                float3 ray = surfacePosition - origin;
                float distanceToSurface = length(ray); ray /= max(distanceToSurface, 0.00001);
                float3 originDx=ddx(origin), originDy=ddy(origin), rayDx=ddx(ray), rayDy=ddy(ray);
                float mask = 0, darkening = 0;
                float4 selected = 0, filter = 1, vignetteColor = 1, extra=0, boundarySettings=0;
                float2 boundaryCenter=float2(.5,.5);
                float rim=0;
                float4 motionSettings=0;
                for (int i = 0; i < _AreaCount; i++)
                {
                    float4 sphere = _AreaSpheres[i * 7]; float4 settings = _AreaSpheres[i * 7 + 1]; float strength = settings.x;
                    if(sphere.w<=.0001) continue;
                    float3 offset = origin - sphere.xyz;
                    float b = dot(offset, ray), c = dot(offset, offset) - sphere.w * sphere.w;
                    float discriminant = b * b - c;
                    float closest=clamp(-b,0,distanceToSurface);
                    float segmentSdf=sphere.w-length(offset+ray*closest);
                    float pixelAA=max((length(originDx+rayDx*closest)+length(originDy+rayDy*closest))*.5,.0002);
                    float coverage=smoothstep(-pixelAA,pixelAA,segmentSdf);
                    if(coverage<=0) continue;
                    float h = sqrt(max(0,discriminant));
                    float contribution = coverage * strength;
                    float radialSquared = saturate(1 - h * h / (sphere.w * sphere.w));
                    float vignetteDistance = settings.w * 1.5;
                    float darkness = 1 - pow(saturate(1 - radialSquared * vignetteDistance * vignetteDistance), _AreaSpheres[i * 7 + 2].w);
                    float4 rimData=_AreaSpheres[i*7+5];
                    float boundary=smoothstep(1-rimData.y,1,sqrt(radialSquared));
                    if (contribution > mask + 0.0001)
                    {
                        mask = contribution; darkening = darkness; selected = settings;
                        filter = _AreaSpheres[i * 7 + 2]; vignetteColor = _AreaSpheres[i * 7 + 3]; extra=_AreaSpheres[i*7+4]; boundarySettings=rimData; motionSettings=_AreaSpheres[i*7+6]; rim=boundary;
                        float4 centerClip=mul(_AreaViewProjection,float4(sphere.xyz,1));
                        if(centerClip.w>.001)
                        {
                            boundaryCenter=centerClip.xy/centerClip.w*.5+.5;
                            #if UNITY_UV_STARTS_AT_TOP
                            boundaryCenter.y=1-boundaryCenter.y;
                            #endif
                        }
                    }
                    else if (abs(contribution - mask) <= 0.0001) { darkening=min(darkening,darkness); rim=min(rim,boundary); }
                }
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float aspect=_ScreenParams.x/_ScreenParams.y;
                float2 centered=(uv-boundaryCenter)*float2(aspect,1);
                float radial=length(centered);
                float2 normal=centered/max(radial,.0001);
                float2 tangent=float2(-normal.y,normal.x);
                float age=extra.z*motionSettings.y;
                float angle=atan2(centered.y,centered.x);
                // Curved travelling bands continuously bend the scene, not just the tint.
                float phase=angle*3-radial*19-age;
                float2 noiseUV=float2(angle*.25+radial*.8-age*.035,radial*2-age*.025);
                float noise=SAMPLE_TEXTURE2D(_AreaNoise,sampler_AreaNoise,noiseUV).r;
                float flow=sin(phase+noise*2);
                float pulse=1+sin(extra.z*.55)*boundarySettings.z;
                float interior=.25+.75*sqrt(saturate(rim));
                float2 warp=tangent*(flow*.75+sin(phase*.63+age)*.25)+normal*sin(radial*24-age*.8+noise)*.25;
                warp.x/=aspect;
                float2 edgeNormal=normal; edgeNormal.x/=aspect;
                float2 refraction=(warp*motionSettings.x*interior+edgeNormal*extra.y*rim*pulse)*mask;
                float2 regionUV=saturate(uv+refraction);
                float accents=smoothstep(.72,.98,sin(phase+noise*3))*(.35+.65*noise);
                // URP legacy chromatic: radial cubic displacement, restricted by volume coverage.
                float2 coords = 2 * uv - 1;
                float2 shift = -coords * dot(coords, coords) * selected.z * (mask*rim) / 3;
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,regionUV).rgb;
                color.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(regionUV + shift)).g;
                color.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(regionUV + 2 * shift)).b;
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(luminance.xxx, color, 1 + selected.y);
                color = lerp(color,color*filter.rgb,extra.x*(.78+.22*rim));
                color *= lerp(float3(1,1,1), vignetteColor.rgb, darkening);
                color += vignetteColor.rgb * boundarySettings.x * rim * accents * pulse;
                float3 result=lerp(original.rgb,color,mask);
                return half4(result, original.a);
            }
            ENDHLSL
        }
    }
}
