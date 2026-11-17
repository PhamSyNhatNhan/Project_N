#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNActionDeliveryChecks
    {
        static PNActionStream Stream(PNActionPublication s)=>new PNActionStream{Session=Guid.NewGuid(),Source=s.Source,NetId=1,Entity=1,Generation=1,Subscription=1,Nonce=1,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
        public static string Run(string folder)
        {
            int checks=0;void C(bool ok,string why){if(!ok)throw new Exception("Action C "+why);checks++;}
            var profile=new PNNetworkProfile();C(profile.ProtocolLimits.Length<=256,"admission fingerprint stays bounded");var before=profile.ProtocolLimits;profile.ActionSendSeconds=.1;C(before!=profile.ProtocolLimits,"Action cadence affects compatibility digest");C(profile.ProtocolLimits==profile.Snapshot().ProtocolLimits,"profile snapshot keeps compatibility digest");
            var results=new JArray();
            bool Same(JToken a,JToken b){if(a is JArray x&&b is JArray y)return x.Count==y.Count&&x.Zip(y,Same).All(v=>v);if((a.Type==JTokenType.Integer||a.Type==JTokenType.Float)&&(b.Type==JTokenType.Integer||b.Type==JTokenType.Float))return (double)a==(double)b;return JToken.DeepEquals(a,b);}
            using(var source=new PNActionPublication())using(var reader=new PNActionPublication.Subscription(source))
            {
                var key=new PNActionRunKey(1,1);source.Begin(key,"test:e","test:d","test:a",1,0,1);source.Advance(key,1,1,1,1);source.End(key,2,PNActionRunEnd.Completed);
                var records=reader.ReadRequired(int.MaxValue,256);C(new[]{0,2,3,4}.All(tag=>records.Any(r=>(int)r.ToArray()[0]==tag)),"typed required covers S/anchor/reset/terminal");
                foreach(var r in records){C(Same(r.ToArray(),JArray.Parse(r.Json)),"typed required/legacy JSON semantics");var copy=r.ToArray();copy[0]=99;C((int)r.ToArray()[0]!=99,"typed required gives independent mutable copies");}
            }
            using(var source=new PNActionPublication())using(var reader=new PNActionPublication.Subscription(source))
            {
                var key=new PNActionRunKey(1,1);source.Begin(key,"test:e","test:d","test:a",1,0,1);source.Advance(key,1,2,1,1);reader.Reset(source.Sequence);
                var frozen=reader.ReadLatest(2,int.MaxValue).Single();source.Advance(key,2,3,1,1);
                var row=Newtonsoft.Json.Linq.JArray.Parse(frozen.Json);
                C(frozen.Stamp==2&&frozen.Progress==1&&(double)row[3]==2&&(double)row[4]==1,"typed sample/lazy JSON remain frozen after mutation");
                C(frozen.HistoryGeneration==(long)row[6]&&frozen.NeedSequence==(long)row[7],"typed sample keeps wire dependency identity");
            }
            foreach(bool receiveStall in new[]{false,true})foreach(double stall in new[]{0d,.2,.5,2})
            {
                using(var source=new PNActionPublication())
                {
                    for(int i=1;i<=64;i++)source.Begin(new PNActionRunKey(1,i),"test:e","test:d","test:a",1,0,1);
                    var stream=Stream(source);var sendBudget=new PNActionReceiveBudget(32*1024*1024);var receiveBudget=new PNActionReceiveBudget();
                    PNActionSender sender=new PNActionSender(source,stream,1,1,4,sendBudget);var receiver=new PNActionReceiver(receiveBudget,stream,1,4);
                    var healthyStream=stream;healthyStream.Nonce=2;
                    var healthySender=new PNActionSender(source,healthyStream,1,1,4,sendBudget);var healthyReceiver=new PNActionReceiver(receiveBudget,healthyStream,1,4);var healthyAges=new List<double>();
                    var held=new Queue<PNActionPacket>();int resets=0;double maxAge=0,now=1;
                    try
                    {
                        for(int tick=0;tick<400;tick++)
                        {
                            now=1+tick*.01;
                            if(tick>0)for(int i=1;i<=64;i++)source.Advance(new PNActionRunKey(1,i),1+(tick-1)*.01,now,.01,1);
                            if(sender.NeedsBaseline||sender.Receipt.Applied&&sender.OldestPending(now)>.5)
                            {
                                double deadline=sender.Receipt.Applied?now+3:sender.Deadline;sender.Dispose();stream.Subscription++;resets++;receiver.Reset(stream,stream.Subscription,deadline);
                                sender=new PNActionSender(source,stream,stream.Subscription,now,deadline,sendBudget);
                            }
                            healthySender.Capture(now);for(int n=0;n<8;n++)if(!healthySender.DrainOne(now,x=>true,p=>healthyReceiver.Receive(p,now)))break;
                            var healthyAck=healthyReceiver.TryApply(now);if(healthyAck.HasValue)healthySender.Confirm(healthyAck.Value,now);
                            healthyAges.Add(healthySender.OldestPending(now));C(!healthySender.NeedsBaseline&&healthyReceiver.State==PNActionReceiveState.Prepared,"healthy peer isolated from slow peer");
                            sender.Capture(now);bool blocked=now>=1.5&&now<1.5+stall;
                            for(int n=0;n<8;n++)if(!sender.DrainOne(now,size=>!(blocked&&!receiveStall),p=>held.Enqueue(p)))break;
                            if(!(blocked&&receiveStall))while(held.Count>0)receiver.Receive(held.Dequeue(),now);
                            receiver.Tick(now);var ack=receiver.TryApply(now);if(ack.HasValue)sender.Confirm(ack.Value,now);
                            C(receiver.State!=PNActionReceiveState.Failed&&receiver.State!=PNActionReceiveState.NeedsRecovery,"live model bounded and coherent: "+receiver.Failure);
                            if(!blocked&&now>1.8+stall)maxAge=Math.Max(maxAge,sender.OldestPending(now));
                        }
                        C(stall>0||resets==0,"normal no routine resets");C(receiver.RunCount==64,"full 64 active set");
                        for(int i=1;i<=64;i++)C(Math.Abs(receiver.Progress(1,i)-source.Progress(new PNActionRunKey(1,i)))<=.11,"current progress within selected cadence");
                        C(maxAge<=.5,"bounded no-stall pending age");healthyAges.Sort();C(healthyAges[(int)(healthyAges.Count*.99)]<=.2&&healthyAges.Last()<=.5,"healthy peer p99/max age targets");C(held.Count==0&&sender.Count<8,"drained after stall");
                        results.Add(new JObject{["receiveStall"]=receiveStall,["stallSeconds"]=stall,["resets"]=resets,["maxNoStallAge"]=maxAge,["senderBytes"]=sender.Bytes,["receiverBytes"]=receiveBudget.Used});
                    }
                    finally{sender.Dispose();receiver.Dispose();healthySender.Dispose();healthyReceiver.Dispose();}
                    C(sendBudget.Used==0&&receiveBudget.Used==0,"all session charges returned");
                }
            }
            using(var source=new PNActionPublication())
            {
                var b=new PNActionReceiveBudget();bool admit=true;var stream=Stream(source);
                using(var sender=new PNActionSender(source,stream,1,1,4,b,n=>admit))
                {
                    source.Begin(new PNActionRunKey(1,1),"test:e","test:d","test:a",1,0,1);admit=false;sender.Capture(1);
                    C(sender.NeedsBaseline&&sender.SubmittedSequence==0,"rejected enqueue never commits required");
                }C(b.Used==0,"failed enqueue cleanup");
            }
            using(var source=new PNActionPublication())
            {
                var stream=Stream(source);var sendBudget=new PNActionReceiveBudget();var receiveBudget=new PNActionReceiveBudget();
                using(var sender=new PNActionSender(source,stream,1,1,5,sendBudget))using(var receiver=new PNActionReceiver(receiveBudget,stream,1,5))
                {
                    for(int tick=0;tick<200;tick++)
                    {double t=1+tick*.02;var k=new PNActionRunKey(2,tick+1);source.Begin(k,"test:e","test:d","test:a",1,0,t);source.End(k,t,tick%2==0?PNActionRunEnd.Completed:PNActionRunEnd.Cancelled);sender.Capture(t);
                     for(int n=0;n<8;n++)if(!sender.DrainOne(t,x=>true,p=>receiver.Receive(p,t)))break;var ack=receiver.TryApply(t);if(ack.HasValue)sender.Confirm(ack.Value,t);
                     C(!sender.NeedsBaseline&&receiver.State==PNActionReceiveState.Prepared,"short S/E before capture remains required");}
                    var model=typeof(PNActionReceiver).GetField("model",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(receiver);C((int)model.GetType().GetField("Starts").GetValue(model)==200&&(int)model.GetType().GetField("Ends").GetValue(model)==200,"continuous lifecycle coverage of every completed/cancelled short run");
                }C(sendBudget.Used==0&&receiveBudget.Used==0,"short lifecycle cleanup");
            }
            using(var oldSource=new PNActionPublication())
            {
                var stream=Stream(oldSource);var budget=new PNActionReceiveBudget();using(var receiver=new PNActionReceiver(budget,stream,1,4))
                {
                    var oldBody=new JArray(2,new JArray(stream.Session.ToString("N"),stream.Scope.World.ToString("N"),1,1,1,stream.Source.ToString("N")),1,1,0,new JArray());foreach(var p in PNActionWire.Pack(stream,1,1,1,0,oldBody,true))receiver.Receive(p,1);receiver.TryApply(1);
                    stream.Subscription=2;receiver.Reset(stream,2,4);var actual=stream;actual.Source=Guid.NewGuid();receiver.ResolveSource(actual,2);C(receiver.Deadline==4&&receiver.State==PNActionReceiveState.Waiting,"source resolution preserves coalesced request/deadline");
                    var body=(JArray)oldBody.DeepClone();body[1][5]=actual.Source.ToString("N");foreach(var p in PNActionWire.Pack(actual,2,1,1,0,body,true))receiver.Receive(p,2);C(receiver.TryApply(2).HasValue,"host rebuild and client retry with same request cannot deadlock");
                    bool refused=false;try{receiver.ResolveSource(stream,2);}catch(InvalidOperationException){refused=true;}C(refused,"prepared stream cannot silently replace source");
                }C(budget.Used==0,"provisional source cleanup");
            }
            var go=new GameObject("Action shared budget fixture");go.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var manager=go.AddComponent<PNNetworkManager>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var peerType=typeof(PNNetworkManager).GetNestedType("ActionPeer",BindingFlags.NonPublic);var state=Activator.CreateInstance(peerType,true);var deadline=peerType.GetField("Deadline");var recovering=peerType.GetField("Recovering");var begin=peerType.GetMethod("BeginRecovery");
                var fence=peerType.GetMethod("BeginFence");C((bool)fence.Invoke(state,new object[]{1u}),"first resume fence requests a fresh cut");C(!(bool)fence.Invoke(state,new object[]{1u}),"same fence cannot repeatedly restart baseline");C((bool)fence.Invoke(state,new object[]{2u}),"new resume attempt requires new actual-apply receipt");
                deadline.SetValue(state,3d);begin.Invoke(state,new object[]{99d});C((double)deadline.GetValue(state)==3,"host pending recovery cannot extend deadline");recovering.SetValue(state,false);begin.Invoke(state,new object[]{15d});C((double)deadline.GetValue(state)==15,"host starts a new episode only after prior ACK");begin.Invoke(state,new object[]{16d});C((double)deadline.GetValue(state)==15,"host rebuild-to-ACK preserves episode deadline");
                manager.RequiredAdapters.Add("project_n:action");typeof(PNNetworkManager).GetMethod("DisposeActions",flags).Invoke(manager,null);C(!manager.RequiredAdapters.Contains("project_n:action"),"rollback removes required adapter before a disabled next session");
                var take=typeof(PNNetworkManager).GetMethod("TakeSendBudget",flags);
                bool Take(int peer,int size,bool action)=>(bool)take.Invoke(manager,new object[]{peer,size,action});
                C(Take(1,48*1024,false),"Move/Buff/Sync reserved share");C(!Take(1,1,false),"other traffic leaves Action reservation");C(Take(1,16*1024,true),"Action has reserved finite service");C(!Take(1,1,true),"Action cannot consume other share");
                typeof(PNNetworkManager).GetField("sendBudgetFrame",flags).SetValue(manager,-1);
                for(int i=0;i<96;i++)C(Take(1,1,false),"other message share accepted");C(!Take(1,1,false),"tiny other messages leave Action message quota");for(int i=0;i<32;i++)C(Take(1,1,true),"Action message reservation survives tiny-packet saturation");C(!Take(1,1,true),"Action message quota finite");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"matrix.json"),results.ToString());string text="PASS "+checks+" Action C delivery/scheduler checks";File.WriteAllText(Path.Combine(folder,"result.txt"),text);return text;
        }
    }
}
#endif
