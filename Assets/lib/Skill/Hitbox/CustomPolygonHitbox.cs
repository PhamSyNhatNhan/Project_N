using System.Collections.Generic;
using UnityEngine;

public class CustomPolygonHitbox : Hitbox
{
    [SerializeField] private List<_CustomPolygonHitbox> attackPoints;

    private PolygonCollider2D _cachedCollider;

    private void Awake()
    {
        GameObject go = new GameObject("_HitboxPolyCache");
        go.transform.SetParent(transform);
        go.SetActive(false);
        _cachedCollider = go.AddComponent<PolygonCollider2D>();
        _cachedCollider.isTrigger = true;
    }

    private void OnDrawGizmos()
    {
        if (attackPoints == null || attackPoints.Count == 0) return;

        for (int i = 0; i < attackPoints.Count; i++)
        {
            if (attackPoints[i]?.Points == null || attackPoints[i].Points.Count < 3) continue;

            Gizmos.color = i < hitboxColors.Count ? hitboxColors[i] : Color.white;

            for (int j = 0; j < attackPoints[i].Points.Count; j++)
            {
                if (attackPoints[i].Points[j] == null) continue;

                Vector3 curr = attackPoints[i].Points[j].position;
                Vector3 next = attackPoints[i].Points[(j + 1) % attackPoints[i].Points.Count].position;
                Gizmos.DrawLine(curr, next);
            }
        }
    }

    public override List<GameObject> detectObject(LayerMask enableDamage)
        => detectObject(enableDamage, 0);

    public override List<GameObject> detectObject(LayerMask enableDamage, int hitBox)
    {
        var result = new List<GameObject>();

        if (attackPoints == null
            || hitBox >= attackPoints.Count
            || attackPoints[hitBox]?.Points == null
            || attackPoints[hitBox].Points.Count < 3)
            return result;

        var pts  = attackPoints[hitBox].Points;
        var poly = new Vector2[pts.Count];

        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i] == null) return result;


            poly[i] = _cachedCollider.transform.InverseTransformPoint(pts[i].position);
        }

        _cachedCollider.SetPath(0, poly);

        var buffer = new List<Collider2D>();
        var filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask    = enableDamage,
            useTriggers  = true,
        };

        _cachedCollider.gameObject.SetActive(true);
        Physics2D.OverlapCollider(_cachedCollider, filter, buffer);
        _cachedCollider.gameObject.SetActive(false);

        foreach (var col in buffer)
        {
            if (col.gameObject == gameObject) continue;
            result.Add(col.gameObject);
        }

        if (result.Count > 1)
            result.Sort((a, b) =>
                Vector2.Distance(transform.position, a.transform.position)
                    .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        return result;
    }
}

[System.Serializable]
public class _CustomPolygonHitbox
{
    [SerializeField] private List<Transform> points;
    public List<Transform> Points { get => points; set => points = value; }
}