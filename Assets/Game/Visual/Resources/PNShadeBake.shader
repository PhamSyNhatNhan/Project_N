// Clone trial: the overlap shadow between the parts of a puppet, baked once at the rest pose into a copy of the receiving part (see PNVisualShade).
// For each pixel of the receiver's tile: where is it on the puppet (the affine of the receiver quad), which higher-layer parts (casters) cover the shadow-offset point
// (their inverse affine gives their image coordinate), and darken the receiver there by the same tint and strength as the dynamic shader (PN/VisualActor). Alpha is unchanged.
Shader "Hidden/PN/VisualShadeBake"
{
    Properties { _MainTex ("Baked atlas", 2D) = "white" {} }
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
            float4 _RecvOuter;            // receiver tile in the atlas: u0, v0, u1, v1 (including its baked border)
            float4 _RecvAx, _RecvAy;      // atlas uv -> puppet-local x, y :  x = dot(_RecvAx.xyz, (u, v, 1))
            float _CasterCount, _Strength;
            float4 _Offset;               // xy: shadow offset in local metres (the shadow sits at the caster + offset), zw: softness vector in local metres
            float4 _CasterBx[8], _CasterBy[8], _CasterBounds[8];   // local -> atlas uv of each caster: u = dot(_CasterBx.xyz, (x, y, 1)); bounds = its tile (u0, v0, u1, v1)
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = lerp(_RecvOuter.xy, _RecvOuter.zw, input.uv);
                float4 base = tex2Dlod(_MainTex, float4(uv, 0, 0));
                float2 local = float2(dot(_RecvAx.xyz, float3(uv, 1)), dot(_RecvAy.xyz, float3(uv, 1)));
                float2 query = local - _Offset.xy;
                float coverage = 0;
                for (int k = 0; k < (int)_CasterCount; k++)
                {
                    float4 bx = _CasterBx[k], by = _CasterBy[k], bounds = _CasterBounds[k];
                    float2 puv = float2(dot(bx.xyz, float3(query, 1)), dot(by.xyz, float3(query, 1)));
                    float2 soft = float2(dot(bx.xy, _Offset.zw), dot(by.xy, _Offset.zw));
                    float2 lo = bounds.xy + _MainTex_TexelSize.xy * .5, hi = bounds.zw - _MainTex_TexelSize.xy * .5;
                    float a = 0;
                    for (int t = 0; t < 3; t++)
                    {
                        float2 q = puv + (t == 1 ? soft : t == 2 ? -soft : 0);
                        float inside = step(bounds.x, q.x) * step(bounds.y, q.y) * step(q.x, bounds.z) * step(q.y, bounds.w);
                        a += tex2Dlod(_MainTex, float4(clamp(q, lo, hi), 0, 0)).a * inside * (t == 0 ? .5 : .25);
                    }
                    coverage = max(coverage, a);
                }
                float3 rgb = lerp(base.rgb, base.rgb * float3(.48, .43, .58), coverage * _Strength);
                return float4(rgb, base.a);
            }
            ENDCG
        }
    }
}
