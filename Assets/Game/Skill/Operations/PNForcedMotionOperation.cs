using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public enum PNForcedMotionKind { Push, Pull, Direction }

// EN: Payload of `project_n:forced_motion` (Docs/BodyMotionPlan.md 3.3): `kind` push (away from the hit point), pull (towards it, stopping `stopDistance` short) or direction (along the cast direction); `distance` (m),
//     `duration` (s), `height` (m, optional lift) and `crowdControl`, the registered strength (default 1) that goes through the crowd_control tag (sender Scale, receiver Mitigation, penetration).
//     The final factor multiplies distance, duration and height.
// VI: Payload cua `project_n:forced_motion`: kind push/pull/direction, distance, duration, height tuy chon va crowdControl (gia tri khong che dang ky, mac dinh 1) di qua tag crowd_control; he so cuoi nhan quang, thoi luong, do cao.
public sealed class PNForcedMotionSettings
{
    public PNForcedMotionKind Kind { get; }
    public float Distance { get; }
    public float Duration { get; }
    public float Height { get; }
    public double CrowdControl { get; }
    public float StopDistance { get; }
    private PNForcedMotionSettings(PNForcedMotionKind kind, float distance, float duration, float height, double crowdControl, float stop)
    { Kind = kind; Distance = distance; Duration = duration; Height = height; CrowdControl = crowdControl; StopDistance = stop; }
    public static PNForcedMotionSettings Parse(JToken payload)
    {
        if (!(payload is JObject data)) throw new FormatException("Forced motion operation requires an object payload.");
        foreach (var field in data.Properties())
            if (field.Name != "kind" && field.Name != "distance" && field.Name != "duration" && field.Name != "height" && field.Name != "crowdControl" && field.Name != "stopDistance")
                throw new FormatException("Unknown forced motion field: " + field.Name);
        string kindText = data["kind"]?.Type == JTokenType.String ? (string)data["kind"] : throw new FormatException("Forced motion kind is required (push, pull or direction).");
        PNForcedMotionKind kind = kindText == "push" ? PNForcedMotionKind.Push : kindText == "pull" ? PNForcedMotionKind.Pull : kindText == "direction" ? PNForcedMotionKind.Direction
            : throw new FormatException("Forced motion kind must be push, pull or direction.");
        double Number(string name, bool required, double fallback)
        {
            var token = data[name];
            if (token == null) { if (required) throw new FormatException("Forced motion " + name + " is required."); return fallback; }
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("Forced motion " + name + " must be a number.");
            double value = (double)token; if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Forced motion " + name + " must be finite.");
            return value;
        }
        float distance = (float)Number("distance", true, 0d), duration = (float)Number("duration", true, 0d), height = (float)Number("height", false, 0d), stop = (float)Number("stopDistance", false, .6d);
        double cc = Number("crowdControl", false, 1d);
        if (distance < 0f || distance > PNForcedMotionLimits.MaxDistance) throw new FormatException("Forced motion distance must be in [0, " + PNForcedMotionLimits.MaxDistance + "].");
        if (!(duration > 0f) || duration > PNForcedMotionLimits.MaxDuration) throw new FormatException("Forced motion duration must be in (0, " + PNForcedMotionLimits.MaxDuration + "].");
        if (height < 0f || height > PNForcedMotionLimits.MaxHeight) throw new FormatException("Forced motion height must be in [0, " + PNForcedMotionLimits.MaxHeight + "].");
        if (cc < 0d) throw new FormatException("Forced motion crowdControl must be nonnegative.");
        if (stop < 0f) throw new FormatException("Forced motion stopDistance must be nonnegative.");
        if (distance == 0f && height == 0f) throw new FormatException("Forced motion needs a distance or a height.");
        return new PNForcedMotionSettings(kind, distance, duration, height, cc, stop);
    }
}

public sealed class PNForcedMotionResult
{
    public PNStat Target { get; }
    public double Factor { get; }
    public uint CommandId { get; }
    public Vector3 Displacement { get; }
    public PNForcedMotionResult(PNStat target, double factor, uint commandId, Vector3 displacement) { Target = target; Factor = factor; CommandId = commandId; Displacement = displacement; }
}

// EN: The host's decision to move bodies it hit. It runs only with authority (a guess or a replay skips it: the body is moved by the host's command, never by a guess). Each target gets its factor from the crowd_control tag;
//     factor 0 means the target is not moved at all (the damage of the same hit is untouched). The command goes to the target's own forced layer (`PNForcedMotion`), which moves a local body at once and sends a client's
//     player to its owner's machine.
// VI: Quyet dinh cua host: day/hut than bi trung. Chi chay khi co authority. Moi muc tieu lay he so tu tag crowd_control; he so 0 thi khong bi day (sat thuong van tinh). Lenh vao lop chuyen dong bi ep cua muc tieu.
public class PNForcedMotionOperation : PNSkillOperation
{
    private bool executed, disposed;
    private readonly List<PNForcedMotionResult> results = new List<PNForcedMotionResult>();
    public virtual IReadOnlyList<PNForcedMotionResult> Results => results.AsReadOnly();

    public override void Execute(PNSkillActionContext context, JToken data)
    {
        if (executed || disposed) throw new InvalidOperationException("Forced motion operation cannot execute twice or after disposal.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (context.Mode != PNSkillMode.Authority) { executed = true; return; }
        var settings = PNForcedMotionSettings.Parse(data);
        var caster = context.Skill.GetComponent<PNStat>();
        if (!context.Skill.IsReady || caster == null || !caster.IsReady) throw new InvalidOperationException("Forced motion operation requires Ready caster stats and skill.");
        PNWorldGameplay.RequireTargets(context.Skill, context.Targets);
#if UNITY_5_3_OR_NEWER
        var control = caster.CreateCrowdControl(settings.CrowdControl);
        Vector3 origin = context.Position ?? caster.transform.position;
        var seen = new HashSet<EveEntityId>();
        executed = true;
        foreach (var target in context.Targets)
        {
            if (target == null || !target.IsReady || target.IsDead || target.SessionId != caster.SessionId || !seen.Add(target.EntityId)) continue;
            var move = target.GetComponent<PNMove>();
            if (move == null || !PNForcedMotion.CanDecide(move)) continue;
            double factor = target.ReceiveCrowdControl(control);
            if (!(factor > 0d)) { results.Add(new PNForcedMotionResult(target, 0d, 0, Vector3.zero)); continue; }
            var toTarget = target.transform.position - origin; toTarget.y = 0f;
            Vector3 direction;
            float distance = (float)Math.Min(PNForcedMotionLimits.MaxDistance, settings.Distance * factor);
            if (settings.Kind == PNForcedMotionKind.Direction || toTarget.sqrMagnitude < 1e-6f) direction = Planar(context.Direction, Vector3.right);
            else direction = toTarget.normalized * (settings.Kind == PNForcedMotionKind.Push ? 1f : -1f);
            if (settings.Kind == PNForcedMotionKind.Pull) distance = Mathf.Min(distance, Mathf.Max(0f, toTarget.magnitude - settings.StopDistance));
            float duration = (float)Math.Min(PNForcedMotionLimits.MaxDuration, settings.Duration * factor), height = (float)Math.Min(PNForcedMotionLimits.MaxHeight, settings.Height * factor);
            var displacement = direction * distance;
            if (displacement.sqrMagnitude < 1e-8f && height <= 0f) { results.Add(new PNForcedMotionResult(target, factor, 0, Vector3.zero)); continue; }
            uint id = PNForcedMotion.Of(move).Issue(displacement, duration, height);
            results.Add(new PNForcedMotionResult(target, factor, id, displacement));
        }
#endif
    }

#if UNITY_5_3_OR_NEWER
    private static Vector3 Planar(Vector3? value, Vector3 fallback)
    {
        if (value == null) return fallback;
        var planar = new Vector3(value.Value.x, 0f, value.Value.z);
        return planar.sqrMagnitude < 1e-6f ? fallback : planar.normalized;
    }
#endif

    public override void Cancel() => Dispose();
    public override void Dispose() { disposed = true; }
}
