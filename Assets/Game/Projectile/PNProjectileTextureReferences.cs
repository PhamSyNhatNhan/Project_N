#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Registered texture references for projectile visuals. The textures stay where the VFX pack keeps them (referenced by GUID,
//     never copied); the asset lives at Resources/PNVfx/ProjectileTextures.
//     Head: the shape of the projectile body (drawn pointing along +x). Trail: a band that is bright in the middle across its width
//     and uniform along its length, used by a TrailRenderer.
// VI: Tham chiếu texture cho hiển thị đạn. Texture nằm nguyên trong pack (tham chiếu GUID, không sao chép).
public sealed class PNProjectileTextureReferences : ScriptableObject
{
    public Texture2D Head;
    public Texture2D Trail;
}
#endif
