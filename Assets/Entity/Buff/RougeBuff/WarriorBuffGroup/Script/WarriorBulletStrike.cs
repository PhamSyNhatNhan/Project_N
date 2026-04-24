using UnityEngine;

public class WarriorBulletStrike : MinorBuff
{
    public override string BuffId      => "warrior_bullet_strike";
    public override string DisplayName => "Bullet Strike";
    public override string Description => "Mỗi 3 đòn tấn công, triệu hồi 2 viên đạn từ 2 bên.";


    // ── Kéo trên Inspector của prefab ────────────────────────────
    //[SerializeField] private GameObject bulletPrefab;
    //[SerializeField] private Transform  spawnLeft;
    //[SerializeField] private Transform  spawnRight;

    private string entityKey;
    private int    attackCount = 0;
    private const int AttacksPerTrigger = 3;

    protected override void OnApply()
    {
        //entityKey = $"{stat.NameCharacter}_{stat.GetInstanceID()}";
        //EventManager.Player.OnPlayerAttack.Get(entityKey).AddListener(HandleAttack);
    }

    protected override void OnRemove()
    {
        //EventManager.Player.OnPlayerAttack.Get(entityKey).RemoveListener(HandleAttack);
        //attackCount = 0;
    }

    private void HandleAttack(UnityEngine.Component sender, object data)
    {
        attackCount++;
        if (attackCount < AttacksPerTrigger) return;

        attackCount = 0;
        SpawnBullets();
    }

    private void SpawnBullets()
    {
        //if (bulletPrefab == null) return;

        //if (spawnLeft  != null) Instantiate(bulletPrefab, spawnLeft.position,  spawnLeft.rotation);
        //if (spawnRight != null) Instantiate(bulletPrefab, spawnRight.position, spawnRight.rotation);
    }
}
