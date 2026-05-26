using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Dagger của Sakuya — kế thừa SakuyaKnife.
/// Khi trúng địch: gây damage như knife + rung camera.
/// </summary>
public class SakuyaDagger : SakuyaKnife
{
    [Header("Dagger")]
    [SerializeField] protected ShakeData shakeData = new ShakeData(0.1f, 1f, 0.2f, 0.1f);

    public override void SendDamage()
    {
        base.SendDamage();
        EventManager.Gm.OnCameraShake.Get().Invoke(this, shakeData);
    }
}