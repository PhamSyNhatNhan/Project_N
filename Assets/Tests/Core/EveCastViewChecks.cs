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
using ProjectN.Coop;

// Docs/SkillNetworkingAPlan.md S6, Edit Mode, no Mirror: the authority's Eve raises a picture for each slash it starts, the block `project_n:skill_cast` carries the last few to every client, and an
// observer's Eve plays them as pictures only (a slash arc, a motion trail, Burst on / off): no damage, no report, no Ability written; a replayed block plays nothing twice; the first block only sets the serial.
public static class EveCastViewChecks
{
    public static string Run() => Run(null);
    public static string Run(string captureFolder)
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Cast view check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995060; var target = PNSyncTarget.ForEntity(new EveEntityId(eveId), 1);
        var def = hub.SyncStates().Get(PNSkillCastView.State);
        Check(def != null && !def.OwnerOnly, "the cast block is registered and goes to every client");
        Check(hub.EntityModules().Get(EveId.Parse("project_n:skill_owner_player")) != null, "the player module that carries it is registered");

        GameObject Root(Scene preview, string name, long id, string character, Vector3 position, long ownerId)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), ownerId); return obj;
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
        JObject Input(long key, EveId slot, PNSkillInputPhase phase, EveId profile) => new JObject { ["k"] = key, ["s"] = slot.ToString(), ["p"] = (int)phase, ["i"] = 0, ["f"] = profile.ToString() };

        // ---- the authority ----
        JToken hostBlock = null; var epoch = Guid.NewGuid(); double now = 1;
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s6_authority"));
            PNTimeService.Attach(EveRegistrySession.Context, new PNLocalTimeline(epoch, () => now));
            var areaRoot = new GameObject("S6 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root(hostScene, "S6 host Eve", eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms(); actor.GetComponent<PNAbilityManager>().Abilities.Advance(0);
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context);
            var ownerView = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>(); var view = actor.AddComponent<PNSkillCastView>();
            service.RegisterObject(target, skill); ownerView.OnBound(service, target); remote.OnBound(service, target); view.OnBound(service, target);
            var scaleModifier = EveId.Parse("test:o1_report_scale");
            var caster = actor.GetComponent<PNStat>();
            caster.SetSource(scaleModifier, new[] { new EveStatModifier(PNContentIds.MeleeScale, 1d, 1) });
            skill.Bindings.TryGet(PNSkillSlots.Attack, out var attackProfile);
            Check(((JArray)view.Capture()["e"]).Count == 0, "before any cast the authority has nothing to show");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(1, PNSkillSlots.Attack, PNSkillInputPhase.Started, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(2, PNSkillSlots.Attack, PNSkillInputPhase.Performed, attackProfile));
            Check(skill.CurrentCast == EveCast.Attack, "the owner's press starts a slash on the authority");
            Check(service.Flush() >= 1 && service.TryGetLatest(target, PNSkillCastView.State, out _, out hostBlock), "the authority publishes the cast block");
            var events = PNSkillCastView.Parse(hostBlock, out long latest);
            Check(events.Count == 1 && events[0].Event.Kind == PNSkill.ActionKind && events[0].Event.Detail.StartsWith("project_n:eve_attack_") && events[0].Event.X > .9f && latest == events[0].Serial,
                "the block holds one Action start with its skill id and direction (" + string.Join(",", events.Select(e => e.Event.Kind + ":" + e.Event.Detail)) + ")");
            Check(hostBlock.ToString(Newtonsoft.Json.Formatting.None).Length < PNSkillCastView.StateBytes, "the block fits its byte bound");
            var runs = PNSkillCastView.ParseRuns(hostBlock, out var envelope);
            Check(runs.Count == 1 && runs[0].Key.Ordinal == 1 && runs[0].Key.Cast == skill.CurrentInvocation.CastKey, "eager Action identity before any hit");
            var runKey = runs[0].Key;
            caster.RemoveSource(scaleModifier);
            var hitOrigin = actor.transform.position + skill.WeaponRig.HandOffset(Vector3.right);
            var reportTarget = Root(hostScene, "O1 report target", 995061, "project_n:dummy", hitOrigin + Vector3.right * 3, 0);
            var reportStat = reportTarget.AddComponent<PNEnemyStat>();
            reportTarget.layer = PNLayers.Enemy;
            reportTarget.AddComponent<EveEntityLoader>().Load(hub);
            reportTarget.SetActive(true);
            var health = reportStat.GetCurrent(PNContentIds.Health);
            Check(skill.HitReceiverCount == 1 && !skill.DeliverReportedHit(reportStat, 0, 0),
                "live sweep refuses a report outside its captured radius");
            reportTarget.transform.position = hitOrigin + Vector3.right * 1.8f;
            Check(skill.DeliverReportedHit(reportStat, 0, 0) && reportStat.GetCurrent(PNContentIds.Health) < health && !reportStat.IsDead,
                "a report outside base reach applies authority damage at the captured scale");
            var afterReport = reportStat.GetCurrent(PNContentIds.Health);
            Check(!skill.DeliverReportedHit(reportStat, 0, 0) && reportStat.GetCurrent(PNContentIds.Health) == afterReport,
                "the report cannot damage the same live target twice");
            var extra = new PNSkillActionContext(skill, EveId.Parse(runs[0].Data), skill.GetData(EveId.Parse(runs[0].Data)), hub, null, Vector3.right, skill.CurrentInvocation);
            extra.BeginAction(); Check(extra.ActionOrdinal == 2, "same cast same definition has separate eager ordinal");
            extra.CancelEffects();
            var cancelled = new PNSkillActionContext(skill, EveId.Parse(runs[0].Data), skill.GetData(EveId.Parse(runs[0].Data)), hub, null, Vector3.right, skill.CurrentInvocation);
            cancelled.CancelEffects(); cancelled.BeginAction(); Check(cancelled.ActionOrdinal == 0, "cancel before start cannot resurrect");
            typeof(PNSkillInvocation).GetField("actionOrdinal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(skill.CurrentInvocation, long.MaxValue);
            var exhausted = new PNSkillActionContext(skill, EveId.Parse(runs[0].Data), skill.GetData(EveId.Parse(runs[0].Data)), hub, null, Vector3.right, skill.CurrentInvocation);
            exhausted.BeginAction(); exhausted.CancelEffects(); Check(exhausted.ActionOrdinal == 0 && skill.ActionIdentityRejected == 1, "ordinal exhaustion rejects only presentation");
            view.OnUnbound(); view.OnBound(service, target);
            Check(PNSkillCastView.ParseRuns(view.Capture(), out _).Single().Key.Equals(runKey), "same life rebind keeps producer key");

            for (int i = 0; i < 300 && skill.IsBusy; i++) { now += .01; skill.Tick(.01f); }
            Check(skill.HitReceiverCount == 0, "completion releases the hit report receiver");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(3, PNSkillSlots.Attack, PNSkillInputPhase.Canceled, attackProfile));
            Check(service.TryGetLatest(target, PNSkillCastView.State, out _, out hostBlock) && view.PlayedCount == 0, "the authority never plays its own pictures");
            ownerView.OnUnbound(); remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- an observer: another player's Eve on this machine ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s6_observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            PNTimeService.Attach(EveRegistrySession.Context, new PNLocalTimeline(epoch, () => now));
            var areaRoot = new GameObject("S6 observer area manager"); SceneManager.MoveGameObjectToScene(areaRoot, clientScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root(clientScene, "S6 observed Eve", eveId, "project_n:eve", Vector3.zero, owner + 1); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var enemyObject = Root(clientScene, "S6 enemy", 995061, "project_n:dummy", new Vector3(.45f, .4f, 0), 0); var enemy = enemyObject.AddComponent<PNEnemyStat>(); enemyObject.layer = PNLayers.Enemy;
            enemyObject.AddComponent<SphereCollider>().radius = .15f; enemyObject.AddComponent<EveEntityLoader>().Load(hub); enemyObject.SetActive(true); Physics.SyncTransforms();
            var abilities = actor.GetComponent<PNAbilityManager>().Abilities;
            var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context);
            var view = actor.AddComponent<PNSkillCastView>(); service.RegisterObject(target, skill); view.OnBound(service, target);
            double hp = enemy.GetCurrent(PNContentIds.Health); string before = string.Join(";", abilities.CaptureReplica().Select(i => i.Id + ":" + i.Amount + ":" + i.Active));
            ulong rev = 1; bool Send(JToken block) => service.ReceiveState(target, PNSkillCastView.State, 2, rev++, block.DeepClone());
            JObject Block(long latest, params object[][] rows) { var block = (JObject)hostBlock.DeepClone(); block["n"] = latest; block["e"] = new JArray(rows.Select(r => (object)new JArray(r))); return block; }
            Check(!skill.IsOwnerClient, "an observed Eve is not on its owner's machine");
            var stale = Block(100); stale["a"]["g"] = target.Generation + 1;
            Check(Send(stale) && view.LastSerial == -1 && view.ReceivedRuns.Count == 0, "old generation payload cannot affect observer");
            stale = Block(100); stale["a"]["e"] = Guid.NewGuid().ToString("N");
            Check(Send(stale) && view.LastSerial == -1, "other epoch payload cannot affect observer");


            Check(Send(Block(7)) && view.PlayedCount == 0 && view.LastSerial == 7, "the first block only sets the serial: a late joiner replays nothing");
            Check(Send(Block(7, new object[] { 7L, "action", "base:eve_attack_right", 1f, 0f, 0f })) && view.PlayedCount == 0, "an event the first block already counted is not played");
            Check(Send(hostBlock) && view.PlayedCount == 0, "the authority's own block, with a serial this client already passed, plays nothing");

            Check(Send(Block(9, new object[] { 8L, "action", "base:eve_attack_right", 1f, 0f, 0f }, new object[] { 9L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && view.PlayedCount == 2, "two new events are played in order (" + view.PlayedCount + ")");
            Check(skill.ObservedActionCount == 2 && skill.ObservedActionsPlayed == 2, "both slashes run as the same Action on the observed Eve (running " + skill.ObservedActionCount + ", played " + skill.ObservedActionsPlayed + ")");
            Check(Send(Block(9, new object[] { 8L, "action", "base:eve_attack_right", 1f, 0f, 0f }, new object[] { 9L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && view.PlayedCount == 2, "the same block again plays nothing twice");
            for (int i = 0; i < 300 && skill.ObservedActionCount > 0; i++) skill.AdvanceObservedActions(.01f);
            Check(skill.ObservedActionCount == 0, "both end by themselves when their time is up");
            Check(enemy.GetCurrent(PNContentIds.Health) == hp, "the observed slash hits nothing");
            Check(!skill.IsBusy && skill.CurrentInvocation == null, "no cast runs on the observer");

            bool burstApplied = skill.Presentation.Apply(new System.Collections.Generic.Dictionary<string, string> { [EveCharacterSkill.BurstLook.Path] = EveCharacterSkill.BurstLook.Namespace });
            Check(burstApplied && skill.HasLook(EveCharacterSkill.BurstLook) && skill.WeaponRig.IsBurst, "Burst on is drawn from the picture (the registered look) applied=" + burstApplied + " has=" + skill.HasLook(EveCharacterSkill.BurstLook) + " rig=" + (skill.WeaponRig != null) + " burst=" + (skill.WeaponRig != null && skill.WeaponRig.IsBurst) + " ready=" + skill.IsReady + " fail=" + skill.LookFailures);
            Check(skill.Presentation.Apply(new System.Collections.Generic.Dictionary<string, string>()) && !skill.HasLook(EveCharacterSkill.BurstLook) && !skill.WeaponRig.IsBurst, "Burst off is drawn from the next picture");
            // the owner's machine draws its own guess and takes it off from the replica's state change, not from a picture: the look must leave the skill's list, or the next Burst is never drawn
            skill.SetLook(EveCharacterSkill.BurstLook, true);
            Check(skill.HasLook(EveCharacterSkill.BurstLook) && skill.WeaponRig.IsBurst, "a locally drawn Burst is on");
            var burstAbility = abilities.Find(EveCharacterSkill.BurstStateId)[0];
            var snapshotCtor = typeof(PNAbilitySnapshot).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            var changeCtor = typeof(PNAbilityChange).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            var snapshot = snapshotCtor.Invoke(new object[] { burstAbility });
            var change = changeCtor.Invoke(new object[] { PNAbilityChangeReason.Ended, snapshot, snapshot, null });
            typeof(EveCharacterSkill).GetMethod("OnAbilityChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(skill, new object[] { change });
            Check(!skill.HasLook(EveCharacterSkill.BurstLook) && !skill.WeaponRig.IsBurst, "the replica's Burst end takes the look off the list too, so the next Burst can be drawn");
            skill.SetLook(EveCharacterSkill.BurstLook, true);
            Check(skill.HasLook(EveCharacterSkill.BurstLook) && skill.WeaponRig.IsBurst, "and the next Burst is drawn again");
            skill.SetLook(EveCharacterSkill.BurstLook, false);
            Check(Send(Block(12, new object[] { 12L, "action", "base:eve_no_such_profile", 1f, 0f, 0f })) && skill.ObservedActionCount == 0 && skill.ObservedActionsPlayed == 2, "an unknown Action is dropped without a sound");
            Check(Send(Block(13, new object[] { 13L, "action", "base:eve_attack_right", 0f, 0f, 0f })) && skill.ObservedActionCount == 0 && skill.ObservedActionsPlayed == 2, "a slash without a direction draws nothing");
            Check(Send(Block(14, new object[] { 14L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && skill.ObservedActionCount == 1, "a new slash starts");
            Check(Send(Block(15, new object[] { 15L, "action", "base:eve_attack_right", 1f, 0f, 0f })) && skill.ObservedActionCount == 2 && skill.ObservedActionsPlayed == 4, "a second slash runs beside the first");
            Check(Send(Block(16, new object[] { 16L, "end", "base:eve_attack_right", 0f, 0f, 0f })) && skill.ObservedActionCount == 1, "the authority's end of an Action stops that one on the observer");
            skill.AdvanceObservedActions(.01f);
            Check(string.Join(";", abilities.CaptureReplica().Select(i => i.Id + ":" + i.Amount + ":" + i.Active)) == before, "no Ability was written by anything the observer drew");
            Check(wireIsEmpty(skill), "nothing was sent or reported (no sink on an observer)");
            if (captureFolder != null)
            {
                skill.SetObservedActionPresentation(true);
                int legacyInvocations=EveSweepAction.Invocations;
                Check(Send(Block(17,new object[]{17L,"action","base:eve_attack_right",1f,0f,0f}))&&
                    skill.ObservedActionCount==0,"cutover suppresses the legacy sweep");
                string dataId=hub.Skills.ResolveIdentity(EveId.Parse("base:eve_attack_right")).ToString();
                string actionId=PNGameplayContentModule.SweepAction.ToString();
                var settings=EveSweepSettings.Parse(skill.GetData(EveId.Parse(dataId)).Action.Perform);
                Check(skill.CanPresentObservedAction(dataId,actionId),"selected definition supplies the sweep presentation");
                var run=new PNActionRunKey(20,1);
                using var source=new PNActionPublication();
                var stream=new PNActionStream{Session=epoch,Source=source.Source,Entity=eveId,Generation=1,
                    NetId=12,Nonce=1,Subscription=1,Scope=new PNWorldBindingScope{World=epoch,Incarnation=1,Membership=1,Subscription=1}};
                var budget=new PNActionReceiveBudget();
                using var receiver=new PNActionReceiver(budget,stream,1,8);
                var frameRows=new List<PNObservedAction>();
                var trace=new JArray();
                Directory.CreateDirectory(captureFolder);
                void Drain(PNActionSender sender,double stamp)
                {
                    while(sender.DrainOne(stamp,_=>true,packet=>Check(receiver.Receive(packet,stamp),"apply sweep payload"))){}
                }
                PNActionPresentationCoverage Draw(double stamp)
                {
                    var status=receiver.ReadPresentation(stamp,frameRows);
                    foreach(var row in frameRows)
                        skill.PresentObservedAction(row.Key,row.DataId,Vector3.right,row.Progress,true);
                    skill.ReconcileObservedActionPresentation(status!=PNActionPresentationCoverage.Unknown,_=>null);
                    trace.Add(new JObject{["presentation"]=stamp,["coverage"]=status.ToString(),
                        ["cast"]=frameRows.Count==0?0:frameRows[0].Key.Cast,["ordinal"]=frameRows.Count==0?0:frameRows[0].Key.Ordinal,
                        ["progress"]=frameRows.Count==0?0:frameRows[0].Progress});
                    return status;
                }
                EveSweepVisual Visible()=>actor.GetComponentsInChildren<EveSweepVisual>().SingleOrDefault();
                source.Begin(run,dataId,dataId,actionId,1,0,1);
                source.Advance(run,1,1.1,settings.Duration*.36,settings.Duration*3.6);
                using(var sender=new PNActionSender(source,stream,1,1.1,8,budget))
                {
                    Drain(sender,1.1);
                    var ack=receiver.TryApply(1.1);
                    Check(ack.HasValue&&sender.Confirm(ack.Value,1.1),"late join baseline ACK");
                    Check(Draw(1.1)==PNActionPresentationCoverage.Active,"late join has active mid-sweep");
                    var first=Visible();
                    int firstStarts=skill.ObservedPresentationsStarted;
                    Check(first!=null&&Math.Abs(first.Progress-.36f)<.0001f&&first.BladeSamples>1&&first.LiveParticles==0,
                        "first frame renders current blade and recent ribbon without historical sparks");
                    CaptureSweep(clientScene,first,captureFolder,"late-join");
                    source.Advance(run,1.1,1.15,0,0);
                    sender.Capture(1.15);Drain(sender,1.15);
                    Check(Draw(1.15)==PNActionPresentationCoverage.Active&&Visible()==first&&Math.Abs(first.Progress-.36f)<.0001f,
                        "hold keeps progress and the same renderer");
                    CaptureSweep(clientScene,first,captureFolder,"before-recovery");
                    var other=new PNActionRunKey(20,2);
                    skill.PresentObservedAction(run,dataId,Vector3.right,settings.Duration*.36,false);
                    skill.PresentObservedAction(other,dataId,Vector3.forward,settings.Duration*.4,true);
                    skill.ReconcileObservedActionPresentation(true,_=>null);
                    var second=actor.GetComponentsInChildren<EveSweepVisual>().Single(v=>v!=first);
                    Check(Vector3.Distance(skill.WeaponRig.BladeTip,second.HeadPosition)<.001f,"one blade owner for overlapping run views");
                    Draw(1.15);
                    Check(Visible()==first&&Vector3.Distance(skill.WeaponRig.BladeTip,first.HeadPosition)<.001f,
                        "retiring the previous blade owner leaves its replacement visible");
                    stream.Subscription++;
                    receiver.Reset(stream,2,8);
                    Check(Draw(1.18)==PNActionPresentationCoverage.Unknown&&Visible()==null,"recovery hides on missing coverage");
                    source.Advance(run,1.15,1.2,settings.Duration*.16,settings.Duration*3.2);
                    using var recovery=new PNActionSender(source,stream,2,1.2,8,budget);
                    Drain(recovery,1.2);
                    ack=receiver.TryApply(1.2,1.15);
                    Check(ack.HasValue&&recovery.Confirm(ack.Value,1.2),"recovery baseline ACK");
                    Check(Draw(1.15)==PNActionPresentationCoverage.Active&&Visible()==first&&Math.Abs(first.Progress-.36f)<.0001f,
                        "committed baseline retains covered older pose before cut");
                    Check(Draw(1.2)==PNActionPresentationCoverage.Active&&Visible()==first&&Math.Abs(first.Progress-.52f)<.0001f&&
                        skill.ObservedPresentationsStarted==firstStarts+1,
                        "recovery at the cut shows the same sweep at resumed progress");
                    CaptureSweep(clientScene,first,captureFolder,"recovery-cut");
                    source.End(run,1.2,PNActionRunEnd.Cancelled);
                    stream.Subscription++;
                    receiver.Reset(stream,3,8);
                    using var ended=new PNActionSender(source,stream,3,1.21,8,budget);
                    Drain(ended,1.21);
                    ack=receiver.TryApply(1.21,1.2);
                    Check(ack.HasValue&&ended.Confirm(ack.Value,1.21),"ended-at-cut baseline applied");
                    Check(Draw(1.21)==PNActionPresentationCoverage.Empty&&Visible()==null,"ended-at-cut sweep never reappears");
                    source.Begin(other,dataId,dataId,actionId,1,0,1.22);
                    source.Advance(other,1.22,1.32,settings.Duration*.4,settings.Duration*4);
                    ended.Capture(1.32);Drain(ended,1.32);
                    source.End(other,1.32,PNActionRunEnd.Cancelled);
                    ended.Capture(1.33);Drain(ended,1.33);
                    Check(Draw(1.31)==PNActionPresentationCoverage.Active&&Visible()!=null,"received cancellation remains visible before terminal");
                    Check(Draw(1.32)==PNActionPresentationCoverage.Empty&&Visible()==null,"cancellation removes the sweep at terminal");
                    Check(EveSweepAction.Invocations==legacyInvocations&&enemy.GetCurrent(PNContentIds.Health)==hp&&
                        skill.CurrentInvocation==null&&wireIsEmpty(skill),"seek does not invoke gameplay, damage or reporting");
                    Check(string.Join(";",abilities.CaptureReplica().Select(i=>i.Id+":"+i.Amount+":"+i.Active))==before,
                        "seek does not mutate Ability");
                }
                File.WriteAllText(Path.Combine(captureFolder,"presentation.json"),trace.ToString());
            }

            foreach (var bad in new JToken[] {
                new JObject { ["n"] = 1 }, new JObject { ["e"] = new JArray() }, Block(1, new object[] { 1L, "sweep", "x", 0f }), Block(2, new object[] { 2L, "sweep", "x", 1f, 0f, 1f }, new object[] { 1L, "sweep", "x", 1f, 0f, 1f }),
                Block(1, new object[] { 1L, new string('a', 60), "x", 1f, 0f, 1f }), Block(1, new object[] { 1L, "sweep", "x", "a", 0f, 1f }),
                new JObject { ["n"] = 1, ["e"] = new JArray(Enumerable.Range(1, 13).Select(i => (object)new JArray((long)i, "k", "d", 0f, 0f, 0f))) } })
            {
                bool refused = false; try { PNSkillCastView.Parse(bad, out _); } catch (FormatException) { refused = true; }
                Check(refused, "a malformed block is refused: " + bad.ToString(Newtonsoft.Json.Formatting.None).Substring(0, Math.Min(60, bad.ToString(Newtonsoft.Json.Formatting.None).Length)));
            }
            view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " cast view checks (authority pictures, block, observer replay, no replay twice, no damage / Ability, malformed refused)";
    }

    private static bool wireIsEmpty(EveCharacterSkill skill) => skill.ReportSink == null && skill.InputForwarder == null;

    private static void CaptureSweep(Scene scene,EveSweepVisual sweep,string folder,string label)
    {
        var root=new GameObject("Sweep verification camera");
        SceneManager.MoveGameObjectToScene(root,scene);
        var camera=root.AddComponent<Camera>();
        camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=sweep.Radius*1.25f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.02f,.03f);
        camera.cullingMask=1<<sweep.gameObject.layer;
        var pivot=sweep.AttackRoot.position;
        root.transform.position=pivot+new Vector3(0,7,-4);
        root.transform.LookAt(pivot);
        var texture=new RenderTexture(480,480,24);
        var output=new Texture2D(480,480,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            texture.Create();
            camera.targetTexture=texture;
            camera.Render();
            RenderTexture.active=texture;
            output.ReadPixels(new Rect(0,0,480,480),0,0);
            output.Apply();
            if(output.GetPixels32().Count(p=>p.r>55&&p.r>p.b*1.2f)<20)
                throw new Exception("Expected rendered sweep pixels at "+label);
            File.WriteAllBytes(Path.Combine(folder,label+".png"),output.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
#endif
