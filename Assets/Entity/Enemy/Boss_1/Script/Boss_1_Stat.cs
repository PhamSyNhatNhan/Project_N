using UnityEngine;

public class Boss_1_Stat : EnemyStat
{
    [Header("Boss FX")]
    [SerializeField] private Vector2 deadFxOffset = Vector2.zero;

    protected override void PlaySpawnFx()
    {
        if (fxSpawnInstance == null) return;

        SetVisible(false);
        isSpawning = true;

        fxSpawnInstance.transform.position   = transform.position;
        fxSpawnInstance.transform.localScale = Vector3.one;
        fxSpawnInstance.SetActive(true);

        gameObject.SetActive(false);
    }

    protected override void PlayDeadFx()
    {
        if (fxDeadInstance == null) return;

        fxDeadInstance.transform.position   = (Vector2)transform.position + deadFxOffset;
        fxDeadInstance.transform.rotation   = Quaternion.identity;
        fxDeadInstance.transform.localScale = Vector3.one;
        fxDeadInstance.SetActive(true);
    }
}