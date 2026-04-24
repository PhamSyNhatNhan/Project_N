using UnityEngine;

public class CalamitasBomb : EasyBulletObject
{
    [Header("Fade In")]
    [SerializeField] private float startAlpha = 0.5f;

    private SpriteRenderer sr;

    protected override void Awake()
    {
        base.Awake();
        sr = GetComponent<SpriteRenderer>()
             ?? GetComponentInChildren<SpriteRenderer>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetAlpha(startAlpha);
    }
    protected override void OnDisable()
    {
        base.OnDisable();
        if (liveTimeSub <= 0)
        {
            explosion.transform.position = transform.position;
            explosion.transform.rotation = transform.rotation;
            explosion.GetComponent<ProjectileObject>().SetUp(type, damage, stat, critRate, critDamage);
            explosion.SetActive(true);
            gameObject.SetActive(false);
        }
        
    }

    protected override void Update()
    {
        base.Update();
        if (sr == null || liveTime <= 0f) return;
        
        float t = 1f - (liveTimeSub / liveTime);
        SetAlpha(Mathf.Lerp(startAlpha, 1f, t));
    }

    private void SetAlpha(float a)
    {
        if (sr == null) return;
        Color c = sr.color;
        c.a      = a;
        sr.color = c;
    }
    
    public override void SendDamage(){}
}