#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

public enum PNEnemyLookPriority { Task, Skill }
// One exact lease; releasing it cannot release another selection's look command.
public sealed class PNEnemyLookIntent : IDisposable
{
    private PNEnemyMove owner;
    internal readonly Guid Generation;
    internal readonly Func<bool> IsCurrent;
    internal readonly long Sequence;
    internal readonly PNEnemyLookPriority Priority;
    internal Vector3 Point;
    internal PNEnemyLookIntent(PNEnemyMove owner, Guid generation, long sequence, Vector3 point,
        PNEnemyLookPriority priority, Func<bool> isCurrent)
    { this.owner = owner; Generation = generation; Sequence = sequence; Point = point; Priority = priority; IsCurrent = isCurrent; }
    public bool UpdatePoint(Vector3 point)
    { PNNavigationSettings.Position(point); return owner != null && owner.UpdateLook(this, point); }
    public void Dispose() { var previous = owner; owner = null; if (previous != null) previous.ReleaseLook(this); }
}
#endif
