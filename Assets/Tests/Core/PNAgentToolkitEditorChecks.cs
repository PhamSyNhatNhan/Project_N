#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class PNAgentToolkitEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use idle Edit Mode.");
        var doc = new EveContentDocument("dummy", File.ReadAllText(Path.Combine(Application.streamingAssetsPath,
            "BaseContent/characters/DummyCharacter.json")), "project_n");
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { doc });
        int checks = 0;
        var curve = JObject.Parse(@"{'kind':'CurveXZ','duration':0.6,'distance':2,'lateralOffset':0.5,
            'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies',
            'maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64}");
        void Valid(JObject payload, bool contact)
        { PNAgentTrajectorySettings.Parse(payload, hub, contact); checks++; }
        void Invalid(Action<JObject> mutate, bool contact = false, JObject seed = null)
        {
            var payload = (JObject)(seed ?? curve).DeepClone(); mutate(payload);
            bool rejected = false;
            try { PNAgentTrajectorySettings.Parse(payload, hub, contact); }
            catch (Exception) { rejected = true; }
            if (!rejected) throw new Exception("Malformed trajectory payload was accepted: " + payload);
            checks++;
        }
        Valid(curve, false);
        foreach (var name in new[] { "kind", "duration", "distance", "lateralOffset", "permissionId", "actorCollision", "maxSegmentLength", "maxChordError", "maxSubsteps" })
        {
            Invalid(payload => payload.Remove(name));
            Invalid(payload => payload[name] = JValue.CreateNull());
        }
        Invalid(payload => payload["unknown"] = 1);
        Invalid(payload => payload["duration"] = "0.6");
        Invalid(payload => payload["duration"] = -1);
        Invalid(payload => payload["maxSubsteps"] = 2.5);
        Invalid(payload => payload["maxSubsteps"] = 129);
        Invalid(payload => payload["kind"] = "0");
        Invalid(payload => payload["actorCollision"] = "1");
        Invalid(payload => payload["height"] = 1);
        Invalid(payload => payload["permissionId"] = "missing:permission");
        var hop = (JObject)curve.DeepClone(); hop["kind"] = "Hop"; hop.Remove("lateralOffset"); hop["height"] = .6;
        Valid(hop, false);
        Invalid(payload => payload["height"] = 6, seed: hop);
        var orbit = (JObject)curve.DeepClone(); orbit["kind"] = "OrbitSegment"; orbit.Remove("distance"); orbit.Remove("lateralOffset"); orbit["radius"] = 1.5; orbit["sweepDegrees"] = 90;
        Valid(orbit, false);
        Invalid(payload => payload["sweepDegrees"] = 0, seed: orbit);
        var contact = (JObject)curve.DeepClone(); contact["hitRadius"] = .4; contact["activeStart"] = 0; contact["activeEnd"] = 1;
        contact["repeatInterval"] = 0; contact["canCrit"] = false; contact["value"] = JObject.Parse("{'amount':10}");
        Valid(contact, true);
        foreach (var name in new[] { "hitRadius", "activeStart", "activeEnd", "repeatInterval", "canCrit", "value" })
            Invalid(payload => payload.Remove(name), true, contact);
        Invalid(payload => { payload["activeStart"] = .8; payload["activeEnd"] = .2; }, true, contact);
        Invalid(payload => payload["hitRadius"] = 0, true, contact);
        Invalid(payload => payload["canCrit"] = "false", true, contact);
        return "PASS " + checks + " native toolkit schema checks; registered content bootstrap and strict trajectory/contact contracts.";
    }
}
#endif
