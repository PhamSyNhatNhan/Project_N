using Unity.VisualScripting;
using UnityEngine;

public class CalamitasPillar : Stat
{
    protected override void Awake()
    {
        base.Awake();
    }

    protected override void OnDead()
    {
        EventManager.Entity.OnEntityDead
            .Get("CalamitasPillar")
            .Invoke(this, null);
        
        gameObject.SetActive(false);
    }
    
}