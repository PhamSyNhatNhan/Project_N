#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The forced layer of a body (Docs/BodyMotionPlan.md 3.3, B1/B4/B9-B11): pushes, pulls and lifts that something else does to the body (a knockback, a black hole), as opposed to the voluntary motion a skill
//     chooses (Dash, Hop). Every command is only a displacement, a duration and an optional height; the path is a fixed curve of the elapsed time (fast start, soft stop; a parabola for the height), so the machine
//     that writes the pose can start it late and still end at the same point (`age`). Many commands run IN PARALLEL: each frame the body takes the sum of their steps (two black holes both pull; opposite pulls cancel).
//     Walls stop it (the sweep is the shared `PNBodyMotion.Clip`); actor bodies do not (players and enemies never collide, B11).
//     Who writes the pose follows the machine's role for the body, like every motion: a Local body (host, offline, enemies) and the predicted Owner write it; the host's view of a client's player (RemoteOwnerHost)
//     only runs the clock and, when the owner never moved, puts the body at the end point through the existing Teleport correction (the safety net). Nothing here knows what hit the body or why.
// VI: Lop chuyen dong bi ep cua than: day/hut/hat tung do thu khac gay ra. Moi lenh chi la do dich, thoi luong va do cao tuy chon; duong di la ham co dinh cua thoi gian troi qua nen may ghi pose den tre van ve dung diem cuoi.
//     Nhieu lenh chay SONG SONG, moi khung than di tong cac buoc. Tuong chan, than nguoi/quai khong chan nhau. May ghi pose theo vai tro; ban sao host cua nguoi choi client chi chay dong ho va co luoi an toan Teleport.
[DisallowMultipleComponent]
public sealed class PNForcedMotion : MonoBehaviour
{
    public const int MaxCommands = PNForcedMotionLimits.MaxCommands;
    // How long after its end the host waits for the owner's pose before it puts the body at the end point itself (the owner's own travel and the report delay are inside it).
    public const float OwnerGraceSeconds = .75f;
    public const float MaxDuration = PNForcedMotionLimits.MaxDuration, MaxDistance = PNForcedMotionLimits.MaxDistance, MaxHeight = PNForcedMotionLimits.MaxHeight;

    private sealed class Command
    {
        public uint Id; public Vector3 Displacement; public float Duration, Height, Elapsed, AppliedS;
        public Vector3 Start; public Vector3 Expected; public float Grace; public bool Watch;
    }
    private readonly List<Command> active = new List<Command>();
    private readonly List<Command> waiting = new List<Command>();
    private PNMove move;
    private PNBodyMotion motion;
    private bool motionVertical;
    private PNControlLease lease;
    private uint nextId;
    private RaycastHit[] hits = new RaycastHit[32];
    private readonly HashSet<uint> seen = new HashSet<uint>();

    public int ActiveCount => active.Count;
    public bool IsActive => active.Count != 0;
    // Counts commands the layer refused or dropped (a cancelled layer, a body that could not be written): the owner view and the tests read it.
    public int Dropped { get; private set; }
    // Raised on the host for each command it decides (id, displacement, duration, height): the owner view sends it to the machine that writes a client's player.
    public event Action<uint, Vector3, float, float> Issued;
    // Raised when the safety net put a remote-owned body at the end point.
    public event Action<Vector3> Relocated;

    public static PNForcedMotion Of(PNMove move)
    {
        if (move == null) throw new ArgumentNullException(nameof(move));
        var layer = move.GetComponent<PNForcedMotion>();
        return layer != null ? layer : move.gameObject.AddComponent<PNForcedMotion>();
    }

    public static bool CanDecide(PNMove move) => move != null && move.NetworkUsable && (move.NetworkRole == PNMoveNetworkRole.Local && move.CanWriteMotion || move.NetworkRole == PNMoveNetworkRole.RemoteOwnerHost);

    // The host's decision: this body is pushed by `displacement` over `duration`, lifted by `height` (0 = none). Returns the command id, or 0 when the body cannot take it (not a body the authority moves).
    public uint Issue(Vector3 displacement, float duration, float height)
    {
        Validate(displacement, duration, height);
        move = move != null ? move : GetComponent<PNMove>();
        if (!CanDecide(move)) { Dropped++; return 0; }
        uint id = checked(++nextId);
        if (!Begin(id, displacement, duration, height, 0f)) return 0;
        Issued?.Invoke(id, displacement, duration, height);
        return id;
    }

    // The machine that writes a client's player received the host's command; `age` is how long ago the host decided it.
    public bool Receive(uint id, Vector3 displacement, float duration, float height, float age)
    {
        Validate(displacement, duration, height);
        if (age < 0f || float.IsNaN(age) || float.IsInfinity(age)) throw new ArgumentOutOfRangeException(nameof(age));
        move = move != null ? move : GetComponent<PNMove>();
        if (move == null || !move.NetworkUsable || move.NetworkRole != PNMoveNetworkRole.Owner) { Dropped++; return false; }
        if (id == 0 || seen.Contains(id)) return false;
        if (age >= duration) { seen.Add(id); return false; }
        return Begin(id, displacement, duration, height, age);
    }

    // Takes everything off the body (an unload, a world change, a teleport or death correction): nothing more is written.
    public void CancelAll()
    {
        Dropped += active.Count + waiting.Count; active.Clear(); waiting.Clear(); Release();
    }

    private static void Validate(Vector3 displacement, float duration, float height)
    {
        if (float.IsNaN(displacement.x) || float.IsNaN(displacement.y) || float.IsNaN(displacement.z) || displacement.magnitude > MaxDistance)
            throw new ArgumentOutOfRangeException(nameof(displacement), "Forced displacement must be finite and at most " + MaxDistance + " m.");
        if (!(duration > 0f) || duration > MaxDuration) throw new ArgumentOutOfRangeException(nameof(duration), "Forced duration must be in (0, " + MaxDuration + "] s.");
        if (float.IsNaN(height) || height < 0f || height > MaxHeight) throw new ArgumentOutOfRangeException(nameof(height), "Forced height must be in [0, " + MaxHeight + "] m.");
    }

    private bool Begin(uint id, Vector3 displacement, float duration, float height, float age)
    {
        if (active.Count >= MaxCommands) { Dropped++; return false; }
        move = move != null ? move : GetComponent<PNMove>();
        if (seen.Count > 128) seen.Clear(); seen.Add(id);
        var command = new Command { Id = id, Displacement = displacement, Duration = duration, Height = height, Elapsed = Mathf.Min(age, duration), Start = move.Body.position };
        bool shows = PNBodyMotion.IsRemoteOwnerBody(move);
        if (!shows)
        {
            if (!PrepareMotor(height > 0f || NeedsVertical())) { Dropped++; seen.Remove(id); return false; }
        }
        else
        {
            // The host does not write this pose: where the body should end up (walls stop it) is what the safety net compares the owner's pose against.
            command.Watch = true; command.Expected = command.Start + ClipPath(command.Start, displacement);
            AcquireControl();
        }
        active.Add(command);
        return true;
    }

    private bool NeedsVertical() { foreach (var c in active) if (c.Height > 0f) return true; return false; }

    // The body is taken from its voluntary motion (they are told through their cancel), then moved only by this layer.
    private bool PrepareMotor(bool vertical)
    {
        if (motion != null && motion.CanWrite && (motionVertical || !vertical)) return true;
        if (motion != null) { motion.Dispose(); motion = null; }
        move.CancelMotions();
        try { motion = new PNBodyMotion(move, PNControlIds.ForcedMotion, PNMotionActorCollision.IgnoreAllActors, vertical, OnMotionLost); motionVertical = vertical; }
        catch (InvalidOperationException) { return false; }
        AcquireControl();
        return true;
    }

    private void AcquireControl()
    {
        if (lease != null) return;
        var controls = move.GetComponent<PNControlManager>();
        if (controls == null || !controls.IsReady) return;
        try { lease = controls.State.Acquire("project_n:forced_motion", new[] { PNControlIds.VoluntaryMotion }); }
        catch (InvalidOperationException) { /* a machine without authority keeps no lease; the host's one decides */ }
    }

    // The move dropped the motion (a correction, a lost permission): the commands end, nothing is written until a new one comes.
    private void OnMotionLost()
    {
        motion = null; Dropped += active.Count; active.Clear(); waiting.Clear(); Release();
    }

    private void Release()
    {
        motion?.Dispose(); motion = null; motionVertical = false;
        lease?.Dispose(); lease = null;
    }

    private Vector3 ClipPath(Vector3 from, Vector3 displacement)
    {
        var capsule = move.GetComponent<CapsuleCollider>(); float distance = displacement.magnitude;
        if (capsule == null || capsule.direction != 1 || distance < .0001f) return displacement;
        var planar = new Vector3(displacement.x, 0f, displacement.z); float planarDistance = planar.magnitude;
        if (planarDistance < .0001f) return displacement;
        float allowed = PNBodyMotion.Clip(move.gameObject.scene, capsule, move, planar / planarDistance, planarDistance, PNMotionActorCollision.IgnoreAllActors, PNBodyMotion.TrajectorySkin, ref hits, out bool overflow);
        if (overflow) allowed = 0f;
        return planar / planarDistance * allowed + new Vector3(0f, displacement.y, 0f);
    }

    private static float Ease(float s) => 1f - (1f - s) * (1f - s);
    private static float Arc(float s) => 4f * s * (1f - s);

    // The delta every frame: the sum of what each command has to cover between the last frame and this one.
    private void Update()
    {
        if (active.Count == 0) return;
        var clock = GetComponent<EveTimeScale>();
        Tick(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);
    }

    // One step of every running command by `delta` seconds of this body clock (the tests drive it directly).
    public void Tick(float delta)
    {
        if (active.Count == 0) return;
        if (move == null) { CancelAll(); return; }
        if (!(delta > 0f)) return;
        Vector3 step = Vector3.zero;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var c = active[i];
            c.Elapsed += delta; float s = Mathf.Clamp01(c.Elapsed / c.Duration);
            step += c.Displacement * (Ease(s) - Ease(c.AppliedS)) + Vector3.up * (c.Height * (Arc(s) - Arc(c.AppliedS)));
            c.AppliedS = s;
            if (s >= 1f) { active.RemoveAt(i); if (c.Watch) { c.Grace = OwnerGraceSeconds; waiting.Add(c); } }
        }
        if (motion != null && step.sqrMagnitude > 1e-12f) motion.Step(step);
        if (active.Count == 0 && motion != null) Release();
    }

    // The host's safety net for a client's player: a command is over and the owner's pose never moved towards it, so the body goes to the end point by the existing relocation (a Teleport correction).
    private void LateUpdate() { TickSafety(Time.deltaTime); }

    public void TickSafety(float delta)
    {
        for (int i = waiting.Count - 1; i >= 0; i--)
        {
            var c = waiting[i]; c.Grace -= delta;
            if (c.Grace > 0f) continue;
            waiting.RemoveAt(i);
            if (move == null || !move.NetworkUsable || move.NetworkRole != PNMoveNetworkRole.RemoteOwnerHost) continue;
            var expected = new Vector2(c.Expected.x - c.Start.x, c.Expected.z - c.Start.z); var current = move.Body.position;
            float wanted = expected.magnitude, moved = new Vector2(current.x - c.Start.x, current.z - c.Start.z).magnitude;
            if (wanted < .5f || moved >= wanted * .25f) continue;
            try { move.RelocateToPoint(new Vector2(c.Expected.x, c.Expected.z)); Relocated?.Invoke(c.Expected); }
            catch (InvalidOperationException) { }
        }
        if (active.Count == 0 && waiting.Count == 0 && lease != null && motion == null) { lease.Dispose(); lease = null; }
    }

    private void OnDisable() { if (active.Count != 0 || waiting.Count != 0 || motion != null || lease != null) CancelAll(); }
    private void OnDestroy() { Release(); }
}
#endif
