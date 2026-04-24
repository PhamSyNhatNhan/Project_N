using System.Collections.Generic;
using UnityEngine;

public class EllipseHitbox : Hitbox
{
    [SerializeField] private List<_EllipseHitbox> attackPoints;

    private void OnDrawGizmos()
    {
        for (int i = 0; i < attackPoints.Count; i++)
        {
            if (!attackPoints[i].IsShow) continue;

            Gizmos.color = i > 9 ? hitboxColors[9] : hitboxColors[i];
            DrawEllipse(
                attackPoints[i].AttackTransform.position,
                attackPoints[i].AttackSize,
                attackPoints[i].AttackTransform.eulerAngles.z
            );
        }
    }

    // FIX: thêm rotation → ellipse xoay theo AttackTransform
    private void DrawEllipse(Vector3 position, Vector2 size, float angleDeg)
    {
        int      segments = 100;
        Vector3[] points  = new Vector3[segments + 1];
        float     rad     = angleDeg * Mathf.Deg2Rad;
        float     cos     = Mathf.Cos(rad);
        float     sin     = Mathf.Sin(rad);

        for (int i = 0; i <= segments; i++)
        {
            float a  = i * 2.0f * Mathf.PI / segments;
            float lx = Mathf.Cos(a) * size.x / 2;
            float ly = Mathf.Sin(a) * size.y / 2;

            // Xoay điểm theo rotation của AttackTransform
            float wx = lx * cos - ly * sin;
            float wy = lx * sin + ly * cos;

            points[i] = position + new Vector3(wx, wy, 0);
        }

        for (int i = 0; i < segments; i++)
            Gizmos.DrawLine(points[i], points[i + 1]);
    }

    public override List<GameObject> detectObject(LayerMask enableDamage)
        => detectObject(enableDamage, 0);

    public override List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        var ap          = attackPoints[hitBox];
        var listObject  = new List<GameObject>();
        float maxRadius = Mathf.Max(ap.AttackSize.x, ap.AttackSize.y) / 2;

        var detectedObjects = Physics2D.OverlapCircleAll(
            ap.AttackTransform.position, maxRadius, enableDamage);

        foreach (Collider2D col in detectedObjects)
        {
            if (IsWithinEllipse(
                    col.transform.position,
                    ap.AttackTransform.position,
                    ap.AttackSize,
                    ap.AttackTransform.eulerAngles.z))
            {
                listObject.Add(col.gameObject);
            }
        }

        if (listObject.Count > 1)
            listObject.Sort((a, b) =>
                Vector2.Distance(transform.position, a.transform.position)
                    .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        return listObject;
    }

    // FIX: thêm rotation → transform point về local space của ellipse trước khi check
    private bool IsWithinEllipse(Vector3 point, Vector3 center, Vector2 size, float angleDeg)
    {
        float dx  = point.x - center.x;
        float dy  = point.y - center.y;

        // Xoay ngược điểm về local space của ellipse
        float rad = -angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        float lx  = dx * cos - dy * sin;
        float ly  = dx * sin + dy * cos;

        return (lx * lx) / (size.x * size.x / 4)
             + (ly * ly) / (size.y * size.y / 4) <= 1;
    }
}

[System.Serializable]
public class _EllipseHitbox
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