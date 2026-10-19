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
        private sealed class StatBindingState
        {
            public PNStatReplication Component;
            public Action<PNStatFrame> LifeNotice;
            public Action<Exception> Failure;
            public readonly List<EveIReplicaStage> Stages=new List<EveIReplicaStage>();
        }
        private const string StatKind="project_n:stat";
        private void InitializeStat()
        {
            RegisterStateAdapter(StatKind,()=>bindings.Values.Where(x=>!x.Retiring&&RoomCatchupBinding(x)).All(x=>x.Candidate?.Loader!=null&&x.Candidate.Loader.IsReady&&
                (x.Candidate.Loader.GetComponent<PNStat>()==null||x.Candidate.Loader.GetComponent<PNStatReplication>()?.View!=null||subscribedSession.HasAuthority)));
        }
        private void PublishStatStages(Binding binding)=>PublishOwnedStages(binding.Stats.Stages);
        private void RollbackStatStages(Binding binding)=>DisposeOwnedStages(binding.Stats.Stages);
        private void HookStat(Binding binding)
        {
            var part=binding.Candidate.Loader.GetComponent<PNStatReplication>();binding.Stats.Component=part;
            if(part==null)return;
            binding.Stats.Failure=error=>WorldFailure(binding.Metadata.World,new InvalidOperationException("Stat projection actor "+binding.Metadata.Entity,error));
            binding.Stats.LifeNotice=frame=>
            {
                if(stopping||!IsPlaying||fatalSerialization||binding.Shell==null||binding.Shell.netId==0)return;
                PNStatWire.Size(frame);
                var notice=new PNStatNotice {NetId=binding.Shell.netId,Session=binding.Metadata.HostSession,Entity=binding.Metadata.Entity,Generation=binding.Metadata.Generation,State=frame};
                foreach(var conn in binding.Shell.netIdentity.observers.Values)if(!(conn is LocalConnectionToClient)&&CanObserveWorld(conn,binding.Metadata.World,binding.Metadata.WorldIncarnation)){notice.Scope=Scope(binding,conn);conn.Send(notice);}
            };
            part.ProjectionFailed+=binding.Stats.Failure;part.LifeObserved+=binding.Stats.LifeNotice;
        }
        private void UnhookStat(Binding binding)
        {
            if(binding.Stats.Component!=null){binding.Stats.Component.ProjectionFailed-=binding.Stats.Failure;binding.Stats.Component.LifeObserved-=binding.Stats.LifeNotice;}
            binding.Stats.Component=null;binding.Stats.LifeNotice=null;binding.Stats.Failure=null;
        }
        private bool CaptureStat(Binding binding)
        {
            if(binding.Candidate?.Loader==null||!binding.Candidate.Loader.IsReady)throw new InvalidOperationException("Published actor lost its Ready state: "+binding.Metadata.Entity);
            if(binding.Stats.Component==null)return true;
            var buffs=binding.Candidate.Loader.GetComponent<PNBuffManager>();
            if(buffs!=null&&buffs.StateCaptureFaulted)throw new InvalidOperationException("Stat mutation source failed for actor "+binding.Metadata.Entity);
            if(!binding.Stats.Component.CanCaptureReplication)return false;
            var frame=binding.Stats.Component.CaptureState();PNStatWire.Size(frame);
            if(!ReferenceEquals(frame,binding.Shell.State)){binding.Shell.State=frame;binding.Shell.SetDirty();}
            return true;
        }
        // Initial decode precedes OnStartClient and gameplay binding; reserve quota here, not later.
        public void ReceiveStat(PNActorShell shell,PNStatFrame frame,bool initial)
        {
            if(stopping||!IsPlaying||fatalSerialization||NetworkServer.active)return;
            try
            {
                if(frame!=null&&frame.LifeOnly)throw new FormatException("Actor snapshot must be full.");
                if(initial)
                {
                    if(!ClientScope(shell.Metadata,out _))throw new FormatException("Initial Stat state requires an authorized loaded world.");
                    if(initialStates.Count>=Profile.PendingBindings||initialStates.ContainsKey(shell.netId))throw new FormatException("Duplicate/overflow initial actor.");
                    long token=checked(++nextToken);shell.BindingToken=token;
                    var m=shell.Metadata;int metadata=144+System.Text.Encoding.UTF8.GetByteCount(m.Character+m.SceneKey+m.EntityKey+m.ContentSlot+m.SpawnEntry+m.Template);
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
            if(stopping||!IsPlaying||fatalSerialization)return;
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
            var baseline=part.StageState(frame,binding.Metadata.HostSession,binding.Metadata.Generation);binding.Stats.Stages.Add(baseline);baseline.Commit();
            foreach(var notice in inbox.ReadRequired<PNStatFrame>(binding.Shell.netId,binding.Token,StatKind).Where(x=>x.Revision>frame.Revision).OrderBy(x=>x.Revision))
            {var stage=part.StageUpdate(notice,binding.Metadata.HostSession,binding.Metadata.Generation);binding.Stats.Stages.Add(stage);stage.Commit();}
        }
        private void ReceiveStatMessage(PNStatNotice notice)
        {
            if(bindings.TryGetValue(notice.NetId,out var scoped))
            {if(!Scope(scoped).Equals(notice.Scope)||!ClientScope(scoped.Metadata,out _))return;}
            else if(initialStates.TryGetValue(notice.NetId,out var initial))
            {var m=initial.Shell.Metadata;if(!ClientScope(m,out var world)||notice.Scope.World!=m.World||notice.Scope.Incarnation!=m.WorldIncarnation||notice.Scope.Membership!=m.MembershipRevision||notice.Scope.Subscription!=world.Offer.Ticket.Subscription)return;}
            else return;
            if(notice.State?.LifeOnly==true){ReceiveLife(notice);return;}
            if(!bindings.TryGetValue(notice.NetId,out var binding))return;
            if(notice.Session!=binding.Metadata.HostSession||notice.Entity!=binding.Metadata.Entity||notice.Generation!=binding.Metadata.Generation)return;
            ReceiveStat(binding.Shell,notice.State,false);
        }
    }
}
