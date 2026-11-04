#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// EN: A named place in a room map where enemies may appear (Docs/RogueliteFramePlan.md 5.4). A room's JSON names the group (`spawns[].group`); the map says where it is: a set of
//     points, or a disc on the ground around this object. Picking uses the run's Spawn stream, so a seed always puts the enemies in the same places. The position is on the map's
//     ground plane as authored; putting it on the navigation mesh is the room director's job. Keys follow the same rule as PNWorldObject and are unique inside a content slot.
// VI: Một chỗ có tên trong map phòng mà quái được xuất hiện: tập điểm hoặc hình tròn quanh object. Chọn chỗ dùng luồng Spawn của run nên cùng seed ra cùng chỗ.
[DisallowMultipleComponent]
public sealed class PNRoomSpawnGroup : MonoBehaviour
{
    public enum Shape { Points, Circle }
    public const float MaxRadius = 50f;
    [SerializeField] private string group = "";
    [SerializeField] private Shape shape = Shape.Points;
    [SerializeField] private Transform[] points = new Transform[0];
    [SerializeField] private float radius = 3f;
    public string Group => group;
    public Shape Layout => shape;
    public int PointCount => points?.Count(x => x != null) ?? 0;

    public void Configure(string key, Shape layout, IEnumerable<Transform> pointList, float discRadius)
    {
        group = PNSyncKey.Require(key, "A spawn group key"); shape = layout; points = (pointList ?? Enumerable.Empty<Transform>()).ToArray(); radius = discRadius; Validate();
    }

    public void Validate()
    {
        PNSyncKey.Require(group, "Spawn group key of " + name);
        if (shape == Shape.Points)
        {
            if (points == null || points.Length == 0 || points.Any(x => x == null)) throw new InvalidOperationException("Spawn group '" + group + "' needs at least one point and no empty slot.");
        }
        else if (shape == Shape.Circle)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f || radius > MaxRadius) throw new InvalidOperationException("Spawn group '" + group + "' needs a radius in (0, " + MaxRadius + "].");
        }
        else throw new InvalidOperationException("Spawn group '" + group + "' has an unknown shape.");
    }

    public static void ValidateSet(IEnumerable<PNRoomSpawnGroup> groups)
    {
        if (groups == null) throw new ArgumentNullException(nameof(groups));
        var seen = new HashSet<string>();
        foreach (var item in groups)
        {
            if (item == null) throw new InvalidOperationException("Missing spawn group component.");
            item.Validate();
            if (!seen.Add(item.group)) throw new InvalidOperationException("Duplicate spawn group '" + item.group + "' in one content slot.");
        }
    }

    // EN: One place, from the Spawn stream. Points: one of the points. Circle: uniform over the disc (the square root keeps the density even), at this object's height.
    public Vector3 Pick(PNRunRandom random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        Validate();
        if (shape == Shape.Points) return points[random.NextInt(PNRunStream.Spawn, points.Length)].position;
        double angle = random.NextDouble(PNRunStream.Spawn) * 2d * Math.PI, distance = radius * Math.Sqrt(random.NextDouble(PNRunStream.Spawn));
        var centre = transform.position;
        return new Vector3(centre.x + (float)(Math.Cos(angle) * distance), centre.y, centre.z + (float)(Math.Sin(angle) * distance));
    }
}
#endif
