using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Data truyền vào ShockEffect qua AddEffect(type, data).
/// </summary>
public class ShockData
{
    public float           BurstRadius;
    public LayerMask       DamageLayer;
    public List<DamageEntry> Damages;

    public ShockData(float burstRadius, LayerMask damageLayer, List<DamageEntry> damages = null)
    {
        BurstRadius = burstRadius;
        DamageLayer = damageLayer;
        Damages     = damages;
    }
}