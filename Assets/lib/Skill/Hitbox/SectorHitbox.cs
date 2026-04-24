using System.Collections.Generic;
using UnityEngine;

public class SectorHitbox : Hitbox
{
    [SerializeField] private List<_SectorHitbox> attackPoints;

    private void OnDrawGizmos()
    {
        for (int i = 0; i < attackPoints.Count; i++)
        {
            if (!attackPoints[i].IsShow) continue;

            Gizmos.color = i > 9 ? hitboxColors[9] : hitboxColors[i];
            DrawSector(
                attackPoints[i].AttackTransform.position,
                attackPoints[i].InnerRadius,
                attackPoints[i].OuterRadius,
                attackPoints[i].AttackAngles,
                // FIX: cộng rotation của AttackTransform vào direction
                attackPoints[i].AttackDirections + attackPoints[i].AttackTransform.eulerAngles.z
            );
        }
    }

    private void DrawSector(Vector3 position, float innerRadius, float outerRadius, float angle, float direction)
    {
        int   segments  = 50;
        float halfAngle = angle / 2;

        Vector3 prevInnerPoint = position + new Vector3(
            Mathf.Cos((direction - halfAngle) * Mathf.Deg2Rad) * innerRadius,
            Mathf.Sin((direction - halfAngle) * Mathf.Deg2Rad) * innerRadius, 0);
        Vector3 prevOuterPoint = position + new Vector3(
            Mathf.Cos((direction - halfAngle) * Mathf.Deg2Rad) * outerRadius,
            Mathf.Sin((direction - halfAngle) * Mathf.Deg2Rad) * outerRadius, 0);

        Gizmos.DrawLine(prevInnerPoint, prevOuterPoint);

        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = direction - halfAngle + (i / (float)segments) * angle;
            float rad          = currentAngle * Mathf.Deg2Rad;

            Vector3 innerPoint = position + new Vector3(Mathf.Cos(rad) * innerRadius, Mathf.Sin(rad) * innerRadius, 0);
            Vector3 outerPoint = position + new Vector3(Mathf.Cos(rad) * outerRadius, Mathf.Sin(rad) * outerRadius, 0);

            Gizmos.DrawLine(prevInnerPoint, innerPoint);
            Gizmos.DrawLine(prevOuterPoint, outerPoint);

            prevInnerPoint = innerPoint;
            prevOuterPoint = outerPoint;
        }

        Gizmos.DrawLine(
            position + new Vector3(Mathf.Cos((direction + halfAngle) * Mathf.Deg2Rad) * innerRadius, Mathf.Sin((direction + halfAngle) * Mathf.Deg2Rad) * innerRadius, 0),
            position + new Vector3(Mathf.Cos((direction + halfAngle) * Mathf.Deg2Rad) * outerRadius, Mathf.Sin((direction + halfAngle) * Mathf.Deg2Rad) * outerRadius, 0));
    }

    public override List<GameObject> detectObject(LayerMask enableDamage)
        => detectObject(enableDamage, 0);

    public override List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        var   ap          = attackPoints[hitBox];
        var   listObject  = new List<GameObject>();
        var   detectedObjects = Physics2D.OverlapCircleAll(ap.AttackTransform.position, ap.OuterRadius, enableDamage);

        Vector2 origin       = ap.AttackTransform.position;
        float   innerRadiusSq = ap.InnerRadius * ap.InnerRadius;
        float   outerRadiusSq = ap.OuterRadius * ap.OuterRadius;
        float   halfAngle     = ap.AttackAngles / 2;

        // FIX: cộng rotation của AttackTransform vào direction
        float   directionRad  = (ap.AttackDirections + ap.AttackTransform.eulerAngles.z) * Mathf.Deg2Rad;
        Vector2 mainDirection = new Vector2(Mathf.Cos(directionRad), Mathf.Sin(directionRad));

        foreach (Collider2D col in detectedObjects)
        {
            Vector2 toCollider  = (col.transform.position - (Vector3)origin).normalized;
            float   distanceSq  = (col.transform.position - (Vector3)origin).sqrMagnitude;
            float   angleToCol  = Vector2.Angle(mainDirection, toCollider);

            if (distanceSq >= innerRadiusSq && distanceSq <= outerRadiusSq && angleToCol <= halfAngle)
                listObject.Add(col.gameObject);
        }

        if (listObject.Count > 1)
            listObject.Sort((a, b) =>
                Vector2.Distance(transform.position, a.transform.position)
                    .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        return listObject;
    }
}

[System.Serializable]
public class _SectorHitbox
{
    [SerializeField] private bool      isShow           = true;
    [SerializeField] private Transform attackTransform;
    [SerializeField] private float     outerRadius;
    [SerializeField] private float     innerRadius;
    [SerializeField] private float     attackAngles;     // góc hình quạt
    [SerializeField] private float     attackDirections; // hướng hình quạt (local, cộng thêm transform rotation)

    public bool      IsShow            { get => isShow;            set => isShow            = value; }
    public Transform AttackTransform   { get => attackTransform;   set => attackTransform   = value; }
    public float     OuterRadius       { get => outerRadius;       set => outerRadius       = value; }
    public float     InnerRadius       { get => innerRadius;       set => innerRadius       = value; }
    public float     AttackAngles      { get => attackAngles;      set => attackAngles      = value; }
    public float     AttackDirections  { get => attackDirections;  set => attackDirections  = value; }
}