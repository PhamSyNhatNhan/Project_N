using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// Payload of project_n:directional (every field required except gravity, subSteps and hitCooldown). Immutable; parsed once per definition.
//   speed, acceleration (units/s, units/s^2; 0 allowed), speedMin <= speed <= speedMax,
//   life {mode: "Time" | "Distance", value > 0}  (exactly one of lifetime seconds or maximum distance),
//   volume {size: [x, y, z]} (full size of the body box: x right, y up, z forward),
//   environmentMask: layer names treated as walls/ground (default content: ["Wall"]; [] = ignore the environment),
//   gravity (optional): [x, y, z] units/s^2 added as an external velocity; absent = off,
//   subSteps (optional): 1..16, further limited by the manager cap,
//   hitLimit: number of hits (> 0) after which the projectile ends,
//   hitCooldown (optional, seconds of the projectile clock >= 0): the same target can be hit again after it; absent = each target once.
public sealed class PNDirectionalSettings
{
    public double Speed, Acceleration, SpeedMin, SpeedMax, LifeValue;
    public bool ByDistance;
    public float SizeX, SizeY, SizeZ;
    public IReadOnlyList<string> EnvironmentLayers;
    public double[] Gravity;
    public int SubSteps, HitLimit;
    public double HitCooldown; public bool HasCooldown;
    public PNDirectionalSettings(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "speed", "acceleration", "speedMin", "speedMax", "life", "volume", "environmentMask", "gravity", "subSteps", "hitLimit", "hitCooldown");
        Speed = Number(obj, "speed"); Acceleration = PNBuffContentBatch.Number(obj, "acceleration"); SpeedMin = Number(obj, "speedMin"); SpeedMax = Number(obj, "speedMax");
        if (SpeedMax < SpeedMin || Speed < SpeedMin || Speed > SpeedMax) throw new FormatException("Projectile speed must lie within speedMin..speedMax.");
        if (Math.Abs(Acceleration) > 1e9) throw new FormatException("Projectile acceleration is out of range.");
        var life = PNBuffContentBatch.Object(obj["life"]); PNBuffContentBatch.Fields(life, "mode", "value");
        string mode = PNBuffContentBatch.String(life, "mode");
        if (mode != "Time" && mode != "Distance") throw new FormatException("Projectile life mode must be Time or Distance.");
        ByDistance = mode == "Distance"; LifeValue = PNBuffContentBatch.Number(life, "value");
        if (LifeValue <= 0 || LifeValue > 1e9) throw new FormatException("Projectile life value must be positive.");
        var volume = PNBuffContentBatch.Object(obj["volume"]); PNBuffContentBatch.Fields(volume, "size");
        var size = PNBuffContentBatch.Array(volume["size"]).ToArray();
        if (size.Length != 3 || size.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer)) throw new FormatException("Projectile volume.size needs three numbers.");
        var s = size.Select(x => x.Value<double>()).ToArray();
        if (s.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x <= 0 || x > 1000)) throw new FormatException("Projectile volume sizes must be positive and finite.");
        SizeX = (float)s[0]; SizeY = (float)s[1]; SizeZ = (float)s[2];
        if (obj["environmentMask"] == null || !(obj["environmentMask"] is JArray)) throw new FormatException("Projectile environmentMask is required (use [] for none).");
        var layers = PNBuffContentBatch.Array(obj["environmentMask"]).Select(x => x.Type == JTokenType.String && !string.IsNullOrWhiteSpace(x.Value<string>()) ? x.Value<string>() : throw new FormatException("Layer name string required.")).ToArray();
        EnvironmentLayers = Array.AsReadOnly(layers.Distinct().ToArray());
        if (obj["gravity"] != null)
        {
            var g = PNBuffContentBatch.Array(obj["gravity"]).ToArray();
            if (g.Length != 3 || g.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer)) throw new FormatException("Projectile gravity needs three numbers.");
            Gravity = g.Select(x => x.Value<double>()).ToArray();
            if (Gravity.Any(x => double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 1e6)) throw new FormatException("Projectile gravity must be finite.");
        }
        HitLimit = PNBuffContentBatch.Integer(obj, "hitLimit"); if (HitLimit < 1 || HitLimit > 100000) throw new FormatException("Projectile hitLimit must be 1..100000.");
        if (obj["hitCooldown"] != null) { HitCooldown = PNBuffContentBatch.Number(obj, "hitCooldown"); HasCooldown = true; if (HitCooldown < 0 || HitCooldown > 1e6) throw new FormatException("Projectile hitCooldown must be nonnegative."); }
        if (obj["subSteps"] != null) { SubSteps = PNBuffContentBatch.Integer(obj, "subSteps"); if (SubSteps < 1 || SubSteps > 16) throw new FormatException("Projectile subSteps must be 1..16."); }
    }
    private static double Number(JObject obj, string field)
    { double value = PNBuffContentBatch.Number(obj, field); if (value < 0 || value > 1e9) throw new FormatException("Projectile " + field + " must be nonnegative."); return value; }
    // Directional projectiles take no launch input.
    public static void ValidateInput(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input); PNBuffContentBatch.Fields(obj);
    }
    public static void ValidateDefinition(JToken token, EveRegistryHub hub) => ValidateLayers(new PNDirectionalSettings(token));
    public static void ValidateLayers(PNDirectionalSettings settings)
    {
#if UNITY_5_3_OR_NEWER
        foreach (var name in settings.EnvironmentLayers)
            if (UnityEngine.LayerMask.NameToLayer(name) < 0) throw new FormatException("Unknown environment layer " + name);
#endif
    }
}

#if UNITY_5_3_OR_NEWER
// EN: Flies along its heading with optional acceleration within a speed range, optional gravity, ends by time or distance and
//     ends on an environment hit, applies the launch damage and frozen OnHit effects to targets (hitLimit / hitCooldown).
// VI: Bay theo hướng với gia tốc tuỳ chọn trong khoảng tốc độ, gravity tuỳ chọn, kết thúc theo thời gian hoặc quãng đường
//     hoặc khi chạm môi trường. Chưa xử lý trúng mục tiêu (thuộc bước sát thương).
public class PNDirectionalProjectile : PNProjectileInstance
{
    private PNDirectionalSettings settings;
    private int environmentMask;
    private double speed, speedMin, speedMax, limit;
    // The motion heading chosen by this implementation; Forward (the box orientation) also follows the external velocity such as gravity.
    private Vector3 direction;
    protected Vector3 Direction { get => direction; set { if (value.sqrMagnitude > 1e-12f) direction = value.normalized; } }
    protected PNDirectionalSettings Settings => settings;
    // An implementation that extends the payload overrides this to return the directional part of its own settings.
    protected virtual PNDirectionalSettings ParseSettings() => Definition.Settings(payload => new PNDirectionalSettings(payload));
    protected double Speed => speed;
    protected override int EnvironmentMask => environmentMask;
    protected override Vector3 Gravity => settings.Gravity == null ? Vector3.zero : new Vector3((float)settings.Gravity[0], (float)settings.Gravity[1], (float)settings.Gravity[2]);
    protected override bool IsExpired => settings.ByDistance ? Distance >= limit : Age >= limit;
    protected override int SubStepLimit => settings.SubSteps > 0 ? settings.SubSteps : int.MaxValue;
    protected override void Initialize(PNProjectileSpawn spawn)
    {
        settings = ParseSettings();
        double factor = Launch.SpeedFactor;
        speed = settings.Speed * factor; speedMin = settings.SpeedMin * factor; speedMax = settings.SpeedMax * factor;
        limit = settings.LifeValue * Launch.LifetimeFactor;
        double scale = Launch.ScaleFactor;
        HalfExtents = new Vector3((float)(settings.SizeX * scale / 2), (float)(settings.SizeY * scale / 2), (float)(settings.SizeZ * scale / 2));
        direction = spawn.Direction; environmentMask = 0; lastHit.Clear();
        foreach (var name in settings.EnvironmentLayers) environmentMask |= 1 << LayerMask.NameToLayer(name);
        Velocity = direction * (float)speed;
    }
    protected override void Move(float dt)
    {
        if (settings.Acceleration != 0) speed = Math.Min(speedMax, Math.Max(speedMin, speed + settings.Acceleration * dt));
        Velocity = direction * (float)speed;
    }
    protected override void OnContacts(PNProjectileContacts contacts)
    {
        if (!contacts.HasEnvironment) { OnTargets(contacts.Targets); return; }
        // A wall ends the projectile, but targets in front of it (earlier along the step) are still hit first.
        var before = new List<PNProjectileContact>();
        foreach (var contact in contacts.Targets) if (contact.Fraction <= contacts.Environment.Fraction) before.Add(contact);
        if (before.Count != 0) OnTargets(before);
        Finish(PNProjectileEndReason.Environment);
    }
    // Hit rule: each overlapping target is hit once, or again after hitCooldown (this projectile's clock); the projectile ends after hitLimit hits.
    private readonly Dictionary<EveEntityId, float> lastHit = new Dictionary<EveEntityId, float>();
    protected virtual void OnTargets(IReadOnlyList<PNProjectileContact> targets)
    {
        for (int i = 0; i < targets.Count && IsRunning; i++)
        {
            var target = targets[i].Target;
            if (target == null) continue;
            if (lastHit.TryGetValue(target.EntityId, out float at) && (!settings.HasCooldown || Age - at < settings.HitCooldown)) continue;
            if (ApplyHit(targets[i]) == null) continue;
            lastHit[target.EntityId] = Age;
            if (HitCount >= settings.HitLimit) { Finish(PNProjectileEndReason.Hit); return; }
        }
    }
}
#else
// Placeholder so the standalone (non-Unity) build can register the implementation, like PNTimeScaleArea.
public class PNDirectionalProjectile { }
#endif
