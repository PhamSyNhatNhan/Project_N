using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Looks (Docs/ObservedViewPlan.md PV3). A look is a picture that logic only NAMES; the registry owns how it is drawn, so no character or Action carries network code for it.
//     Two shapes share one registry (`PNLookDefinition`, like a GameplayCue):
//       - a look ATTACHED TO A RUN: the Action's skill data says `"look": "<id>"` in its perform payload; the engine builds it with the run's view and moves it with the run's age (a dash trail);
//       - a look SUSTAINED ON AN ENTITY: logic calls `skill.SetLook(id, on)`; every machine draws it (the owner's guess and the authority at once, everyone else from the authority's picture, a late joiner
//         from its first image). The picture rides the entity's `Presentation` block: key = the look's path, value = its namespace.
//     A look never touches gameplay state. Display only: not a trust boundary.
// VI: Look là hình mà logic chỉ GỌI TÊN; registry lo cách vẽ nên nhân vật/Action không mang code mạng. Hai dạng chung registry: gắn run (dữ liệu skill khai `look`) và duy trì trên entity (`SetLook`).
public abstract class PNLook
{
    // True while the look still shows something after `End` (a fading trail); the skill keeps it and ends it again on teardown.
    public virtual bool HasTail => false;
    // Only a look attached to a run is advanced: `age` and `rate` are the run's, `seeking` is true when the age was set instead of advanced.
    public virtual void Advance(double age, double rate, bool seeking) { }
    public abstract void End(PNActionViewEnd kind);
}

public readonly struct PNLookBuild
{
    public readonly PNSkill Skill;
    public readonly EveId Id;
    public readonly PNSkillData Data;       // the run's skill data; null for a look sustained on an entity
    public readonly Vector3 Direction;
    public readonly Vector3? Position;
    public readonly double Scale;
    public PNLookBuild(PNSkill skill, EveId id, PNSkillData data, Vector3 direction, Vector3? position, double scale)
    { Skill = skill; Id = id; Data = data; Direction = direction; Position = position; Scale = scale; }
}

public sealed class PNLookDefinition : EveIIdentified
{
    public const int MaxPathLength = PNPresentation.MaxKeyLength;
    public EveId Id { get; }
    private readonly Func<PNLookBuild, PNLook> factory;
    // A look sustained on an entity travels as the entity's presentation key, so its path is that key: lowercase letters, digits and underscores, at most 24.
    public PNLookDefinition(EveId id, Func<PNLookBuild, PNLook> factory)
    {
        if (!id.IsValid) throw new ArgumentException("Look ID is required.", nameof(id));
        if (!PNPresentation.IsValidKey(id.Path)) throw new ArgumentException("A look path is lowercase letters, digits and underscores (at most " + MaxPathLength + "): " + id, nameof(id));
        if (!PNPresentation.IsValidValue(id.Namespace) || id.Namespace.Length == 0) throw new ArgumentException("A look namespace is at most " + PNPresentation.MaxValueLength + " characters: " + id, nameof(id));
        Id = id; this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    internal PNLook Create(PNLookBuild build) => factory(build);
}

// The engine's view for an Action whose skill data names a look: the look follows the run's age and ends with the run.
internal sealed class PNLookRunView : IPNActionView
{
    private readonly PNLook look;
    public PNLookRunView(PNLook look) { this.look = look; }
    public bool HasTail => look.HasTail;
    public bool Lead { set { } }
    public void Advance(double age, double rate, bool seeking) => look.Advance(age, rate, seeking);
    public void Fact(string key, double[] values, double age) { }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind) => look.End(kind);
}

public static class PNLookData
{
    // The look an Action's skill data names (`perform.look`), or null.
    public static EveId? Declared(PNSkillData data)
    {
        if (!(data?.Action.Perform is JObject perform) || perform["look"] == null) return null;
        if (perform["look"].Type != JTokenType.String) throw new FormatException("An Action's look is an ID string.");
        return EveId.Parse((string)perform["look"]);
    }
    public static bool Declares(PNSkillData data)
    {
        try { return Declared(data).HasValue; } catch (FormatException) { return false; }
    }
    public static void Validate(PNSkillActionData data, PNSkillActionDefinition definition, EveRegistryHub hub)
    {
        if (!(data.Perform is JObject perform) || perform["look"] == null) return;
        if (perform["look"].Type != JTokenType.String) throw new FormatException("An Action's look is an ID string.");
        var id = EveId.Parse((string)perform["look"]);
        hub.Get<PNLookDefinition>().Get(id);
        if (definition.ViewDeclared && !definition.HasView)
            throw new FormatException("Action " + definition.Id + " declares NoView, so its data cannot name the look " + id + ".");
    }
}

public partial class PNSkill
{
    private readonly Dictionary<EveId, PNLook> entityLooks = new Dictionary<EveId, PNLook>();
    private readonly List<PNLook> lookTails = new List<PNLook>();
    // Looks that are wanted but cannot be drawn yet (the factory answered null: the rig is not built). The engine tries them again every frame until drawn or turned off.
    private readonly HashSet<EveId> pendingLooks = new HashSet<EveId>();
    private bool lookPictureHooked;
    public int EntityLookCount => entityLooks.Count;
    public int PendingLookCount => pendingLooks.Count;
    public int LookFailures { get; private set; }
    public bool HasLook(EveId id) => entityLooks.ContainsKey(id);

    // Logic names a look and turns it on or off. Every machine draws it at once (where the entity is simulated: the authority and the owner's guess); the authority also puts it in the entity's
    // picture, which is how everyone else gets it. Returns whether anything changed here. A look this machine does not know (another mod set) is simply not drawn.
    public bool SetLook(EveId id, bool on)
    {
        if (!IsReady || registry == null) return false;
        // The picture is published even where this machine draws nothing (a host without visuals still tells everyone else).
        bool changed = on ? StartLook(id) : StopLook(id);
        try { changed |= Presentation.Set(id.Path, on ? id.Namespace : ""); }
        catch (InvalidOperationException error) { Debug.LogException(error, this); LookFailures++; }
        return changed;
    }

    private bool StartLook(EveId id)
    {
        if (entityLooks.ContainsKey(id)) return false;
        if (!registry.Get<PNLookDefinition>().TryGet(id, out var definition)) return false;
        PNLook look;
        try { look = definition.Create(new PNLookBuild(this, id, null, new Vector3(0f, 0f, 1f), null, 1)); }
        catch (Exception error) { Debug.LogException(error, this); LookFailures++; return false; }
        if (look == null) { pendingLooks.Add(id); return false; }
        pendingLooks.Remove(id);
        entityLooks.Add(id, look); return true;
    }

    private bool StopLook(EveId id)
    {
        pendingLooks.Remove(id);
        if (!entityLooks.TryGetValue(id, out var look)) return false;
        entityLooks.Remove(id);
        try { look.End(PNActionViewEnd.Completed); if (look.HasTail) lookTails.Add(look); }
        catch (Exception error) { Debug.LogException(error, this); LookFailures++; }
        return true;
    }

    // Another machine's picture changed: a pure observer draws it. The authority and the owner's machine draw their own looks and ignore the picture.
    private void OnLookPicture(string key, string value)
    {
        if (!IsReady || registry == null || HasGameplayAuthority || IsOwnerClient) return;
        if (value.Length == 0)
        {
            var gone = new List<EveId>();
            foreach (var id in entityLooks.Keys) if (id.Path == key) gone.Add(id);
            foreach (var id in pendingLooks) if (id.Path == key) gone.Add(id);
            foreach (var id in gone) StopLook(id);
            return;
        }
        if (EveId.TryParse(value + ":" + key, out var look)) StartLook(look);
    }

    // Draw what the picture already says. Nobody has to call this: a look whose factory is not ready yet is retried by the engine every frame (`RetryPendingLooks`). Kept for checks.
    public void ReplayLooks()
    {
        if (!IsReady || registry == null) return;
        foreach (var pair in Presentation.Values) OnLookPicture(pair.Key, pair.Value);
    }

    private void LateUpdate()
    {
        if (pendingLooks.Count == 0 || !IsReady || registry == null) return;
        foreach (var id in new List<EveId>(pendingLooks)) { pendingLooks.Remove(id); StartLook(id); }
    }

    private void HookLooks()
    {
        if (lookPictureHooked) return;
        lookPictureHooked = true; Presentation.Changed += OnLookPicture;
    }

    private void ClearEntityLooks()
    {
        if (lookPictureHooked) { lookPictureHooked = false; if (presentation != null) presentation.Changed -= OnLookPicture; }
        foreach (var look in entityLooks.Values)
            try { look.End(PNActionViewEnd.Cancelled); } catch (Exception error) { Debug.LogException(error, this); }
        entityLooks.Clear(); pendingLooks.Clear();
        foreach (var tail in lookTails)
            try { tail.End(PNActionViewEnd.Cancelled); } catch (Exception error) { Debug.LogException(error, this); }
        lookTails.Clear();
    }
}
