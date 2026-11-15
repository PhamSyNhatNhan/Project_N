using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: G4. A decision point inside a cast: the only place where a running cast chooses again, written once for every execution mode (`invocation.Choose`).
//       Predicted  - the owner's machine runs `compute` and tells the authority what it chose (a proposal).
//       Authority  - for a remote owner, takes the proposal if the value is well formed, legal for the framework (an entity that exists, is ready, shares the scene, is in `reach`) and accepted by the skill's
//                    `validate`; anything else falls back to `compute`. For the owner's own / offline cast there is no proposal, so it is `compute`.
//       Observed   - `compute` (an observer draws, it does not decide).
//     The skill only writes the two lambdas and names the point; the encoding, the wire and the reach slack for latency belong to the framework. Supported values: bool, int, string and PNStat.
// VI: G4. Điểm quyết định giữa cast: nơi duy nhất cast đang chạy chọn lại, viết một lần cho mọi chế độ. Skill chỉ viết `compute` / `validate` và đặt tên điểm; mã hóa, đường truyền và độ nới tầm do trễ thuộc bộ khung.
public static class PNSkillDecisions
{
    public const int MaxPointLength = 48;
    // A decision point is named by the skill at the `Choose` call; the host only needs the name to be sane.
    public static bool IsValidPoint(string point)
    {
        if (string.IsNullOrEmpty(point) || point.Length > MaxPointLength) return false;
        foreach (var c in point) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
        return true;
    }
    public static bool IsSupported(Type type) => type == typeof(bool) || type == typeof(int) || type == typeof(string) || type == typeof(PNStat);

    internal static bool TryEncode<T>(T value, out JToken token)
    {
        token = null; object boxed = value; if (boxed == null) return false;
        switch (boxed)
        {
            case bool flag: token = new JObject { ["v"] = flag ? 1 : 0 }; return true;
            case int number: token = new JObject { ["v"] = number }; return true;
            case string text: token = new JObject { ["v"] = text }; return true;
            case PNStat stat: { long id = stat.EntityId.Value; if (id == 0) return false; token = new JObject { ["t"] = id }; return true; }
            default: return false;
        }
    }

    internal static bool TryDecode<T>(JToken token, PNSkill skill, float reach, float slack, out T value)
    {
        value = default;
        if (!(token is JObject o)) return false;
        if (typeof(T) == typeof(bool)) { var v = o["v"]; if (v == null || v.Type != JTokenType.Integer) return false; value = (T)(object)((long)v != 0); return true; }
        if (typeof(T) == typeof(int)) { var v = o["v"]; if (v == null || v.Type != JTokenType.Integer) return false; long n = (long)v; if (n < int.MinValue || n > int.MaxValue) return false; value = (T)(object)(int)n; return true; }
        if (typeof(T) == typeof(string)) { var v = o["v"]; if (v == null || v.Type != JTokenType.String) return false; value = (T)(object)(string)v; return true; }
        if (typeof(T) == typeof(PNStat))
        {
            var found = ResolveEntity(o, skill, reach, slack); if (found == null) return false;
            value = (T)(object)found; return true;
        }
        return false;
    }

    // An entity the session knows, ready, in the same scene, not the skill's own entity and, when the skill gave a reach, within it (plus the slack the network needs).
    private static PNStat ResolveEntity(JObject o, PNSkill skill, float reach, float slack)
    {
#if UNITY_5_3_OR_NEWER
        if (skill == null || o["t"] == null || o["t"].Type != JTokenType.Integer) return null;
        long id = (long)o["t"]; if (id == 0) return null;
        if (!EveRegistrySession.IsReady || !EveRegistrySession.Context.Entities.TryGet(new EveEntityId(id), out var loader) || loader == null) return null;
        var stat = loader.GetComponent<PNStat>(); var own = skill.GetComponent<PNStat>();
        if (stat == null || !stat.IsReady || ReferenceEquals(stat, own) || stat.gameObject.scene != skill.gameObject.scene) return null;
        if (!float.IsInfinity(reach))
        {
            var offset = stat.transform.position - skill.transform.position; offset.y = 0;
            if (offset.magnitude > reach + Mathf.Max(0, slack)) return null;
        }
        return stat;
#else
        return null;
#endif
    }
}

public sealed partial class PNSkillInvocation
{
    // `point` names the decision; `compute` is the skill's own choice; `validate` (optional) is the skill's rule for a value a remote owner proposed (game rules only: the framework already checked the
    // value is well formed and, for an entity, that it exists and is within `reach`). `reach` is the planar distance from the skill's entity an entity value must be within (metres; unlimited by default).
    public T Choose<T>(string point, Func<T> compute, Func<T, bool> validate = null, float reach = float.PositiveInfinity)
    {
        if (!PNSkillDecisions.IsValidPoint(point)) throw new ArgumentException("A decision point is named with lowercase letters, digits and underscores (at most " + PNSkillDecisions.MaxPointLength + ").", nameof(point));
        if (!PNSkillDecisions.IsSupported(typeof(T))) throw new NotSupportedException("A decision value is a bool, int, string or PNStat, not " + typeof(T).Name + ".");
        if (compute == null) throw new ArgumentNullException(nameof(compute));
        var skill = State.Skill;
        switch (Mode)
        {
            case PNSkillMode.Predicted:
            {
                var chosen = compute();
                if (!IsEnded && skill != null && PNSkillDecisions.TryEncode(chosen, out var token)) skill.ReportSink?.ReportDecision(CastKey, point, token);
                return chosen;
            }
            case PNSkillMode.Authority:
            {
                var source = IsEnded || skill == null ? null : skill.DecisionSource;
                if (source != null && source.TryTake(CastKey, point, out var token) && PNSkillDecisions.TryDecode<T>(token, skill, reach, source.ReachSlack, out var proposed) && (validate == null || validate(proposed))) return proposed;
                return compute();
            }
            default: return compute();
        }
    }
}

public partial class PNSkill
{
    // The same for a skill that may be asked outside a cast (no invocation): it is its own `compute`.
    protected T Choose<T>(string point, Func<T> compute, Func<T, bool> validate = null, float reach = float.PositiveInfinity)
    {
        var cast = CurrentInvocation;
        if (cast != null && !cast.IsEnded) return cast.Choose(point, compute, validate, reach);
        if (compute == null) throw new ArgumentNullException(nameof(compute));
        return compute();
    }
}
