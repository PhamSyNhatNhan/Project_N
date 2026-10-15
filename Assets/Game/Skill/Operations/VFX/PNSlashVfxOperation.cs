using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Validated visual parameters, without hitboxes or damage data.
// VI: Tham số hình ảnh đã kiểm tra, không chứa hitbox hoặc damage.
public class PNSlashVfxSettings
{
    public float Duration { get; }
    public float Radius { get; }
    public float Height { get; }
    public float ForwardOffset { get; }
    public string Prefab { get; }
    public bool HasPrefab { get; }
    public bool FollowCaster { get; }
    public PNSlashVfxSettings(JToken data)
    {
        if (!(data is JObject value)) throw new FormatException("Slash perform data must be an object.");
        Duration = Read(value, "duration", 0.32f, true);
        Radius = Read(value, "radius", 0.6f, true);
        Height = Read(value, "height", 0.18f, false);
        ForwardOffset = Read(value, "forwardOffset", 0.15f, false);
        var prefab = value["prefab"];
        HasPrefab = prefab != null;
        if (prefab != null && (prefab.Type != JTokenType.String || string.IsNullOrWhiteSpace(prefab.Value<string>())))
            throw new FormatException("Slash prefab must be a Resources path.");
        Prefab = prefab == null ? "PNVfx/PNArcSlash" : prefab.Value<string>();
        var follow = (value["hitbox"] as JObject)?["followCaster"];
        if (follow != null && follow.Type != JTokenType.Boolean) throw new FormatException("Slash followCaster must be Boolean.");
        FollowCaster = follow != null && follow.Value<bool>();
    }
    private static float Read(JObject value, string name, float fallback, bool positive)
    {
        var token = value[name];
        if (token != null && token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            throw new FormatException($"Slash {name} must be a number.");
        float result = token == null ? fallback : token.Value<float>();
        if (float.IsNaN(result) || float.IsInfinity(result) || (positive ? result <= 0f : result < 0f))
            throw new FormatException($"Invalid slash {name}.");
        return result;
    }
}

// EN: Player/enemy skills supply their own persistent pool to the reusable slash operation.
// VI: Skill player/enemy cung cấp pool riêng lâu dài cho operation chém dùng chung.
public interface PNSlashVfxPoolOwner
{
    EveEasyPooling GetSlashVfxPool();
}

// EN: Borrows a slash VFX; the parent action advances it and returns it on Exit/Cancel.
// VI: Mượn VFX chém; action cha điều khiển thời gian và trả về pool khi Exit/Cancel.
public class PNSlashVfxOperation : PNSkillOperation
{
    private PNArcSlashVisual visual;
    private float elapsed;
    private PNSlashVfxSettings settings;
#if UNITY_5_3_OR_NEWER
    private PNSkill caster;
    private UnityEngine.Vector3 casterOrigin, visualOrigin;
#endif
    public PNArcSlashVisual Visual => visual;
    public virtual bool IsComplete => settings != null && elapsed >= settings.Duration;

    public override void Execute(PNSkillActionContext context, JToken data)
    {
        if (settings != null) throw new InvalidOperationException("Slash operation already executed.");
        if (!context.Direction.HasValue)
            throw new InvalidOperationException("Slash requires a direction supplied by the skill.");
        settings = new PNSlashVfxSettings(data);
        if (!(context.Skill is PNSlashVfxPoolOwner owner))
            throw new InvalidOperationException("Slash requires a skill-owned VFX pool.");
        visual = PNArcSlashVisual.Spawn(owner.GetSlashVfxPool(), context.Skill, settings, context.Direction.Value);
#if UNITY_5_3_OR_NEWER
        caster = context.Skill; casterOrigin = caster.transform.position; visualOrigin = visual.AttackRoot.position;
#endif
    }
    public virtual void Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (settings == null) return;
        elapsed = Math.Min(settings.Duration, elapsed + deltaTime);
#if UNITY_5_3_OR_NEWER
        if (settings.FollowCaster && visual != null && caster != null)
            visual.AttackRoot.position = visualOrigin + caster.transform.position - casterOrigin;
#endif
        if (visual != null) visual.Render(elapsed / settings.Duration);
    }
    public override void Cancel() => Dispose();
    public override void Dispose()
    {
        if (visual != null) visual.Release();
        visual = null;
#if UNITY_5_3_OR_NEWER
        caster = null;
#endif
    }
}
