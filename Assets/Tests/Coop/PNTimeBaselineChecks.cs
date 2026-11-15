using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // Opt-in diagnostics in the disposable skill Player. No production protocol/clock changes.
    public struct PNTimeProbeRequest : NetworkMessage { public double Sent; }
    public struct PNTimeProbeReply : NetworkMessage
    {
        public double Sent, HostReal, HostScaled, BuffGame, WorldGame, EntityScale, BurnRemaining;
    }
    public static class PNTimeBaselineChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-time-baseline");
        private static readonly FieldInfo Presentation = typeof(PNBuffManager).GetField("presentationTime", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Samples = typeof(EveEngine.Movement.MoveObserverModel).GetField("samples", BindingFlags.Instance | BindingFlags.NonPublic);
        public static IEnumerator Run(string folder, string peer, PNPlayerMove subject)
        {
            var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(Measure(folder,peer,subject));Exception failure=null;
            try
            {
                while(stack.Count>0)
                {
                    var current=stack.Peek();bool next=false;
                    try{next=current.MoveNext();}catch(Exception e){failure=e;}
                    if(failure!=null)break;
                    if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(current.Current is IEnumerator nested){stack.Push(nested);continue;}
                    yield return current.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
            File.WriteAllText(Path.Combine(folder, peer + ".txt"), failure == null ? "PASS T0 baseline captured" : "FAIL T0 " + failure);
            if (failure != null) Debug.LogException(failure);
            Application.Quit(failure == null ? 0 : 1);
        }
        // T0b observes private state without changing the production handshake or clock.
        private sealed class RecoveryDiagnostics : IDisposable
        {
            private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            private readonly PNNetworkManager manager;
            private readonly PNBuffManager buffs;
            private readonly StreamWriter log; // Required diagnostics: fail loudly if a private field is renamed.
            private readonly System.Collections.IDictionary handlers;
            private readonly System.Collections.Generic.Dictionary<ushort, object> saved = new System.Collections.Generic.Dictionary<ushort, object>();
            private string last;
            private double holdUntil=-1, resumeStarted=-1;
            private bool gateAck, injected, replayed, restarted;
            private bool changedDigest, secondResume;
            private PNResume rejectedAck;
            private readonly System.Collections.Generic.Queue<PNResume> held = new System.Collections.Generic.Queue<PNResume>();
            private static object Read(object o, string name) => o?.GetType().GetField(name, Flags)?.GetValue(o);
            private object Call(string name, params object[] args) => typeof(PNNetworkManager).GetMethod(name, Flags).Invoke(manager, args);
            public RecoveryDiagnostics(string folder, string peer, PNNetworkManager manager, PNBuffManager buffs)
            {
                this.manager=manager; this.buffs=buffs;
                gateAck=peer=="client2" && Environment.GetCommandLineArgs().Contains("--pn-time-gate-ack");
                log=new StreamWriter(Path.Combine(folder, peer+"-recovery.jsonl")) { AutoFlush=true };
                if (!NetworkServer.active)
                {
                    handlers=(System.Collections.IDictionary)typeof(NetworkClient).GetField("handlers", BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    Save<PNResume>(); Save<PNStateFence>(); Save<PNBuffPacket>();
                    NetworkClient.ReplaceHandler<PNResume>(p=> {
                        Event("resume-received", Newtonsoft.Json.JsonConvert.SerializeObject(p));
                        if(p.Acknowledged && Time.realtimeSinceStartupAsDouble<holdUntil) { if(held.Count>=64)throw new InvalidOperationException("T0b held resume bound"); held.Enqueue(p); Event("resume-held",p.Sequence.ToString()); return; }
                        if(gateAck && !injected && resumeStarted>=0 && p.Acknowledged && p.Sequence==(uint)Read(manager,"fenceAttempt"))
                        {
                            injected=true; rejectedAck=p;
                            var bindings=(System.Collections.IDictionary)Read(manager,"bindings");
                            var binding=bindings.Values.Cast<object>().First(b=>((PNActorShell)Read(b,"Shell")).HasBuff);
                            var records=Read(Read(binding,"Buffs"),"Records");
                            if((int)records.GetType().GetProperty("Count").GetValue(records)!=0)throw new Exception("fixture requires drained Buff queue before injecting pending record");
                            // Only the real Records.Count barrier is exercised, synchronously; no fake payload is decoded.
                            records.GetType().GetMethod("Enqueue").Invoke(records,new object[]{null});
                            try { Event("gate-final-ack-once",p.Sequence.ToString()); Call("ReceiveResumeClient",p);if(manager.ClientInputReady||Read(manager,"pendingFinalResume")==null)throw new Exception("pending Buff record did not retain final ACK"); }
                            finally { records.GetType().GetMethod("Dequeue").Invoke(records,null); }
                            Snapshot("gate-restored",true); return;
                        }
                        if(gateAck&&restarted&&!changedDigest&&p.Acknowledged&&p.Sequence==(uint)Read(manager,"fenceAttempt")&&(double)Read(manager,"clientInterrupted")>=0)
                        {
                            changedDigest=true;var settled=manager.RequiredStateSettled;double original=(double)Read(manager,"clientInterrupted");
                            try {manager.RequiredStateSettled=()=>false;Call("ReceiveResumeClient",p);}finally{manager.RequiredStateSettled=settled;}
                            var field=typeof(PNNetworkManager).GetField("fenceDigest",Flags);field.SetValue(manager,new string('b',64));
                            try{Call("TryFinishPendingResume");}finally{field.SetValue(manager,p.BindingDigest);}
                            if(Read(manager,"pendingFinalResume")!=null||(uint)Read(manager,"clientResumeFloor")<=p.Sequence||(double)Read(manager,"clientInterrupted")!=original||manager.ClientInputReady)throw new Exception("digest change failed fresh-attempt/original-grace contract");
                            Event("digest-change-new-attempt-pass",p.Sequence.ToString());return;
                        }
                        Call("ReceiveResumeClient", p); Snapshot("resume-applied", true);
                    });
                    NetworkClient.ReplaceHandler<PNStateFence>(p=> { Event("fence-received", Newtonsoft.Json.JsonConvert.SerializeObject(p)); Call("ReceiveFence", p); });
                    NetworkClient.ReplaceHandler<PNBuffPacket>(p=> { Event("buff-packet", "kind="+p.Kind+" revision="+p.Revision+" transfer="+p.Transfer+" time="+p.Time.ToString("R",CultureInfo.InvariantCulture)+" host="+p.HostTime.ToString("R",CultureInfo.InvariantCulture)+" rate="+p.Rate); Call("ReceiveBuffPacket",p); });
                }
            }
            private void Save<T>() where T:struct,NetworkMessage { ushort id=NetworkMessageId<T>.Id; saved[id]=handlers[id]; }
            public void Event(string kind, string detail)
            {
                if(kind=="pause-request" && detail=="false")resumeStarted=Time.realtimeSinceStartupAsDouble;
                if(kind=="pause-request" && detail=="false" && Environment.GetCommandLineArgs().Contains("--pn-time-hold-resume"))holdUntil=Time.realtimeSinceStartupAsDouble+6;
                log.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new { real=Time.realtimeSinceStartupAsDouble, kind, detail }));
            }
            public void Snapshot(string phase, bool force=false)
            {
                if(held.Count>0 && Time.realtimeSinceStartupAsDouble>=holdUntil) { Event("resume-release",held.Count.ToString()); while(held.Count>0) Call("ReceiveResumeClient",held.Dequeue()); }
                if(gateAck && injected && !replayed && Time.realtimeSinceStartupAsDouble-resumeStarted>=3)
                { replayed=true; Event("retry-old-ack",rejectedAck.Sequence.ToString()); Call("ReceiveResumeClient",rejectedAck); }
                if(gateAck && injected && !restarted && Time.realtimeSinceStartupAsDouble-resumeStarted>=6)
                {
                    restarted=true; if(!manager.ClientInputReady)throw new Exception("T2 final ACK did not recover automatically");Event("automatic-resume-pass","no fixture pause/resume restart");
                    manager.NotifyApplicationPause(true);manager.NotifyApplicationPause(false);Call("ReceiveResumeClient",rejectedAck);
                    if(manager.ClientInputReady)throw new Exception("old ACK opened a new attempt");Event("stale-ack-new-attempt-pass",rejectedAck.Sequence.ToString());
                }
                if(gateAck&&changedDigest&&!secondResume&&Time.realtimeSinceStartupAsDouble-resumeStarted>=10)
                {secondResume=true;if(!manager.ClientInputReady)throw new Exception("fresh attempt after digest change did not settle");Event("digest-change-resume-pass","fresh attempt settled within original grace");}
                var state=new System.Collections.Generic.Dictionary<string,object>();
                foreach(string name in new[]{"paused","clientInputReady","clientInterrupted","clientResumeFloor","heartbeatSequence","lastResponseSequence","fenceAttempt","fenceCount","fenceDigest"}) state[name]=Read(manager,name);
                state["adaptersSettled"]=Call("AdaptersSettled");
                state["fenceAtAttemptSettled"]=Call("FenceSettled", Read(manager,"fenceAttempt"), Read(manager,"fenceDigest"));
                state["fenceSettled"]=Call("FenceSettled", Read(manager,"lastResponseSequence"), Read(manager,"fenceDigest"));
                state["digest"]=Call("BindingDigest", new object[]{null, Guid.Empty});
                state["hasBaseline"]=buffs.HasBaseline; state["appliedRevision"]=buffs.AppliedRevision;
                state["isPlaying"]=manager.IsPlaying; state["failure"]=manager.LastFailure;
                var closure=buffs.AuthoritativePresentationTime?.Target;
                if(closure!=null) foreach(var f in closure.GetType().GetFields(Flags)) if(f.FieldType==typeof(double)) state["anchor_"+f.Name]=f.GetValue(closure);
                var bindings=(System.Collections.IDictionary)Read(manager,"bindings");
                var rows=new System.Collections.Generic.List<object>();
                foreach(var binding in bindings.Values)
                {
                    var b=Read(binding,"Buffs"); var component=(PNBuffManager)Read(b,"Component");
                    rows.Add(new { metadata=JsonUtility.ToJson(Read(binding,"Metadata")), hostSession=Read(Read(binding,"Metadata"),"HostSession")?.ToString(), world=Read(Read(binding,"Metadata"),"World")?.ToString(), token=Read(binding,"Token"), prepared=Read(b,"Prepared"), lastTransfer=Read(b,"LastTransfer"), records=Read(b,"RecordBytes"), assembly=Read(b,"Assembly")!=null, baseline=component?.HasBaseline, revision=component?.AppliedRevision });
                }
                state["bindings"]=rows;
                state["fence"]=Read(manager,"clientFence");
                if(NetworkServer.active) state["peers"]=NetworkServer.connections.Values.Where(c=>c.authenticationData is PNPeer).Select(c=>new { c.connectionId, peer=JsonUtility.ToJson(c.authenticationData) }).ToArray();
                string encoded=Newtonsoft.Json.JsonConvert.SerializeObject(state);
                if(force||encoded!=last) { Event(phase,encoded); last=encoded; }
            }
            public void Dispose() { if(handlers!=null)foreach(var entry in saved)handlers[entry.Key]=entry.Value; log.Dispose(); }
        }
        private static IEnumerator WaitTime(Func<bool> condition,string label)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+45;
            while(!condition()){if(NetworkManager.singleton is PNNetworkManager m&&!string.IsNullOrEmpty(m.LastFailure))throw new Exception(label+": "+m.LastFailure);if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException(label);yield return null;}
        }
        private static IEnumerator TransferTimeCheck(string folder,string peer,PNPlayerMove subject)
        {
            var manager=(PNNetworkManager)NetworkManager.singleton;bool host=NetworkServer.active;
            var game=PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
            var source=subject.GetComponent<EveEntityIdentity>().World;EveWorldContext destination=null;PNWorldTransferReceipt receipt=null;
            string marker=Path.Combine(folder,"time-transfer-world");
            if(host)
            {
                destination=game.Worlds.Create(source.DefinitionId,Guid.Empty,1,game.Worlds.Get(source).Features.Ids);
                yield return WaitTime(()=>destination.State==EveWorldState.Ready||destination.State==EveWorldState.Paused,"transfer destination ready");
                File.WriteAllText(marker+".pending",destination.InstanceId.ToString());File.Move(marker+".pending",marker);
                destination.SetPaused(true);destination.RestoreClock(100);
                manager.SubscribeWorld(NetworkServer.connections.Values.Single(c=>c.authenticationData is PNPeer p&&p.Owner==1),destination);
                yield return null;destination.RestoreClock(200);
                yield return WaitTime(()=>File.Exists(Path.Combine(folder,"client1-time-restored")),"paused destination restored clock observed");
                destination.SetPaused(false);
                receipt=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{subject.GetComponent<EveEntityLoader>()},destination,"main","player"));
                yield return WaitTime(()=>receipt.IsTerminal&&!receipt.CleanupPending,"transfer A to B");
                if(receipt.State!=PNWorldTransferState.Completed)throw new Exception("time transfer failed "+receipt.Error);
                destination.SetPaused(true);File.WriteAllText(Path.Combine(folder,"time-transfer-value"),destination.GameplayTime.ToString("R",CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(folder,"time-transfer-at-b"),"ok");
            }
            yield return WaitTime(()=>File.Exists(marker),"destination marker");
            Guid id=Guid.Parse(File.ReadAllText(marker));
            if(peer=="client1")
            {
                yield return WaitTime(()=>manager.ReadWorldPresentationClock(id,out var value)==PNClockRead.Ready&&Math.Abs(value-200)<1e-8,"same-rate restore exports fresh paused world baseline");
                File.WriteAllText(Path.Combine(folder,"client1-time-restored"),"ok");
            }
            yield return WaitTime(()=>File.Exists(Path.Combine(folder,"time-transfer-at-b")),"host transfer B marker");
            if(peer=="client1")
            {
                yield return WaitTime(()=>manager.ClientWorldReady(id)&&manager.ReadWorldPresentationClock(id,out _)==PNClockRead.Ready,"owner destination clock/input baseline");
                double heldValue=double.Parse(File.ReadAllText(Path.Combine(folder,"time-transfer-value")),CultureInfo.InvariantCulture);
                yield return WaitTime(()=>manager.ReadWorldPresentationClock(id,out var value)==PNClockRead.Ready&&Math.Abs(value-heldValue)<1e-8,"destination paused projection");
                if(!EveRegistrySession.Context.Entities.Snapshot().Any(x=>x.GetComponent<EveEntityIdentity>().OwnerId==1&&x.GetComponent<EveEntityIdentity>().World.InstanceId==id))throw new Exception("owner absent from destination");
            }
            else if(!host)
            {
                if(manager.ReadWorldPresentationClock(id,out _)!=PNClockRead.MissingBaseline)throw new Exception("unsubscribed world leaked clock");
                if(manager.ReadWorldPresentationClock(source.InstanceId,out _)!=PNClockRead.Ready)throw new Exception("unrelated source clock stopped");
                manager.ReadWorldPresentationClock(source.InstanceId,out var before);
                yield return WaitTime(()=>manager.ReadWorldPresentationClock(source.InstanceId,out var value)==PNClockRead.Ready&&value>before+.1,"source clock advances while destination is paused");
            }
            File.WriteAllText(Path.Combine(folder,peer+"-time-transfer-b"),"ok");
            if(host)
            {
                yield return WaitTime(()=>File.Exists(Path.Combine(folder,"client1-time-transfer-b"))&&File.Exists(Path.Combine(folder,"client2-time-transfer-b")),"peer destination checks");
                destination.SetPaused(false);
                receipt=game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(),new[]{subject.GetComponent<EveEntityLoader>()},source,"main","player"));
                yield return WaitTime(()=>receipt.IsTerminal&&!receipt.CleanupPending,"transfer B to A");
                if(receipt.State!=PNWorldTransferState.Completed)throw new Exception("time return transfer failed "+receipt.Error);
                File.WriteAllText(Path.Combine(folder,"time-transfer-returned"),"ok");
            }
            yield return WaitTime(()=>File.Exists(Path.Combine(folder,"time-transfer-returned")),"return marker");
            if(!host)yield return WaitTime(()=>manager.ClientWorldReady(source.InstanceId)&&manager.ReadWorldPresentationClock(source.InstanceId,out _) == PNClockRead.Ready,"source clock after return");
            File.WriteAllText(Path.Combine(folder,peer+"-time-transfer-pass"),"PASS A-B-A identity/world clock checks");
        }
        private static IEnumerator Measure(string folder, string peer, PNPlayerMove subject)
        {
            var manager = (PNNetworkManager)NetworkManager.singleton;
            bool host = NetworkServer.active;
            var buffs = subject.GetComponent<PNBuffManager>();
            var identity = subject.GetComponent<EveEntityIdentity>();
            var world = identity.World;
            var clock = subject.GetComponent<EveTimeScale>();
            float previousScale = Time.timeScale, previousMin = clock.MinScale;
            long modifier = 0;
            string phase = "setup";
            var path = Path.Combine(folder, peer + "-timing.csv");
            using (var diagnostics = new RecoveryDiagnostics(folder, peer, manager, buffs))
            using (var csv = new StreamWriter(path))
            {
                csv.WriteLine("phase,localReal,hostRealEstimate,echoRtt,offsetBound,netScaled,netUnscaled,predicted,mirrorBuffer,bufferMultiplier,moveRender,hostScaledEstimate,moveAge,buffProjection,buffExpectedAtRender,buffError,hostBuffAtReply,worldLocal,entityScale,poseLatest,poseCount,burnRemaining,hostBurnAtReply,worldProjection,worldExpectedAtRender");
                PNTimeProbeReply reply = default; double received = -1;
                if (host) NetworkServer.RegisterHandler<PNTimeProbeRequest>((connection, request) => connection.Send(new PNTimeProbeReply {
                    Sent = request.Sent, HostReal = NetworkTime.localTime, HostScaled = NetworkTime.time,
                    BuffGame = (double)Presentation.GetValue(buffs), WorldGame = world.GameplayTime, EntityScale = clock.Scale,
                    BurnRemaining = buffs.TryReadPresentationTiming(EveId.Parse("project_n:burn"), out var timing) && timing.Present ? timing.Remaining ?? -1 : -1
                }), true);
                else NetworkClient.RegisterHandler<PNTimeProbeReply>(message => { reply = message; received = NetworkTime.localTime; }, true);
                File.WriteAllText(Path.Combine(folder, peer + "-time-ready"), "ok");
                try
                {
                    string[] phases = { "normal", "half", "double", "pause", "resume", "entity-quarter", "entity-stop", "world-pause", "world-resume", "interruption", "recovered" };
                    float[] rates = { 1, .5f, 2, 0, 1, 1, 1, 1, 1, 1, 1 };
                    for (int p = 0; p < (Environment.GetCommandLineArgs().Contains("--pn-time-transfer-only")?0:phases.Length); p++)
                    {
                        phase = phases[p];
                        if (host)
                        {
                            double deadline = Time.realtimeSinceStartupAsDouble + 45;
                            while (!File.Exists(Path.Combine(folder, "client1-time-ready")) || !File.Exists(Path.Combine(folder, "client2-time-ready")))
                            { if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("T0 ready"); yield return null; }
                            if (modifier != 0) { clock.RemoveModifier(modifier); modifier = 0; }
                            clock.MinScale = 0;
                            if (p == 5 || p == 6) modifier = clock.AddOwnedModifier(EveId.Parse("project_n:t0_probe"), EveTimeModifierKind.Scale, p == 5 ? -.75f : -1f, buffs);
                            if (p == 7) world.SetPaused(true);
                            if (p == 8) world.SetPaused(false);
                            if (p != 7 && p != 9 && world.CanExecute)
                            {
                                var burn = EveId.Parse("project_n:burn");
                                buffs.Buffs.RemoveMatching(x => x.Id == burn, PNBuffEndReason.Dispelled);
                                buffs.Buffs.Apply(new PNBuffApplyRequest(burn, PNCombatSource.Capture(subject.GetComponent<PNStat>())));
                            }
                            File.WriteAllText(Path.Combine(folder, "time-phase-" + p), "ok");
                        }
                        double until = Time.realtimeSinceStartupAsDouble + 45;
                        while (!File.Exists(Path.Combine(folder, "time-phase-" + p))) { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("T0 phase"); yield return null; }
                        Time.timeScale = rates[p];
                        if (!host && p == 9) { diagnostics.Event("pause-request", "true"); manager.NotifyApplicationPause(true); }
                        if (!host && p == 10) { diagnostics.Event("pause-request", "false"); manager.NotifyApplicationPause(false); }
                        double start = Time.realtimeSinceStartupAsDouble, nextPing = 0, nextRow = 0;
                        double duration = p == 0 ? 14 : p == 9 ? 1 : p == 10 ? 15 : 7;
                        // Sample while real clocks/transport/replica Updates run normally. No cross-process local-clock subtraction.
                        while (Time.realtimeSinceStartupAsDouble - start < duration)
                        {
                            double now = NetworkTime.localTime;
                            diagnostics.Snapshot(phase);
                            if (!host && now >= nextPing) { NetworkClient.Send(new PNTimeProbeRequest { Sent = now }); nextPing = now + .1; }
                            if (now >= nextRow)
                            {
                                nextRow = now + .05;
                                double rtt = received < 0 ? double.NaN : received - reply.Sent;
                                double hostNow = host ? now : received < 0 ? double.NaN : reply.HostReal + rtt / 2 + now - received;
                                double scaledNow = host ? NetworkTime.time : reply.HostScaled + (rtt / 2 + now - received) * rates[p];
                                double render = PNTimeService.For(EveRegistrySession.Context).Sample().Presentation;
                                double projected = buffs.AuthoritativePresentationTime != null ? buffs.AuthoritativePresentationTime() : (double)Presentation.GetValue(buffs);
                                // Valid only within stable phase after warmup; host game clock rate = global rate unless world paused.
                                double expected = host ? projected : reply.BuffGame + (NetworkTime.time - reply.HostScaled) * (p == 7 ? 0 : 1);
                                double latest = double.NaN; int count = 0;
                                if (subject.ObserverMovement != null)
                                {
                                    var list = (System.Collections.IList)Samples.GetValue(subject.ObserverMovement); count = list.Count;
                                    if (count > 0) latest = ((EveEngine.Movement.MovePoseState)list[count - 1]).SampleTime;
                                }
                                double burnRemaining = buffs.TryReadPresentationTiming(EveId.Parse("project_n:burn"), out var timing) && timing.Present ? timing.Remaining ?? -1 : -1;
                                double mappedWorld=manager.ReadWorldPresentationClock(world.InstanceId,out var mapped)==PNClockRead.Ready?mapped:double.NaN;
                                double expectedWorld=host?world.GameplayTime:reply.WorldGame+(render-reply.HostReal)*(p==7?0:rates[p]);
                                double[] values = { now, hostNow, rtt, rtt / 2, NetworkTime.time, NetworkTime.unscaledTime, NetworkTime.predictedTime,
                                    NetworkClient.bufferTime, NetworkClient.bufferTimeMultiplier, render, scaledNow, hostNow - render, projected, expected, projected - expected,
                                    host ? (double)Presentation.GetValue(buffs) : reply.BuffGame, world.GameplayTime, clock.Scale, latest, count, burnRemaining, reply.BurnRemaining, mappedWorld, expectedWorld };
                                csv.WriteLine(phase + "," + string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
                            }
                            yield return null;
                        }
                        csv.Flush(); File.WriteAllText(Path.Combine(folder, peer + "-time-phase-" + p), "ok");
                        if (host)
                        {
                            until = Time.realtimeSinceStartupAsDouble + 45;
                            while (!File.Exists(Path.Combine(folder, "client1-time-phase-" + p)) || !File.Exists(Path.Combine(folder, "client2-time-phase-" + p)))
                            { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("T0 phase peers"); yield return null; }
                        }
                    }
                    if(Environment.GetCommandLineArgs().Contains("--pn-time-transfer"))yield return TransferTimeCheck(folder,peer,subject);
                    if(peer=="client2"&&Environment.GetCommandLineArgs().Contains("--pn-time-expire"))
                    {
                        var policyProfile=(PNNetworkProfile)manager.Policy.GetType().GetField("profile",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager.Policy);
                        double grace=policyProfile.GraceSeconds;policyProfile.GraceSeconds=2;
                        try
                        {
                            double interrupted=Time.realtimeSinceStartupAsDouble;manager.NotifyApplicationPause(true);
                            while(NetworkClient.active&&Time.realtimeSinceStartupAsDouble-interrupted<4)yield return null;
                            if(NetworkClient.active||Time.realtimeSinceStartupAsDouble-interrupted<1.9||manager.LastFailure!="Host interruption grace expired.")throw new Exception("native original-grace timeout failed: "+manager.LastFailure);
                            File.WriteAllText(Path.Combine(folder,"client2-time-expire-pass"),"PASS actual client timeout at shortened 2-second profile grace");
                        }
                        finally{policyProfile.GraceSeconds=grace;}
                    }
                    File.WriteAllText(Path.Combine(folder, peer + "-time-config.txt"), "sendRate=" + manager.sendRate + " actualServerRate=" + NetworkServer.sendRate + " rtt=" + NetworkTime.rtt + " variance=" + NetworkTime.rttVariance + " moveSend=" + manager.Profile.MoveSendSeconds + " moveDelay=" + manager.Profile.MoveInterpolationSeconds);
                    File.WriteAllText(Path.Combine(folder, peer + "-time-complete"), "ok");
                    double finish = Time.realtimeSinceStartupAsDouble + 45;
                    while (!File.Exists(Path.Combine(folder, "host-time-complete")) || !File.Exists(Path.Combine(folder, "client1-time-complete")) || !File.Exists(Path.Combine(folder, "client2-time-complete")))
                    { if (Time.realtimeSinceStartupAsDouble > finish) throw new TimeoutException("T0 complete"); yield return null; }
                }
                finally
                {
                    Time.timeScale = previousScale;
                    if (host) { if (modifier != 0) clock.RemoveModifier(modifier); clock.MinScale = previousMin; if (world.State == EveWorldState.Paused) world.SetPaused(false); NetworkServer.UnregisterHandler<PNTimeProbeRequest>(); }
                    else { manager.NotifyApplicationPause(false); NetworkClient.UnregisterHandler<PNTimeProbeReply>(); }
                }
            }
        }
    }
}
