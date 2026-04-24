using UnityEngine;

public class CalamitasEasyBulletWithoutExplosionResetHitObject : EasyBulletWithoutExplosionResetHitObject
{
    private SpriteRenderer sr;

    protected override void Awake()
    {
        base.Awake();
        sr     = GetComponent<SpriteRenderer>()
                 ?? GetComponentInChildren<SpriteRenderer>();
    }
    
    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        UpdateFlip();
    }

    private void UpdateFlip()
    {
        if (sr == null) return;
        sr.flipY = MoveDir.x < 0f;
    }
}
