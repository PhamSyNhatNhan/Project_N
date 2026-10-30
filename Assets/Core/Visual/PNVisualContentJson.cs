using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: JSON of the Visual registries (Docs/VisualPlan.md 3.3) inside `extensions.project_n`: visualRigs, visualParts, visualBuilds, visualMotions, visualMotionSets, visualExpressions,
//     visualAppearances. Unknown fields are refused, numbers must be finite and inside the hard limits (refused, never clamped), references are checked against the
//     pending batch and the registries with pack dependencies, and everything commits atomically with the rest of the batch.
// VI: JSON của các registry Visual trong `extensions.project_n`. Trường lạ bị từ chối, số phải hữu hạn trong giới hạn cứng, tham chiếu được kiểm theo phụ thuộc pack.
public sealed partial class PNBuffContentBatch
{
    public static readonly string[] VisualFields = { "visualRigs", "visualParts", "visualBuilds", "visualMotions", "visualMotionSets", "visualExpressions", "visualAppearances", "visualStringShapes", "visualStringColors", "visualStringStyles" };
    private readonly Dictionary<EveId, PNVisualRigDefinition> visualRigs = new Dictionary<EveId, PNVisualRigDefinition>();
    private readonly Dictionary<EveId, PNVisualPartDefinition> visualParts = new Dictionary<EveId, PNVisualPartDefinition>();
    private readonly Dictionary<EveId, PNVisualBuildDefinition> visualBuilds = new Dictionary<EveId, PNVisualBuildDefinition>();
    private readonly Dictionary<EveId, PNVisualMotionDefinition> visualMotions = new Dictionary<EveId, PNVisualMotionDefinition>();
    private readonly Dictionary<EveId, PNVisualMotionSetDefinition> visualMotionSets = new Dictionary<EveId, PNVisualMotionSetDefinition>();
    private readonly Dictionary<EveId, PNVisualExpressionDefinition> visualExpressions = new Dictionary<EveId, PNVisualExpressionDefinition>();
    private readonly Dictionary<EveId, PNVisualAppearanceDefinition> visualAppearances = new Dictionary<EveId, PNVisualAppearanceDefinition>();

    private IEnumerable<EveIIdentified> VisualDefinitions => visualRigs.Values.Cast<EveIIdentified>().Concat(visualParts.Values).Concat(visualBuilds.Values).Concat(visualMotions.Values)
        .Concat(visualMotionSets.Values).Concat(visualExpressions.Values).Concat(visualAppearances.Values).Concat(VisualStringDefinitions);

    private void CommitVisual()
    {
        foreach (var d in visualRigs.Values) hub.Get<PNVisualRigDefinition>().Register(d);
        foreach (var d in visualParts.Values) hub.Get<PNVisualPartDefinition>().Register(d);
        foreach (var d in visualBuilds.Values) hub.Get<PNVisualBuildDefinition>().Register(d);
        foreach (var d in visualMotions.Values) hub.Get<PNVisualMotionDefinition>().Register(d);
        foreach (var d in visualMotionSets.Values) hub.Get<PNVisualMotionSetDefinition>().Register(d);
        foreach (var d in visualExpressions.Values) hub.Get<PNVisualExpressionDefinition>().Register(d);
        foreach (var d in visualAppearances.Values) hub.Get<PNVisualAppearanceDefinition>().Register(d);
        CommitVisualStrings();
    }

    private void ParseVisual(JObject root, EveContentExtensionDocument document)
    {
        foreach (var token in Array(root["visualRigs"])) AddVisual(visualRigs, ParseRig(Object(token), document), hub.Get<PNVisualRigDefinition>());
        foreach (var token in Array(root["visualParts"])) AddVisual(visualParts, ParsePart(Object(token), document), hub.Get<PNVisualPartDefinition>());
        foreach (var token in Array(root["visualBuilds"])) AddVisual(visualBuilds, ParseBuild(Object(token), document), hub.Get<PNVisualBuildDefinition>());
        foreach (var token in Array(root["visualMotions"])) AddVisual(visualMotions, ParseMotion(Object(token), document), hub.Get<PNVisualMotionDefinition>());
        foreach (var token in Array(root["visualMotionSets"])) AddVisual(visualMotionSets, ParseMotionSet(Object(token), document), hub.Get<PNVisualMotionSetDefinition>());
        foreach (var token in Array(root["visualExpressions"])) AddVisual(visualExpressions, ParseExpression(Object(token), document), hub.Get<PNVisualExpressionDefinition>());
        ParseVisualStrings(root, document);
        foreach (var token in Array(root["visualAppearances"])) AddVisual(visualAppearances, ParseAppearance(Object(token), document), hub.Get<PNVisualAppearanceDefinition>());
    }

    private static void AddVisual<T>(Dictionary<EveId, T> pending, T definition, EveRegistry<T> registry) where T : class, EveIIdentified
    {
        if (pending.ContainsKey(definition.Id) || registry.TryGet(definition.Id, out _)) throw new FormatException("Duplicate visual definition " + definition.Id);
        if (pending.Count >= 1024) throw new FormatException("Too many visual definitions in one document.");
        pending.Add(definition.Id, definition);
    }

    // ---- lookups across the pending batch and the registries ----
    private T FindVisual<T>(Dictionary<EveId, T> pending, EveId id) where T : class, EveIIdentified
        => pending.TryGetValue(id, out var found) ? found : hub.Get<T>().TryGet(id, out var registered) ? registered : null;
    private PNVisualRigDefinition FindRig(EveId id) => FindVisual(visualRigs, id);

    private PNVisualResolvedRig RigOf(EveId id, EveContentExtensionDocument document, string what)
    {
        Reference(id, document, hub.Get<PNVisualRigDefinition>());
        var rig = FindRig(id) ?? throw new FormatException(what + " uses unknown rig " + id);
        return rig.Resolve(FindRig);
    }
    private T Require<T>(Dictionary<EveId, T> pending, EveId id, EveContentExtensionDocument document, string what) where T : class, EveIIdentified
    {
        Reference(id, document, hub.Get<T>());
        return FindVisual(pending, id) ?? throw new FormatException(what + " uses unknown " + typeof(T).Name.Replace("PNVisual", "").Replace("Definition", "").ToLowerInvariant() + " " + id);
    }

    // ---- small readers ----
    private static float Float(JObject obj, string field) => (float)Number(obj, field);
    private static float Float(JObject obj, string field, float fallback) => obj[field] == null ? fallback : Float(obj, field);
    private static bool Flag(JObject obj, string field, bool fallback) => obj[field] == null ? fallback : Bool(obj, field);
    private static float Num(JToken token, string what)
    {
        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new FormatException("Number required: " + what);
        double value = token.Value<double>(); if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Finite number required: " + what); return (float)value;
    }
    private static float[] Pair(JToken token, string what)
    {
        if (!(token is JArray array) || array.Count != 2) throw new FormatException(what + " must be [x, y].");
        return new[] { Num(array[0], what), Num(array[1], what) };
    }
    private static int Integer(JObject obj, string field, int fallback) => obj[field] == null ? fallback : Integer(obj, field);
    private static TEnum EnumOf<TEnum>(JObject obj, string field, TEnum fallback) where TEnum : struct
    {
        if (obj[field] == null) return fallback;
        string text = String(obj, field);
        foreach (TEnum value in Enum.GetValues(typeof(TEnum))) if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase)) return value;
        throw new FormatException(field + " has an unknown value: " + text);
    }
    private static IEnumerable<string> Names(JToken token) => Array(token).Select(x => x.Type == JTokenType.String ? x.Value<string>() : throw new FormatException("String required."));

    // ---- parsers ----
    private PNVisualRigDefinition ParseRig(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "pixelsPerMeter", "facing", "capsule", "extends", "bones", "slots", "sockets");
        var id = Owned(String(obj, "id"), document);
        if (obj["facing"] != null && String(obj, "facing") != "right") throw new FormatException("rig facing must be right: every asset faces right and left is a mirror.");
        var capsule = Object(obj["capsule"]); Fields(capsule, "height", "radius");
        EveId? extends = null;
        if (obj["extends"] != null && obj["extends"].Type != JTokenType.Null) { extends = EveId.Parse(String(obj, "extends")); Reference(extends.Value, document, hub.Get<PNVisualRigDefinition>()); }
        var bones = Array(obj["bones"]).Select(token =>
        {
            var b = Object(token); Fields(b, "id", "parent", "at", "scale", "scaleGroup"); var at = Pair(b["at"], "bone at");
            return new PNVisualBone(String(b, "id"), OptionalString(b, "parent"), at[0], at[1], Float(b, "scale", 1f), OptionalString(b, "scaleGroup"));
        }).ToArray();
        var slots = Array(obj["slots"]).Select(token =>
        {
            var s = Object(token); Fields(s, "id", "bone", "layer", "face", "blink", "at"); var at = s["at"] == null ? new float[2] : Pair(s["at"], "slot at");
            return new PNVisualSlot(String(s, "id"), String(s, "bone"), Integer(s, "layer"), Flag(s, "face", false), Flag(s, "blink", false), at[0], at[1]);
        }).ToArray();
        var sockets = Array(obj["sockets"]).Select(token =>
        {
            var s = Object(token); Fields(s, "id", "bone", "at"); var at = Pair(s["at"], "socket at");
            return new PNVisualSocket(String(s, "id"), String(s, "bone"), at[0], at[1]);
        }).ToArray();
        var rig = new PNVisualRigDefinition(id, Float(obj, "pixelsPerMeter"), Float(capsule, "height"), Float(capsule, "radius"), extends, bones, slots, sockets);
        checks.Add(() => rig.Resolve(FindRig));
        return rig;
    }

    private PNVisualPartDefinition ParsePart(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "image", "pivot", "tint", "variants", "segments", "axis");
        var id = Owned(String(obj, "id"), document); var pivot = Pair(obj["pivot"], "part pivot");
        var variants = new Dictionary<string, string>();
        if (obj["variants"] != null) foreach (var p in Object(obj["variants"]).Properties()) variants.Add(p.Name, p.Value.Type == JTokenType.String ? p.Value.Value<string>() : throw new FormatException("variant image path required."));
        return new PNVisualPartDefinition(id, String(obj, "image"), pivot[0], pivot[1], EnumOf(obj, "tint", PNVisualTint.None), variants, Integer(obj, "segments", 1), EnumOf(obj, "axis", PNVisualBendAxis.Y));
    }

    private PNVisualBuildDefinition ParseBuild(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "scaleGroups", "capsule", "shadow");
        var id = Owned(String(obj, "id"), document); var capsule = Object(obj["capsule"]); Fields(capsule, "height", "radius");
        var groups = new Dictionary<string, float[]>();
        if (obj["scaleGroups"] != null) foreach (var p in Object(obj["scaleGroups"]).Properties()) groups.Add(p.Name, Pair(p.Value, "scale group"));
        return new PNVisualBuildDefinition(id, groups, Float(capsule, "height"), Float(capsule, "radius"), Float(obj, "shadow", 1f));
    }

    private static PNVisualCurve ParseCurve(JToken token, string what)
    {
        var obj = Object(token);
        if (obj["wave"] != null)
        {
            Fields(obj, "wave"); var w = Object(obj["wave"]); Fields(w, "amp", "freq", "phase", "offset", "abs");
            return new PNVisualCurve(Float(w, "amp"), Float(w, "freq"), Float(w, "phase", 0f), Float(w, "offset", 0f), Flag(w, "abs", false));
        }
        Fields(obj, "keys", "ease");
        var keys = Array(obj["keys"]).ToArray(); var times = new float[keys.Length]; var values = new float[keys.Length]; bool current = false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (!(keys[i] is JArray pair) || pair.Count != 2) throw new FormatException(what + " keys must be [time, value].");
            times[i] = Num(pair[0], what + " key time");
            if (pair[1].Type == JTokenType.String)
            {
                if (pair[1].Value<string>() != "current" || i != 0) throw new FormatException(what + ": only the first key can be \"current\".");
                current = true; values[i] = 0f;
            }
            else values[i] = Num(pair[1], what + " key value");
        }
        return new PNVisualCurve(times, values, EnumOf(obj, "ease", PNVisualEase.Linear), current);
    }

    private PNVisualMotionDefinition ParseMotion(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "rig", "kind", "clock", "duration", "loop", "blendIn", "blendOut", "channel", "mask", "noModifiers", "speedScale", "tracks", "slotTracks", "events", "frames", "fps");
        var id = Owned(String(obj, "id"), document); var rigId = EveId.Parse(String(obj, "rig")); Reference(rigId, document, hub.Get<PNVisualRigDefinition>());
        var channel = EnumOf(obj, "channel", PNVisualChannel.Base);
        var tracks = new List<PNVisualTrack>();
        if (obj["tracks"] != null)
            foreach (var bone in Object(obj["tracks"]).Properties())
            {
                var set = Object(bone.Value); Fields(set, "rot", "x", "y", "sx", "sy", "bend", "sway");
                foreach (var kind in new[] { PNVisualTrackKind.Rot, PNVisualTrackKind.X, PNVisualTrackKind.Y, PNVisualTrackKind.Sx, PNVisualTrackKind.Sy, PNVisualTrackKind.Bend, PNVisualTrackKind.Sway })
                {
                    string field = kind.ToString().ToLowerInvariant();
                    if (set[field] != null) tracks.Add(new PNVisualTrack(bone.Name, kind, ParseCurve(set[field], id + " " + bone.Name + "." + field)));
                }
            }
        var slotTracks = new List<PNVisualSlotTrack>();
        if (obj["slotTracks"] != null)
            foreach (var slot in Object(obj["slotTracks"]).Properties())
            {
                var s = Object(slot.Value); Fields(s, "variants", "alpha", "sy");
                var times = new List<float>(); var names = new List<string>();
                foreach (var pair in Array(s["variants"])) { if (!(pair is JArray a) || a.Count != 2 || a[1].Type != JTokenType.String) throw new FormatException("slot track variants must be [time, \"name\"]."); times.Add(Num(a[0], "slot track time")); names.Add(a[1].Value<string>()); }
                slotTracks.Add(new PNVisualSlotTrack(slot.Name, times.ToArray(), names.ToArray(), s["alpha"] == null ? null : ParseCurve(s["alpha"], id + " " + slot.Name + ".alpha"), s["sy"] == null ? null : ParseCurve(s["sy"], id + " " + slot.Name + ".sy")));
            }
        var events = Array(obj["events"]).Select(token => { var e = Object(token); Fields(e, "at", "name"); return new PNVisualEvent(Float(e, "at"), String(e, "name")); }).ToArray();
        var motion = new PNVisualMotionDefinition(id, rigId, EnumOf(obj, "kind", PNVisualMotionKind.Pose), EnumOf(obj, "clock", PNVisualClock.Time), Float(obj, "duration"), Flag(obj, "loop", false),
            Float(obj, "blendIn", .1f), Float(obj, "blendOut", channel == PNVisualChannel.Overlay ? .1f : 0f), channel, Flag(obj, "noModifiers", false), Float(obj, "speedScale", 1f),
            Names(obj["mask"]), tracks, slotTracks, events, Names(obj["frames"]), Float(obj, "fps", 0f));
        checks.Add(() =>
        {
            var rig = RigOf(rigId, document, "motion " + id);
            foreach (var track in motion.Tracks) if (rig.BoneIndex(track.Bone) < 0) throw new FormatException("motion " + id + " track uses unknown bone " + track.Bone);
            foreach (var bone in motion.Mask) if (rig.BoneIndex(bone) < 0) throw new FormatException("motion " + id + " mask uses unknown bone " + bone);
            foreach (var st in motion.SlotTracks) if (rig.SlotIndex(st.Slot) < 0) throw new FormatException("motion " + id + " slot track uses unknown slot " + st.Slot);
        });
        return motion;
    }

    private PNVisualMotionSetDefinition ParseMotionSet(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "rig", "states", "modifiers");
        var id = Owned(String(obj, "id"), document); var rigId = EveId.Parse(String(obj, "rig")); Reference(rigId, document, hub.Get<PNVisualRigDefinition>());
        var states = new Dictionary<string, EveId>();
        foreach (var p in Object(obj["states"]).Properties()) states.Add(p.Name, p.Value.Type == JTokenType.String ? EveId.Parse(p.Value.Value<string>()) : throw new FormatException("state needs a motion id."));
        string bone = ""; float amp = 0f, period = 3f, every = 0f, duration = .18f;
        if (obj["modifiers"] != null)
        {
            var m = Object(obj["modifiers"]); Fields(m, "breathe", "blink");
            if (m["breathe"] != null && m["breathe"].Type != JTokenType.Null) { var b = Object(m["breathe"]); Fields(b, "bone", "amp", "period"); bone = String(b, "bone"); amp = Float(b, "amp"); period = Float(b, "period"); }
            if (m["blink"] != null && m["blink"].Type != JTokenType.Null) { var b = Object(m["blink"]); Fields(b, "every", "duration"); every = Float(b, "every"); duration = Float(b, "duration"); }
        }
        var set = new PNVisualMotionSetDefinition(id, rigId, states, bone, amp, period, every, duration);
        checks.Add(() =>
        {
            var rig = RigOf(rigId, document, "motion set " + id);
            if (set.BreatheBone.Length > 0 && rig.BoneIndex(set.BreatheBone) < 0) throw new FormatException("motion set " + id + " breathes unknown bone " + set.BreatheBone);
            foreach (var pair in set.States)
            {
                var motion = Require(visualMotions, pair.Value, document, "motion set " + id + " state " + pair.Key);
                if (motion.Rig != rigId) throw new FormatException("motion set " + id + " state " + pair.Key + " uses a motion of another rig.");
            }
        });
        return set;
    }

    private PNVisualExpressionDefinition ParseExpression(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "rig", "slots", "blink");
        var id = Owned(String(obj, "id"), document); var rigId = EveId.Parse(String(obj, "rig")); Reference(rigId, document, hub.Get<PNVisualRigDefinition>());
        var slots = new List<PNVisualFaceSlot>();
        if (obj["slots"] != null)
            foreach (var p in Object(obj["slots"]).Properties())
            {
                var s = Object(p.Value); Fields(s, "variant", "sx", "sy", "ox", "oy", "rot");
                slots.Add(new PNVisualFaceSlot(p.Name, OptionalString(s, "variant"), Float(s, "sx", 1f), Float(s, "sy", 1f), Float(s, "ox", 0f), Float(s, "oy", 0f), Float(s, "rot", 0f)));
            }
        var expression = new PNVisualExpressionDefinition(id, rigId, Flag(obj, "blink", true), slots);
        checks.Add(() =>
        {
            var rig = RigOf(rigId, document, "expression " + id);
            foreach (var slot in expression.Slots)
            {
                int index = rig.SlotIndex(slot.Slot);
                if (index < 0 || !rig.Slots[index].Face) throw new FormatException("expression " + id + " uses a slot that is not a face slot of the rig: " + slot.Slot);
            }
        });
        return expression;
    }

    private PNVisualAppearanceDefinition ParseAppearance(JObject obj, EveContentExtensionDocument document)
    {
        Fields(obj, "id", "rig", "motionSet", "build", "expression", "colors", "slots", "free", "hide", "blushAlpha", "sockets", "strings");
        var id = Owned(String(obj, "id"), document); var rigId = EveId.Parse(String(obj, "rig")); Reference(rigId, document, hub.Get<PNVisualRigDefinition>());
        var colors = new Dictionary<string, PNVisualColor>();
        if (obj["colors"] != null) foreach (var p in Object(obj["colors"]).Properties()) colors.Add(p.Name, PNVisualColor.Parse(p.Value.Type == JTokenType.String ? p.Value.Value<string>() : throw new FormatException("colour string required.")));
        var slots = new Dictionary<string, EveId>();
        if (obj["slots"] != null) foreach (var p in Object(obj["slots"]).Properties()) slots.Add(p.Name, p.Value.Type == JTokenType.String ? EveId.Parse(p.Value.Value<string>()) : throw new FormatException("slot part id required."));
        var free = Array(obj["free"]).Select(token => { var f = Object(token); Fields(f, "part", "bone", "at", "layer"); var at = Pair(f["at"], "free part at"); return new PNVisualFreePart(EveId.Parse(String(f, "part")), String(f, "bone"), at[0], at[1], Integer(f, "layer")); }).ToArray();
        var sockets = new Dictionary<string, float[]>();
        if (obj["sockets"] != null) foreach (var p in Object(obj["sockets"]).Properties()) sockets.Add(p.Name, Pair(p.Value, "socket"));
        var strings = Array(obj["strings"]).Select(t => ParseStringEntry(t, document)).ToArray();
        EveId expression = default;
        if (obj["expression"] != null && obj["expression"].Type != JTokenType.Null) expression = EveId.Parse(String(obj, "expression"));
        var appearance = new PNVisualAppearanceDefinition(id, rigId, EveId.Parse(String(obj, "motionSet")), EveId.Parse(String(obj, "build")), expression, colors, slots, free, Names(obj["hide"]), Float(obj, "blushAlpha", 1f), sockets, strings);
        checks.Add(() =>
        {
            var rig = RigOf(rigId, document, "appearance " + id);
            var set = Require(visualMotionSets, appearance.MotionSet, document, "appearance " + id); if (set.Rig != rigId) throw new FormatException("appearance " + id + " uses a motion set of another rig.");
            Require(visualBuilds, appearance.Build, document, "appearance " + id);
            if (appearance.Expression.IsValid) { var e = Require(visualExpressions, appearance.Expression, document, "appearance " + id); if (e.Rig != rigId) throw new FormatException("appearance " + id + " uses an expression of another rig."); }
            foreach (var pair in appearance.Slots) { if (rig.SlotIndex(pair.Key) < 0) throw new FormatException("appearance " + id + " fills unknown slot " + pair.Key); Require(visualParts, pair.Value, document, "appearance " + id + " slot " + pair.Key); }
            foreach (var f in appearance.Free) { if (rig.BoneIndex(f.Bone) < 0) throw new FormatException("appearance " + id + " free part uses unknown bone " + f.Bone); Require(visualParts, f.Part, document, "appearance " + id + " free part"); }
            foreach (var slot in appearance.Hide) if (rig.SlotIndex(slot) < 0) throw new FormatException("appearance " + id + " hides unknown slot " + slot);
            foreach (var pair in appearance.SocketOverrides) if (rig.SocketIndex(pair.Key) < 0) throw new FormatException("appearance " + id + " overrides unknown socket " + pair.Key);
            CheckStringEntries(appearance, rig);
        });
        return appearance;
    }
}
