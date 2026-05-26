using UnityEngine;

/// <summary>
/// DaggerLight — kế thừa SakuyaKnife.
/// Không disappear khi va chạm, không cộng PWS, không shake.
/// Chỉ gây damage.
/// </summary>
public class SakuyaDaggerLight : SakuyaKnife
{
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if ((EnableDamage.value & (1 << other.gameObject.layer)) == 0) return;
        var enemyStat = other.GetComponent<Stat>();
        if (enemyStat == null) return;

        enemyStat.TakeDamage(type, damage[0], critRate, critDamage, iFrameDuration);
    }
}