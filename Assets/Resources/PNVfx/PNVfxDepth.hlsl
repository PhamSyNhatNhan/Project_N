TEXTURE2D_X_FLOAT(_PNVfxFloorDepth);
SAMPLER(sampler_PNVfxFloorDepth);
float _PNVfxFloorDepthAvailable;
float PNVfxNearestDepth(float2 uv)
{
    float depth=SampleSceneDepth(uv);
    if (_PNVfxFloorDepthAvailable > .5)
    {
        float floorDepth=SAMPLE_TEXTURE2D_X(_PNVfxFloorDepth,sampler_PNVfxFloorDepth,uv).r;
        #if UNITY_REVERSED_Z
        depth=max(depth,floorDepth);
        #else
        depth=min(depth,floorDepth);
        #endif
    }
    return depth;
}
