using System.Collections.Generic;
using UnityEngine;

public class BoxHitbox : Hitbox
{
    [SerializeField] private List<_BoxHitbox> attackPoints;

    private void OnDrawGizmos()
    {
        for (int i = 0; i < attackPoints.Count; i++)
        {
            if (!attackPoints[i].IsShow) continue;

            Gizmos.color = i > 9 ? hitboxColors[9] : hitboxColors[i];

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                attackPoints[i].AttackTransform.position,
                attackPoints[i].AttackTransform.rotation,
                Vector3.one
            );
            Gizmos.DrawWireCube(Vector3.zero, attackPoints[i].AttackSize);
            Gizmos.matrix = oldMatrix;
        }
    }

    public override List<GameObject> detectObject(LayerMask enableDamage)
    {
        return detectObject(enableDamage, 0);
    }

    public override List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        float angle = attackPoints[hitBox].AttackTransform.eulerAngles.z;

        Collider2D[] DetectObject = Physics2D.OverlapBoxAll(
            attackPoints[hitBox].AttackTransform.position,
            attackPoints[hitBox].AttackSize,
            angle,
            enableDamage
        );

        List<GameObject> listObject = new List<GameObject>();
        foreach (Collider2D collider in DetectObject)
            listObject.Add(collider.gameObject);

        if (listObject.Count != 0)
            listObject.Sort((a, b) =>
                Vector2.Distance(transform.position, a.transform.position)
                .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        return listObject;
    }
}

[System.Serializable]
public class _BoxHitbox
{
    [SerializeField] private bool      isShow          = true;
    [SerializeField] private Transform attackTransform;
    [SerializeField] private Vector2   attackSize;

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

    public Vector2 AttackSize
    {
        get => attackSize;
        set => attackSize = value;
    }
}