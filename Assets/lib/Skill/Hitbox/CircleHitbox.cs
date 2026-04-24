using System.Collections.Generic;
using UnityEngine;

public class CircleHitbox : Hitbox
{
    [SerializeField] private List<_CircleHitbox> attackPoints;
    
    private void OnDrawGizmos()
    {
        for (int i = 0; i < attackPoints.Count; i++)
        {
            if(!attackPoints[i].IsShow) continue;
            
            Gizmos.color = i > 9 ? hitboxColors[9] : hitboxColors[i];
            Gizmos.DrawWireSphere(attackPoints[i].AttackTransform.position, attackPoints[i].AttackRadius);
        }
    }
    
    public override List<GameObject> detectObject(LayerMask enableDamage)
    {
        return detectObject(enableDamage, 0);
    }

    public override List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        Collider2D[] DetectObject = Physics2D.OverlapCircleAll(attackPoints[hitBox].AttackTransform.position, attackPoints[
        hitBox].AttackRadius, enableDamage);

        List<GameObject> listObject = new List<GameObject>();

        foreach (Collider2D collider in DetectObject)
        {
            listObject.Add(collider.gameObject);
        }

        if (listObject.Count != 0)
        {
            listObject.Sort((a, b) => Vector2.Distance(transform.position, a.transform.position).CompareTo(Vector2.Distance(transform.position, b.transform.position)));
            return listObject;
        }
        
        return listObject;
    }
}

[System.Serializable]
public class _CircleHitbox
{
    [SerializeField] private bool isShow = true;
    [SerializeField] private Transform attackTransform;
    [SerializeField] private float attackRadius;

    public bool IsShow
    {
        get => isShow;
        set => isShow = value;
    }

    public Transform AttackTransform
    {
        get => attackTransform;
        set => attackTransform = value;
    }

    public float AttackRadius
    {
        get => attackRadius;
        set => attackRadius = value;
    }
}
