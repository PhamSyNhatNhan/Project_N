#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/SkillNetworkingAPlan.md S1: the owner-only skill block `project_n:skill_owner` and the Ability replica, on a real Eve. Edit Mode, three sessions one after the other (a session has one role):
// 1) HOST session: Eve changes its Abilities, the authority view captures the block; 2) CLIENT session: another Eve receives that block through a non-authority sync service and must end up with the
// same values, tick on by itself, and stay unable to mutate Abilities the normal way; 3) the authority refuses a replica. The wire (who receives an owner-only block) is checked in the Player run.
public static class EveSkillOwnerStateChecks
{
    private sealed class Wire : IPNSyncTransport
    {
        public void SendState(PNSyncTarget t,PNSyncStateDefinition s,ulong r,JToken p) { }
        public void SendCommand(PNSyncTarget t,PNSyncCommandDefinition c,JToken p) { }
    }
    private sealed class Snap { public string Id; public int Index; public int? Amount; public bool Active; public double Cooldown; public double? Duration; }
    private static List<Snap> Read(PNAbilityCollection c)
    {
        var seen = new Dictionary<EveId, int>(); var list = new List<Snap>();
        foreach (var a in c.Instances) { seen.TryGetValue(a.Id, out int i); seen[a.Id] = i + 1; list.Add(new Snap { Id = a.Id.ToString(), Index = i, Amount = a.Amount, Active = a.IsActive, Cooldown = a.CooldownRemaining, Duration = a.DurationRemaining }); }
        return list;
    }
    private static string Compare(List<Snap> a, List<Snap> b)
    {
        if (a.Count != b.Count) return "count " + a.Count + " vs " + b.Count;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.Id != y.Id || x.Index != y.Index || x.Amount != y.Amount || x.Active != y.Active) return x.Id + "#" + x.Index + " amount " + x.Amount + "/" + y.Amount + " active " + x.Active + "/" + y.Active;
            if (Math.Abs(x.Cooldown - y.Cooldown) > 1e-6 || Math.Abs((x.Duration ?? -1) - (y.Duration ?? -1)) > 1e-6) return x.Id + " cooldown " + x.Cooldown + "/" + y.Cooldown + " duration " + x.Duration + "/" + y.Duration;
        }
        return null;
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill owner state check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        JToken payload = null; ulong revision = 0; List<Snap> hostAtCapture = null, hostLater = null; var hostBindings = new Dictionary<EveId, EveId>();
        var target = PNSyncTarget.ForEntity(new EveEntityId(995010), 1);
        var def = hub.SyncStates().Get(PNSkillOwnerView.State);
        Check(def.OwnerOnly && def.Targets == PNSyncTargets.Entity && def.MaxBytes == 4096, "the block is registered owner-only on Entity targets");
        Check(hub.EntityModules().Get(EveId.Parse("project_n:skill_owner_player")) != null, "its player entity module is registered");
        bool badRefused = false;
        try { new PNSyncStateDefinition(EveId.Parse("test:bad"), 1, 100, PNSyncTargets.Session, true); } catch (ArgumentException) { badRefused = true; }
        Check(badRefused, "owner-only on a Session target is refused");

        (GameObject root, EveClientRoleProbeSkill skill) Eve(Scene preview, string name)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.layer = PNLayers.Player;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995010), EveId.Parse("project_n:eve"), 1); obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>();
            var body = obj.AddComponent<Rigidbody>(); body.useGravity = false; var cap = obj.AddComponent<CapsuleCollider>(); cap.radius = .12f; cap.height = .6f;
            obj.AddComponent<PNPlayerMove>(); var s = obj.AddComponent<EveClientRoleProbeSkill>(); s.Prepare(); var l = obj.AddComponent<EveEntityLoader>(); l.Load(hub); obj.SetActive(true); return (obj, s);
        }
        void Cleanup(Scene preview)
        {
            try
            {
                foreach (var r in preview.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in preview.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }

        // ---- 1) host ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s1_host"));
            PNAbilityOwnerFixture.Attach();
            var (root, skill) = Eve(hostScene, "S1 host Eve"); var hostAbilities = root.GetComponent<PNAbilityManager>().Abilities;
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context); var view = root.AddComponent<PNSkillOwnerView>();
            service.RegisterObject(target, root); view.OnBound(service, target);
            skill.BurstGauge.Add(60); skill.DashRecovery.TryConsume(); skill.SkillRecovery.TryConsume(); skill.SlashStacks.Add(2);
            hostAbilities.Advance(.35);
            Check(service.Flush() >= 1 && service.TryGetLatest(target, PNSkillOwnerView.State, out revision, out payload), "the authority view publishes the block after the Abilities changed");
            int bytes = System.Text.Encoding.UTF8.GetByteCount(payload.ToString(Newtonsoft.Json.Formatting.None));
            Check(bytes < 4096 && bytes > 100, "the Eve block fits the 4096 byte bound (" + bytes + " bytes)");
            hostAtCapture = Read(hostAbilities);
            Check(hostAtCapture.Any(x => x.Id.EndsWith("eve_burst_gauge") && x.Amount == 60), "the host really holds gauge 60");
            Check(hostAtCapture.Any(x => x.Id.EndsWith("eve_skill_ready") && x.Amount == 0 && x.Cooldown > 0), "the host really has the Skill on cooldown");
            foreach (var slot in PNSkillSlots.Player) if (skill.Bindings.TryGet(slot, out var profile)) hostBindings[slot] = profile;
            Check(((JObject)payload["b"]).Count == hostBindings.Count && hostBindings.Count >= 4, "the block carries the slot -> profile view binding (" + hostBindings.Count + " slots)");
            hostAbilities.Advance(.5); hostLater = Read(hostAbilities);
            view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- 2) client ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s1_client"));
            PNAbilityOwnerFixture.Attach();
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(EveRegistrySession.Context,new object[]{1L});
            var (root, skill) = Eve(clientScene, "S1 client Eve"); var abilities = root.GetComponent<PNAbilityManager>().Abilities;
            var service = new PNSyncService(hub, false, 1, EveRegistrySession.Context) { Transport = new Wire() }; var view = root.AddComponent<PNSkillOwnerView>();
            service.RegisterObject(target, root); view.OnBound(service, target);
            Check(Compare(hostAtCapture, Read(abilities)) != null, "before the block arrives the client holds JSON defaults, different from the host");
            Check(service.ReceiveState(target, PNSkillOwnerView.State, 2, revision, payload.DeepClone()), "the client's non-authority service accepts the block");
            Check(view.AppliedCount == hostAtCapture.Count && view.AppliedRevision == revision, "every Ability row applied (" + view.AppliedCount + " of " + hostAtCapture.Count + ")");
            string diff = Compare(hostAtCapture, Read(abilities)); Check(diff == null, "client Abilities equal the host's at capture: " + diff);
            Check(view.Bindings.Count == hostBindings.Count && hostBindings.All(p => view.Bindings.TryGetValue(p.Key, out var v) && v == p.Value), "the client reads the same view binding");
            abilities.AdvanceReplica(.5); diff = Compare(hostLater, Read(abilities)); Check(diff == null, "the client ticks on by itself and lands where the host landed 0.5 s later: " + diff);
            bool refusedMutation = false; try { skill.BurstGauge.Add(1); } catch (InvalidOperationException) { refusedMutation = true; }
            Check(refusedMutation, "a client must not mutate Abilities the normal way");
            var bad = (JObject)payload.DeepClone(); ((JArray)bad["a"]).Add(new JArray("test:x")); bool refused = false;
            try { PNSkillOwnerView.Parse(bad, out _); } catch (FormatException) { refused = true; }
            Check(refused, "a malformed row is refused");
            var delayed=(JObject)payload.DeepClone(); delayed["t"]=99.5; delayed["q"]=new JArray(2,2,2);
            view.Apply(delayed,revision+1);
            double original=hostAtCapture.First(x=>x.Id.EndsWith("eve_skill_ready")).Cooldown;
            Check(Math.Abs(skill.SkillRecovery.CooldownRemaining-(original-1))<1e-6 && view.AppliedAge==.5,"T4 delayed owner image uses host age and authority rate, not buffered Presentation");
            typeof(PNAbilityManager).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(root.GetComponent<PNAbilityManager>(),null);
            Check(Math.Abs(skill.SkillRecovery.CooldownRemaining-(original-1))<1e-6,"T4 apply frame does not tick the same elapsed twice");
            PNAbilityOwnerFixture.Ready=false; view.Apply(payload.DeepClone(),revision+2);
            Check(view.HasPending && view.AppliedRevision==revision+1,"T4 unready image waits without replacing current image");
            PNAbilityOwnerFixture.Ready=true;
            typeof(PNSkillOwnerView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
            Check(!view.HasPending && view.AppliedRevision==revision+2,"T4 pending image applies after readiness");
            var expired=(JObject)payload.DeepClone(); expired["t"]=97; int requests=view.BaselineRequests;
            view.Apply(expired,revision+3);
            Check(!view.Prepared && view.BaselineRequests==requests+1 && view.AppliedRevision==revision+2,"T4 age beyond two seconds requests baseline instead of replay");
            var recoveryDeadline=typeof(PNSkillOwnerView).GetField("deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            double fixedDeadline=(double)recoveryDeadline.GetValue(view);PNAbilityOwnerFixture.Now=100.6;
            typeof(PNSkillOwnerView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
            Check((double)recoveryDeadline.GetValue(view)==fixedDeadline && view.BaselineRequests>requests+1,"T4 retry traffic cannot extend original recovery deadline");
            view.Apply(payload.DeepClone(),revision+4);
            Check(view.Prepared && !view.HasPending,"T4 fresh baseline recovers owner input view");
            var old=(JObject)payload.DeepClone(); old["e"]=Guid.NewGuid().ToString("N");view.Apply(old,revision+5);
            Check(view.AppliedRevision==revision+4,"T4 stale epoch cannot replace image");
            var history=PNTimeService.For(EveRegistrySession.Context).History;
            Check(history.AnchorCount==3,"T4 owns exactly three clock baselines");
            PNAbilityOwnerFixture.Now=103;
            typeof(PNSkillOwnerView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
            Check(!view.Prepared,"T4 silent owner image stream also enters baseline recovery");
            var fresh=(JObject)payload.DeepClone();fresh["t"]=103;view.Apply(fresh,revision+6);
            Check(view.Prepared,"T4 fresh capture recovers a silent image stream");
            view.Apply(expired,revision+7);
            typeof(PNSkillOwnerView).GetField("deadline",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(view,99d);
            typeof(PNSkillOwnerView).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);
            Check(EveRegistrySession.Context.ConnectionFailureCode=="ability_clock_timeout","T4 missing baseline expires through the actual session failure path");
            view.OnUnbound(); service.Dispose();
            Check(history.AnchorCount==0 && root.GetComponent<PNAbilityManager>().ReplicaCanAdvance==null && root.GetComponent<PNAbilityManager>().ReplicaAuthorityDelta==null,"T4 unbound clears history and driver delegates");
        }
        finally { Cleanup(clientScene); }

        // ---- 3) the authority never applies a replica ----
        var authorityScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s1_offline"));
            PNAbilityOwnerFixture.Attach();
            var (root, _) = Eve(authorityScene, "S1 offline Eve"); bool refused = false;
            try { root.GetComponent<PNAbilityManager>().Abilities.ApplyReplica(new PNAbilityReplicaItem[0]); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "ApplyReplica is refused on a machine with authority");
        }
        finally { Cleanup(authorityScene); }
        return "PASS " + n + " skill owner state checks (registration, block size, view binding, client replica equals host, local ticking, no normal mutation, authority never applies)";
    }
}
#endif
