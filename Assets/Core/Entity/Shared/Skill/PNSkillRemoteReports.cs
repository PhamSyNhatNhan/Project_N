using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: What a skill running for a REMOTE owner may take from that owner's machine (Docs/SkillNetworkingAPlan.md S4-S5): hits the owner's client saw (N5) and the choices the owner made at a decision point
//     inside a cast (N9). The authority still runs the real cast; a reported hit is applied only if the running action accepts it after its own checks, and a decision is only a proposal the skill
//     validates (anything it refuses falls back to the skill's own choice).
// VI: Điều skill chạy cho chủ nhân Ở XA được nhận từ máy chủ nhân: trúng mà client chủ thấy (N5) và lựa chọn tại điểm quyết định giữa cast (N9). Authority vẫn chạy cast thật; hit báo lên chỉ áp khi action đang
//     chạy chấp nhận sau kiểm riêng của nó; quyết định chỉ là đề xuất, bị từ chối thì dùng lựa chọn của skill.
public interface IPNSkillHitReceiver
{
    // True when the hit was applied. `tolerance` is the slack (metres) a reported hit gets over the action's own reach, for the time the report took to arrive.
    bool AcceptReportedHit(PNStat target, int step, float tolerance);
}

// Where a client's own run of a cast sends what the authority needs to know (hits it saw, choices it made).
public interface IPNSkillReportSink
{
    // `castKey` is the key of the cast that made the hit / the choice (`PNSkillInvocation.CastKey`); `step` tells the action contexts of one cast apart.
    void ReportHit(long castKey, PNStat target, int step);
    void ReportDecision(long castKey, string point, JToken value);
}

public interface IPNSkillDecisionSource
{
    // Takes the answer the owner gave for the named point of the cast with this key, if one arrived (and has not expired).
    bool TryTake(long castKey, string point, out JToken value);
    // The slack (metres) an entity a remote owner named gets over the skill's own reach, for the time the proposal took to arrive.
    float ReachSlack { get; }
}

public partial class PNSkill
{
    private readonly List<IPNSkillHitReceiver> hitReceivers = new List<IPNSkillHitReceiver>();
    // Set on the authority by the component that receives the remote owner's decisions; null for an own or offline player.
    public IPNSkillDecisionSource DecisionSource { get; set; }
    // Set on the owner's client by the component that sends the reports.
    public IPNSkillReportSink ReportSink { get; set; }
    private sealed class HitLease : IDisposable
    {
        private PNSkill owner; private IPNSkillHitReceiver receiver;
        public HitLease(PNSkill owner, IPNSkillHitReceiver receiver) { this.owner = owner; this.receiver = receiver; }
        public void Dispose() { if (owner == null) return; owner.hitReceivers.Remove(receiver); owner = null; receiver = null; }
    }

    // A running action that can take reported hits registers while it runs and disposes the lease when it ends.
    public IDisposable RegisterHitReceiver(IPNSkillHitReceiver receiver)
    {
        if (receiver == null) throw new ArgumentNullException(nameof(receiver));
        hitReceivers.Add(receiver); return new HitLease(this, receiver);
    }
    public int HitReceiverCount => hitReceivers.Count;

    public bool DeliverReportedHit(PNStat target, int step, float tolerance)
    {
        if (target == null || tolerance < 0 || float.IsNaN(tolerance)) return false;
        foreach (var receiver in hitReceivers.ToArray()) if (receiver.AcceptReportedHit(target, step, tolerance)) return true;
        return false;
    }
}

public partial class PNSkill
{
    // True on the machine of the entity's owner (its client forwards the owner's input): it drew the cast itself, so it ignores the events.
    public bool IsOwnerClient => CanForwardInput;
}
