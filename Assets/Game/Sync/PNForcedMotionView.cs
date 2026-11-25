#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: How a forced motion the host decided reaches the machine that writes a client's player (Docs/BodyMotionPlan.md 3.3, M4). An owner-only Entity state `project_n:forced_motion` lists the commands still worth
//     sending (id, displacement, duration, height, the host time it was decided at). The owner's machine starts each new one at its age, so the end point matches the host even after the trip; the commands are
//     the same as on the host (PNForcedMotion), the pose reaches the host and the others through the ordinary Move channel. A command older than its duration is never started (late join, resync). Nothing here
//     decides anything: the host's PNForcedMotion issues, this component only carries.
// VI: Duong den may ghi pose cua nguoi choi client: state chu-rieng liet ke cac lenh con can gui (id, do dich, thoi luong, do cao, thoi diem host quyet); may chu nhan bat dau moi lenh theo tuoi cua no.
[DisallowMultipleComponent]
public sealed class PNForcedMotionView : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:forced_motion");
    public const int StateBytes = 2048;
    private const double KeepSeconds = 2.5;

    private struct Item { public uint Id; public Vector3 Displacement; public float Duration, Height; public double Stamp; }
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNForcedMotion layer;
    private readonly List<Item> items = new List<Item>();
    private readonly List<IDisposable> leases = new List<IDisposable>();
    private readonly List<Item> pending = new List<Item>();
    private Guid pendingEpoch;
    private ulong appliedRevision;
    public bool IsBound { get; private set; }
    public EveId StateId => State;
    public int AppliedCount { get; private set; }
    public int SentCount { get; private set; }
    // Diagnostics for the Player runs: states received, commands refused by the age window, commands the layer refused, the last age read.
    public int ApplyCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int AgeRejected { get; private set; }
    public int ReceiveRefused { get; private set; }
    public double LastAge { get; private set; }
    public string Diagnostics => "bound=" + IsBound + " captures=" + CaptureCalls + " latestRev=" + LatestRevision() + " sent=" + SentCount + " applyCalls=" + ApplyCalls + " applied=" + AppliedCount + " ageRejected=" + AgeRejected + " refused=" + ReceiveRefused + " lastAge=" + LastAge.ToString("F3") + " pending=" + pending.Count;
    public IReadOnlyList<uint> Pending { get { var ids = new List<uint>(); foreach (var item in pending) ids.Add(item.Id); return ids; } }

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; appliedRevision = 0; AppliedCount = SentCount = 0;
        var move = GetComponent<PNMove>(); if (move == null) throw new InvalidOperationException("Forced motion view requires a Move.");
        layer = PNForcedMotion.Of(move);
        if (service.IsAuthority)
        {
            leases.Add(service.RegisterSource(value, this));
            layer.Issued += OnIssued;
        }
        leases.Add(service.RegisterSink(value, this));
    }
    public void OnUnbound()
    {
        if (layer != null) layer.Issued -= OnIssued;
        foreach (var lease in leases) lease.Dispose(); leases.Clear();
        items.Clear(); pending.Clear(); IsBound = false; sync = null; layer = null;
    }

    // Host: a command was decided for this body. Only a body whose pose another machine writes needs it sent.
    private void OnIssued(uint id, Vector3 displacement, float duration, float height)
    {
        if (!IsBound || sync == null || !sync.IsAuthority) return;
        var move = GetComponent<PNMove>(); if (move == null || move.NetworkRole != PNMoveNetworkRole.RemoteOwnerHost) return;
        var sample = PNTimeService.For(sync.Session).Sample();
        items.Add(new Item { Id = id, Displacement = displacement, Duration = duration, Height = height, Stamp = sample.Local });
        if (items.Count > PNForcedMotion.MaxCommands) items.RemoveAt(0);
        sync.MarkDirty(target, State); SentCount++;
    }

    private ulong LatestRevision() { if (sync == null) return 0; return sync.TryGetLatest(target, State, out var revision, out _) ? revision : 0; }

    public JToken Capture()
    {
        CaptureCalls++;
        var sample = PNTimeService.For(sync.Session).Sample();
        items.RemoveAll(item => sample.Local - item.Stamp > item.Duration + KeepSeconds);
        var list = new JArray();
        foreach (var item in items)
            list.Add(new JArray(item.Id, item.Displacement.x, item.Displacement.y, item.Displacement.z, item.Duration, item.Height, item.Stamp));
        return new JObject { ["v"] = 1, ["e"] = sample.Epoch.ToString("N"), ["c"] = list };
    }

    // Reads and checks a captured value (also used by the tests): returns the epoch and the commands.
    public static List<(uint Id, Vector3 Displacement, float Duration, float Height, double Stamp)> Parse(JToken value, out Guid epoch)
    {
        if (!(value is JObject o) || (int?)o["v"] != 1 || !(o["c"] is JArray c) || c.Count > PNForcedMotion.MaxCommands) throw new FormatException("Forced motion state.");
        if (o["e"] == null || o["e"].Type != JTokenType.String || !Guid.TryParseExact((string)o["e"], "N", out epoch) || epoch == Guid.Empty) throw new FormatException("Forced motion epoch.");
        var result = new List<(uint, Vector3, float, float, double)>();
        foreach (var token in c)
        {
            if (!(token is JArray a) || a.Count != 7) throw new FormatException("Forced motion command.");
            long id = (long)a[0]; double x = (double)a[1], y = (double)a[2], z = (double)a[3], duration = (double)a[4], height = (double)a[5], stamp = (double)a[6];
            if (id < 1 || id > uint.MaxValue || double.IsNaN(x + y + z + stamp) || double.IsInfinity(x + y + z + stamp) || stamp < 0d || !(duration > 0d) || duration > PNForcedMotion.MaxDuration ||
                height < 0d || height > PNForcedMotion.MaxHeight || new Vector3((float)x, (float)y, (float)z).magnitude > PNForcedMotion.MaxDistance)
                throw new FormatException("Forced motion command range.");
            result.Add(((uint)id, new Vector3((float)x, (float)y, (float)z), (float)duration, (float)height, stamp));
        }
        return result;
    }

    // Owner: new commands wait here until the machine's host clock is ready, then start at their age.
    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority || revision <= appliedRevision) return;
        ApplyCalls++; var commands = Parse(value, out var epoch); appliedRevision = revision;
        if (GetComponent<EveEntityLoader>().LoadContext.Identity.OwnerId != sync.Session.LocalOwnerId) return;
        pendingEpoch = epoch;
        foreach (var command in commands)
        {
            bool known = false; foreach (var p in pending) if (p.Id == command.Id) known = true;
            if (!known) pending.Add(new Item { Id = command.Id, Displacement = command.Displacement, Duration = command.Duration, Height = command.Height, Stamp = command.Stamp });
        }
        TryStart();
    }

    private void Update() { if (pending.Count != 0) TryStart(); }

    private void TryStart()
    {
        if (!IsBound || sync == null || layer == null || !sync.Session.IsActive) return;
        var sample = PNTimeService.For(sync.Session).Sample();
        if (!sample.Ready) return;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var item = pending[i]; pending.RemoveAt(i);
            // A command the host time says is too old (or from another epoch) is not started: the host's safety net covers the end point.
            if (!PNOwnerImageAge.TryRead(sample, pendingEpoch, item.Stamp, out double age)) { AgeRejected++; continue; }
            LastAge = age;
            try { if (layer.Receive(item.Id, item.Displacement, item.Duration, item.Height, (float)age)) AppliedCount++; else ReceiveRefused++; }
            catch (ArgumentOutOfRangeException) { }
        }
    }
}
#endif
