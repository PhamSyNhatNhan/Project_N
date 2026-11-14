using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

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

public interface IPNSkillDecisionSource
{
    // Takes the oldest answer for the named point, if one arrived (and has not expired).
    bool TryTake(string point, out JToken value);
}

public partial class PNSkill
{
    private readonly List<IPNSkillHitReceiver> hitReceivers = new List<IPNSkillHitReceiver>();
    // Set on the authority by the component that receives the remote owner's decisions; null for an own or offline player.
    public IPNSkillDecisionSource DecisionSource { get; set; }

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
