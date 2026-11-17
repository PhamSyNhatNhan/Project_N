using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    // Separate maximum-field wire test, after production play has torn down.
    // Synthetic contexts are explicit: this is not registered Action capacity.
    public static class PNActionBoundaryPlayChecks
    {
        public static bool Enabled=>Environment.GetCommandLineArgs().Contains("--pn-action-delivery-boundary");
        static double Now=>NetworkTime.localTime;
        static void Need(bool ok,string why){if(!ok)throw new Exception("D boundary: "+why);}
        static void Publish(string path,string text){File.WriteAllText(path+".tmp",text);File.Move(path+".tmp",path);}
        static IEnumerator Wait(Func<bool> ready,string why,double seconds=15){double until=Now+seconds;while(!ready()){Need(Now<until,"timeout "+why);yield return null;}}
        public static IEnumerator Run(string folder,string peer)
        {
            var manager=(PNNetworkManager)NetworkManager.singleton;bool host=NetworkServer.active;
            string P(string name)=>Path.Combine(folder,name);
            if(host){PNGameBootstrap.EndPlay();Publish(P("boundary-play-ended"),"");}
            yield return Wait(()=>File.Exists(P("boundary-play-ended"))&&!manager.IsPlaying,"play teardown");
            var results=new JArray();
            for(int test=0;test<3;test++)
            {
                string prefix="boundary-"+test;var budget=new PNActionReceiveBudget();
                if(host)
                {
                    using var source=new PNActionPublication();string id="test:"+new string('x',91);int count=0;
                    while(count<4096&&source.CurrentBytes+640<=source.CurrentLimit){Need(source.Begin(new PNActionRunKey(1,++count),id,id,id,1,0,1)==PNActionPublicationStatus.Ok,"complete max-field source");}
                    Need(source.CurrentBytes>source.CurrentLimit-1280,"near current byte boundary");
                    var connections=new Dictionary<string,NetworkConnectionToClient>();var streams=new Dictionary<string,PNActionStream>();var config=new JObject();
                    foreach(string name in new[]{"client1","client2"}){long owner=long.Parse(File.ReadAllText(P(name+".owner")));var connection=NetworkServer.connections.Values.Single(c=>c.authenticationData is PNPeer p&&p.Owner==owner);connections[name]=connection;
                        var stream=new PNActionStream{Session=Guid.NewGuid(),Source=source.Source,NetId=1,Entity=1,Generation=1,Subscription=test+1,Nonce=(ulong)owner+1,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};streams[name]=stream;config[name]=JObject.FromObject(stream);}
                    config["count"]=count;config["deadlineSeconds"]=3;Publish(P(prefix+"-config.json"),config.ToString());
                    var senders=new Dictionary<int,PNActionSender>();var applied=new Dictionary<int,double>();var duplicates=new Dictionary<int,PNActionPacket>();double begin=0;
                    using var transport=new PNActionTransport((c,s)=>streams.Values.Any(x=>x.Equals(s))&&connections.Values.Contains(c),(c,p)=>{if(senders.TryGetValue(c.connectionId,out var sender)&&sender.Confirm(p,Now))applied[c.connectionId]=Now-begin;},null,null);
                    try
                    {
                        yield return Wait(()=>File.Exists(P(prefix+"-client1-ready"))&&File.Exists(P(prefix+"-client2-ready")),"receivers bound");
                        begin=Now;double deadline=begin+3;
                        foreach(var pair in connections)senders[pair.Value.connectionId]=new PNActionSender(source,streams[pair.Key],test+1,begin,deadline,budget);
                        int maxCharge=budget.Used;var take=typeof(PNNetworkManager).GetMethod("TakeActionBudget",BindingFlags.Instance|BindingFlags.NonPublic);int rotation=0;
                        bool Take(int connection,int bytes)=>(bool)take.Invoke(manager,new object[]{connection,bytes,Now});
                        while(Now<deadline&&!connections.All(p=>p.Key=="client2"&&test==2||senders[p.Value.connectionId].Receipt.Applied))
                        {
                            var peers=connections.Values.ToArray();for(int i=0;i<peers.Length;i++){var c=peers[(i+rotation)%peers.Length];int key=c.connectionId;
                                if(duplicates.TryGetValue(key,out var duplicate)&&Take(key,duplicate.Body.Length+192)){c.Send(duplicate,Channels.Reliable);duplicates.Remove(key);}
                                for(int n=0;n<8;n++)if(!senders[key].DrainOne(Now,b=>Take(key,b),packet=>{c.Send(packet,Channels.Reliable);if(test==1&&packet.Index==0)duplicates[key]=packet;}))break;}
                            rotation++;maxCharge=Math.Max(maxCharge,budget.Used);yield return null;
                        }
                        // The missing-fragment peer must reach its own original
                        // deadline, while the healthy peer completes normally.
                        yield return Wait(()=>File.Exists(P(prefix+"-client1-done"))&&File.Exists(P(prefix+"-client2-done")),"boundary result",5);
                        foreach(var pair in connections)Need(senders[pair.Value.connectionId].Receipt.Applied==(test!=2||pair.Key!="client2"),"ACK only after complete apply");
                        results.Add(new JObject{["case"]=test,["rows"]=count,["sourceCurrentBytes"]=source.CurrentBytes,["sourceLimit"]=source.CurrentLimit,["queueHighWater"]=maxCharge,["applySeconds"]=JObject.FromObject(applied),["sentBytes"]=senders.Values.Sum(x=>x.SentBytes),["missingFragmentFailureExpected"]=test==2});
                    }
                    finally{foreach(var sender in senders.Values)sender.Dispose();Need(budget.Used==0,"sender cleanup");}
                }
                else
                {
                    yield return Wait(()=>File.Exists(P(prefix+"-config.json")),"boundary config");var config=JObject.Parse(File.ReadAllText(P(prefix+"-config.json")));var stream=config[peer].ToObject<PNActionStream>();int count=(int)config["count"];double begin=Now,deadline=Now+3;int omitted=0,duplicates=0;double applied=-1;
                    using(var receiver=new PNActionReceiver(budget,stream,test+1,deadline))
                    using(var transport=new PNActionTransport(null,null,s=>s.Equals(stream),packet=>{if(test==2&&peer=="client2"&&packet.Index==packet.Count-1){omitted++;return;}int prior=receiver.RunCount;receiver.Receive(packet,Now);if(receiver.State!=PNActionReceiveState.Prepared)Need(prior==0&&receiver.RunCount==0,"no partial baseline exposed");if(packet.Index==0)duplicates++;}))
                    {
                        Publish(P(prefix+"-"+peer+"-ready"),"");
                        while(receiver.State!=PNActionReceiveState.Prepared&&receiver.State!=PNActionReceiveState.Failed){receiver.Tick(Now);var ack=receiver.TryApply(Now);if(ack.HasValue){applied=Now-begin;Need(receiver.RunCount==count,"complete boundary row count");for(int i=1;i<=count;i++)Need(receiver.Progress(1,i)==0,"boundary progress");NetworkClient.Send(ack.Value,Channels.Reliable);}yield return null;}
                        bool missing=test==2&&peer=="client2";Need(missing?receiver.State==PNActionReceiveState.Failed&&receiver.RunCount==0&&omitted==1:receiver.State==PNActionReceiveState.Prepared,"explicit complete or original deadline failure");
                        Need(!missing||Now-deadline<.25,"bounded missing fragment timeout");if(test==1)Need(duplicates==2,"real duplicate fragment delivered");
                        results.Add(new JObject{["case"]=test,["rows"]=count,["appliedSeconds"]=applied,["failedAtSeconds"]=missing?Now-begin:-1,["duplicates"]=duplicates,["omitted"]=omitted,["state"]=receiver.State.ToString()});
                    }
                    Need(budget.Used==0,"receiver cleanup");Publish(P(prefix+"-"+peer+"-done"),"");
                }
            }
            if(host)typeof(PNNetworkManager).GetMethod("DisposeActions",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);
            File.WriteAllText(P(peer+"-boundary.json"),results.ToString());Publish(P(peer+"-boundary-finished"),"");
            yield return Wait(()=>File.Exists(P("host-boundary-finished"))&&File.Exists(P("client1-boundary-finished"))&&File.Exists(P("client2-boundary-finished")),"boundary peers finished");
        }
    }
}
