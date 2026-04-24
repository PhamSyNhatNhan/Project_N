using System.Collections.Generic;
using UnityEngine;

public class BrimstoneLaser : ProjectileObject
{
    // ── Config ────────────────────────────────────────────────────
    [Header("Brimstone")]
    [SerializeField] private GameObject brimstoneLaserSubPrefab;

    // ── Runtime ───────────────────────────────────────────────────
    private GameObject         subInstance;
    private BrimstoneLaserSub  sub;
    private Hitbox hitbox;


    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        hitbox = GetComponent<Hitbox>();

        if (brimstoneLaserSubPrefab != null)
        {
            subInstance                    = Instantiate(brimstoneLaserSubPrefab);
            subInstance.transform.parent   = null;
            sub                            = subInstance.GetComponent<BrimstoneLaserSub>();
            subInstance.SetActive(false);
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (subInstance != null) subInstance.SetActive(false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (subInstance != null) subInstance.SetActive(false);
    }

    private void Update()
    {
        if (subInstance == null || !subInstance.activeSelf) return;
        subInstance.transform.position = transform.position;
        subInstance.transform.rotation = transform.rotation;
    }
    
    // ── Damage ────────────────────────────────────────────────────
    public override void SendDamage()
    {
        List<GameObject> Enemy = hitbox.detectObject(EnableDamage);

        for (int i = 0; i < Enemy.Count; i++)
        {
            var stat = Enemy[i].GetComponent<Stat>();
            stat.TakeDamage(type, damage[0], critRate, critDamage);
        }
    }

    // ── Animation Events ──────────────────────────────────────────
    /// <summary>Gọi từ Animation Event — bật sub nếu chưa active</summary>
    public void OnHold()
    {
        if (subInstance == null || subInstance.activeSelf) return;
        subInstance.transform.position = transform.position;
        subInstance.transform.rotation = transform.rotation;
        subInstance.SetActive(true);
        sub?.OnLaserHold();
    }

    /// <summary>Gọi từ bên ngoài — kết thúc laser</summary>
    public void OnEnd()
    {
        sub?.OnLaserEnd();
    }

    /// <summary>Gọi từ Animation Event của sub khi animation end kết thúc → disable sub</summary>
    public void OnEndEnd()
    {
        if (subInstance != null) subInstance.SetActive(false);
        gameObject.SetActive(false);
    }
}