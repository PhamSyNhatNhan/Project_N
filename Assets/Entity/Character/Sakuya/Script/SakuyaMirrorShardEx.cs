using UnityEngine;

/// <summary>
/// MirrorShardEx — kế thừa SakuyaDagger.
/// Khi trúng địch: gây damage + shake + fire OnProjectileHit channel "_mirror"
/// để SakuyaSkill tạo timestop 1.5s.
/// </summary>
public class SakuyaMirrorShardEx : SakuyaDagger
{
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if ((EnableDamage.value & (1 << other.gameObject.layer)) == 0) return;
        var enemyStat = other.GetComponent<Stat>();
        if (enemyStat == null) return;

        enemyStat.TakeDamage(type, damage[0], critRate, critDamage, iFrameDuration);
        EventManager.Gm.OnCameraShake.Get().Invoke(this, shakeData);

        // Fire event để SakuyaSkill tạo timestop
        EventManager.Projectile.OnProjectileHit
            .Get(nameChannel)
            .Invoke(this, null);

        Debug.Log("MirrorEx: " + nameChannel);
        gameObject.SetActive(false);
    }
}