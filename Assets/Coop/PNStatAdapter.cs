using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop
{
    // Typed Stat adapter shares lifecycle and quota ownership with the manager, not gameplay authority.
    public sealed partial class PNNetworkManager
    {
        private const string StatKind="project_n:stat";
        private EveSessionContext subscribedSession;
        private double lastStatCapture;
        private readonly Dictionary<uint,(PNActorShell Shell,long Token)> initialStates=new Dictionary<uint,(PNActorShell,long)>();
        public void InitializeReplication()
        {
            if(subscribedSession!=null)return;
            subscribedSession=EveRegistrySession.Context;
            subscribedSession.ConnectionFailed+=SessionFailed;
            RegisterStateAdapter(StatKind,()=>bindings.Values.Where(x=>!x.Retiring).All(x=>x.Candidate?.Loader!=null&&x.Candidate.Loader.IsReady&&
                (x.Candidate.Loader.GetComponent<PNStat>()==null||x.Candidate.Loader.GetComponent<PNStatReplication>()?.View!=null||subscribedSession.HasAuthority)));
            NetworkLoop.OnLateUpdate+=PumpReplication;
            InitializeBuff();
        }
        private void SessionFailed(string code,string message)=>ProtocolFailure(new InvalidOperationException(code+": "+message),false);
        private void DisposeReplication()
        {
            NetworkLoop.OnLateUpdate-=PumpReplication;
            DisposeBuff();
            if(subscribedSession!=null)subscribedSession.ConnectionFailed-=SessionFailed;
            foreach(var binding in bindings.Values.Concat(awaitingSpawn).ToArray()){UnhookStat(binding);if(binding.Shell!=null&&binding.Shell.netId==0)Destroy(binding.Shell.gameObject);}
            foreach(var value in initialStates)if(inbox.Contains(value.Key,value.Value.Token))inbox.Release(value.Key,value.Value.Token);
            initialStates.Clear();peerFences.Clear();clientFence.Clear();stateAdapters.Clear();subscribedSession=null;
        }
        private void HookStat(Binding binding)
        {
            var part=binding.Candidate.Loader.GetComponent<PNStatReplication>();binding.Stat=part;
            if(part==null)return;
            part.UseTypedTransport();
            binding.StatFailure=error=>ProtocolFailure(new InvalidOperationException("Stat projection actor "+binding.Metadata.Entity,error),true);
            binding.LifeNotice=frame=>
            {
                if(stopping||fatalSerialization||binding.Shell==null||binding.Shell.netId==0)return;
                PNStatWire.Size(frame);
                var notice=new PNStatNotice {NetId=binding.Shell.netId,Session=binding.Metadata.HostSession,Entity=binding.Metadata.Entity,Generation=binding.Metadata.Generation,State=frame};
                foreach(var conn in binding.Shell.netIdentity.observers.Values)if(!(conn is LocalConnectionToClient))conn.Send(notice);
            };
            part.ProjectionFailed+=binding.StatFailure;part.LifeObserved+=binding.LifeNotice;
        }
        private void UnhookStat(Binding binding)
        {
            UnhookBuff(binding);
            if(binding.Stat!=null){binding.Stat.ProjectionFailed-=binding.StatFailure;binding.Stat.LifeObserved-=binding.LifeNotice;}
            foreach(var stage in binding.Stages.AsEnumerable().Reverse())stage.Dispose();binding.Stages.Clear();binding.Stat=null;
        }
        private bool CaptureStat(Binding binding)
        {
            if(binding.Candidate?.Loader==null||!binding.Candidate.Loader.IsReady)throw new InvalidOperationException("Published actor lost its Ready state: "+binding.Metadata.Entity);
            if(binding.Stat==null)return true;
            var buffs=binding.Candidate.Loader.GetComponent<PNBuffManager>();
            if(buffs!=null&&buffs.StateCaptureFaulted)throw new InvalidOperationException("Stat mutation source failed for actor "+binding.Metadata.Entity);
            if(!binding.Stat.CanCaptureReplication)return false;
            var frame=binding.Stat.CaptureState();PNStatWire.Size(frame);
            if(!ReferenceEquals(frame,binding.Shell.State)){binding.Shell.State=frame;binding.Shell.SetDirty();}
            return true;
        }
        // Initial decode precedes OnStartClient and gameplay binding; reserve quota here, not later.
        public void ReceiveStat(PNActorShell shell,PNStatFrame frame,bool initial)
        {
            if(stopping||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(frame!=null&&frame.LifeOnly)throw new FormatException("Actor snapshot must be full.");
                if(initial)
                {
                    if(initialStates.Count>=Profile.PendingBindings||initialStates.ContainsKey(shell.netId))throw new FormatException("Duplicate/overflow initial actor.");
                    long token=checked(++nextToken);shell.BindingToken=token;
                    var m=shell.Metadata;int metadata=64+System.Text.Encoding.UTF8.GetByteCount(m.Character+m.SceneKey+m.EntityKey);
                    inbox.Reserve(shell.netId,token,metadata);initialStates.Add(shell.netId,(shell,token));
                    if(frame!=null)inbox.LatestValue(shell.netId,token,StatKind,frame,PNStatWire.Size(frame));
                    return;
                }
                if(bindings.TryGetValue(shell.netId,out var binding)&&ReferenceEquals(binding.Shell,shell))
                {
                    if(binding.Candidate?.Loader!=null&&binding.Candidate.Loader.IsReady)
                    {var part=binding.Candidate.Loader.GetComponent<PNStatReplication>();if((part==null)!=(frame==null))throw new FormatException("Stat component set changed.");part?.ApplyState(frame,binding.Metadata.HostSession,binding.Metadata.Generation);}
                    else if(frame!=null)inbox.LatestValue(shell.netId,binding.Token,StatKind,frame,PNStatWire.Size(frame));
                }
                else if(initialStates.TryGetValue(shell.netId,out var slot)&&ReferenceEquals(slot.Shell,shell)&&frame!=null)
                    inbox.LatestValue(shell.netId,slot.Token,StatKind,frame,PNStatWire.Size(frame));
                else throw new FormatException("Unknown Stat binding.");
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private void ReceiveLife(PNStatNotice notice)
        {
            if(stopping||fatalSerialization)return;
            try
            {
                if(notice.State==null||!notice.State.LifeOnly)throw new FormatException("Expected life notice.");
                if(!bindings.TryGetValue(notice.NetId,out var binding))
                {
                    if(!initialStates.TryGetValue(notice.NetId,out var initial))return; // Actor already retired.
                    var metadata=initial.Shell.Metadata;
                    if(notice.Session!=metadata.HostSession||notice.Entity!=metadata.Entity||notice.Generation!=metadata.Generation)return;
                    inbox.RequiredValue(notice.NetId,initial.Token,StatKind,notice.State,PNStatWire.Size(notice.State));return;
                }
                var m=binding.Metadata;if(notice.Session!=m.HostSession||notice.Entity!=m.Entity||notice.Generation!=m.Generation)return;
                if(binding.Candidate?.Loader!=null&&binding.Candidate.Loader.IsReady)
                    binding.Candidate.Loader.GetComponent<PNStatReplication>().ApplyState(notice.State,m.HostSession,m.Generation);
                else inbox.RequiredValue(notice.NetId,binding.Token,StatKind,notice.State,PNStatWire.Size(notice.State));
            }
            catch(Exception error){ProtocolFailure(error,false);}
        }
        private void StageStat(Binding binding,EveEntityLoader loader)
        {
            if(fatalSerialization||!EveRegistrySession.Context.IsActive)throw new InvalidOperationException("Session failed before publication.");
            var frame=inbox.ReadLatest<PNStatFrame>(binding.Shell.netId,binding.Token,StatKind);
            var part=loader.GetComponent<PNStatReplication>();
            if((loader.GetComponent<PNStat>()!=null)!=(part!=null)||((part!=null)!=(frame!=null)))throw new FormatException("Missing/unexpected required Stat baseline.");
            if(part==null)return;
            var baseline=part.StageState(frame,binding.Metadata.HostSession,binding.Metadata.Generation);binding.Stages.Add(baseline);baseline.Commit();
            foreach(var notice in inbox.ReadRequired<PNStatFrame>(binding.Shell.netId,binding.Token,StatKind).Where(x=>x.Revision>frame.Revision).OrderBy(x=>x.Revision))
            {var stage=part.StageUpdate(notice,binding.Metadata.HostSession,binding.Metadata.Generation);binding.Stages.Add(stage);stage.Commit();}
        }
        private void ReceiveStatMessage(PNStatNotice notice)
        {
            if(notice.State?.LifeOnly==true){ReceiveLife(notice);return;}
            if(!bindings.TryGetValue(notice.NetId,out var binding))return;
            if(notice.Session!=binding.Metadata.HostSession||notice.Entity!=binding.Metadata.Entity||notice.Generation!=binding.Metadata.Generation)return;
            ReceiveStat(binding.Shell,notice.State,false);
        }
        private void PumpReplication()
        {
            if(stopping)return;
            if(fatalSerialization){StopSession();return;}
            try{PumpBuff();}catch(Exception error){ProtocolFailure(error,NetworkServer.active);return;}
            if(!NetworkServer.active)return;
            try
            {
                // Spawn deferred actors only at drained capture boundaries, within prepare timeout.
                foreach(var binding in awaitingSpawn.ToArray())
                {
                    if(binding.Candidate?.Loader==null||!binding.Candidate.Loader.IsReady||binding.Shell!=null)continue;
                    if(Time.realtimeSinceStartupAsDouble-binding.AwaitingAt>Scheduler.TimeoutMilliseconds/1000d)throw new TimeoutException("Initial Stat capture timed out.");
                    var stat=binding.Candidate.Loader.GetComponent<PNStatReplication>();if(stat!=null&&!stat.CanCaptureReplication){var buffs=binding.Candidate.Loader.GetComponent<PNBuffManager>();if(buffs!=null&&buffs.StateCaptureFaulted)throw new InvalidOperationException("Initial Stat mutation source failed.");continue;}
                    var root=CreateShell<PNActorShell>("Actor "+binding.Metadata.Entity);binding.Shell=root.GetComponent<PNActorShell>();binding.Shell.Metadata=binding.Metadata;
                    HookStat(binding);HookBuff(binding);binding.Shell.HasBuff=binding.Buff!=null;CaptureStat(binding);
                    NetworkServer.Spawn(root,PNActorShell.Asset,binding.OwnerConnection);bindings.Add(binding.Shell.netId,binding);awaitingSpawn.Remove(binding);
                }
                if(Time.realtimeSinceStartupAsDouble-lastStatCapture<.05)return;lastStatCapture=Time.realtimeSinceStartupAsDouble;
                foreach(var binding in bindings.Values.Where(x=>!x.Retiring))CaptureStat(binding);
            }
            catch(Exception error){ProtocolFailure(error,true);StopSession();}
        }
    }
}
