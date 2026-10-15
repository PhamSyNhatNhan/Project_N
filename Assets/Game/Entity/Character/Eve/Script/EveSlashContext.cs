using EveEngine;
using UnityEngine;

// Per-hit presentation/geometry inputs; the skill still owns combo and rewards.
public sealed class EveSlashContext : PNSkillActionContext
{
    public int Stroke { get; }
    public float Scale { get; }
    public float Energy { get; }
    public EveSlashContext(PNSkill skill, EveId id, PNSkillData data, EveRegistryHub registry,
        Vector3 direction, int stroke, float scale, float energy)
        : base(skill, id, data, registry, null, direction)
    { Stroke = stroke; Scale = scale; Energy = energy; }
#if UNITY_5_3_OR_NEWER
    public void Configure(PNArcSlashVisual visual)
        => ConfigureVisual(visual, Stroke, Scale, Energy);
    public static void ConfigureVisual(PNArcSlashVisual visual, int stroke, float scale, float energy)
    {
        visual.EnergyIntensity = energy;
        if (visual.Tracks == null) return;
        foreach (var track in visual.Tracks)
        {
            track.Geometry.Radius *= scale; track.Geometry.InnerRadius *= scale;
            track.Geometry.Height = Mathf.Max(track.Geometry.Height, .65f);
            track.Offset.y = .4f;
            // Tilt the sweep rather than rotate aim: right-down / left-up / horizontal.
            track.Euler.x += stroke == 0 ? 38f : stroke == 1 ? -38f : 0f;
            if (stroke != 2)
            {
                var keys = track.Yaw.keys;
                float sweep = stroke == 0 ? 90f : -90f;
                for (int i = 0; i < keys.Length; i++)
                {
                    float sign = stroke == 1 ? -1 : 1;
                    keys[i].value = keys[i].value * sign + (keys[i].time - .5f) * sweep;
                    keys[i].inTangent = keys[i].inTangent * sign + sweep;
                    keys[i].outTangent = keys[i].outTangent * sign + sweep;
                }
                track.Yaw = new AnimationCurve(keys);
            }
        }
    }
#endif
}
