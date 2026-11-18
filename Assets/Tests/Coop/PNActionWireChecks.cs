#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNActionWireChecks
    {
        static PNActionPacket Copy(PNActionPacket p){var w=new NetworkWriter();w.Write(p);if(w.Position+2>4096)throw new Exception("full message cap");return new NetworkReader(w.ToArray()).Read<PNActionPacket>();}
        public static string RunFairness(string folder)
        {
            Directory.CreateDirectory(folder);var output=new JArray();
            var emit=typeof(PNActionDiagnostics).GetMethod("Emit",BindingFlags.Static|BindingFlags.NonPublic);
            var record=typeof(PNActionDiagnostics).GetMethod("RecordCore",BindingFlags.Static|BindingFlags.NonPublic);
            long ticks=PNActionDiagnostics.CallbackTicks,count=PNActionDiagnostics.CallbackCount,inc=PNActionDiagnostics.CoreInclusiveTicks,exc=PNActionDiagnostics.CoreExclusiveTicks;
            Action<string,int,PNActionPacket?,PNActionReceiver,double,double> observer=(k,p,a,b,c,d)=>System.Threading.Thread.SpinWait(10000);
            PNActionDiagnostics.Sample+=observer;
            try{emit.Invoke(null,new object[]{"native-observer",0,null,null,0d,0d});}finally{PNActionDiagnostics.Sample-=observer;}
            if(PNActionDiagnostics.CallbackCount-count!=1||PNActionDiagnostics.CallbackTicks<=ticks)throw new Exception("observer count/ticks not measured");
            record.Invoke(null,new object[]{100L,30L});
            if(PNActionDiagnostics.CoreInclusiveTicks-inc!=100||PNActionDiagnostics.CoreExclusiveTicks-exc!=70)throw new Exception("inclusive/exclusive observer subtraction contract");
            File.WriteAllText(Path.Combine(folder,"observer.txt"),"PASS direct callback count/ticks and inclusive/exclusive subtraction; no performance claim");
            foreach(string mode in new[]{"continuous","held-subset","churn"})
            {
                using(var source=new PNActionPublication())
                {
                    var keys=Enumerable.Range(1,64).Select(i=>new PNActionRunKey(1,i)).ToArray();
                    foreach(var key in keys)source.Begin(key,"test:e","test:d","test:a",1,0,0);
                    var stream=new PNActionStream{Session=Guid.NewGuid(),Source=source.Source,NetId=4,Entity=5,Generation=6,Subscription=1,Nonce=7,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
                    var budget=new PNActionReceiveBudget(32*1024*1024);
                    using(var sender=new PNActionSender(source,stream,1,0,30,budget))
                    {
                        var last=new System.Collections.Generic.Dictionary<string,double>();var born=new System.Collections.Generic.Dictionary<string,double>();double max=0;int updates=0;long ordinal=64;
                        void Drain(double now){while(sender.DrainOne(now,_=>true,packet=>{if(packet.Kind!=1)return;foreach(JArray row in PNActionWire.Decode(packet.Body)){if((int)row[0]!=1)continue;string key=row[1].ToString(Newtonsoft.Json.Formatting.None);if(born.TryGetValue(key,out var t)){max=Math.Max(max,now-t);born.Remove(key);}last[key]=now;updates++;}})){} }
                        Drain(0);
                        for(int step=1;step<=600;step++)
                        {
                            double now=step/60d,prior=(step-1)/60d;
                            if(mode=="churn"&&step%6==0)for(int i=0;i<8;i++){int index=((step/6)*8+i)%64;string old=new JArray(keys[index].Cast,keys[index].Ordinal).ToString(Newtonsoft.Json.Formatting.None);source.End(keys[index],prior,PNActionRunEnd.Completed);born.Remove(old);keys[index]=new PNActionRunKey(1,++ordinal);source.Begin(keys[index],"test:e","test:d","test:a",1,0,prior);}
                            for(int i=0;i<64;i++)
                            {
                                if(mode=="held-subset"&&i>=32&&step>1)continue;
                                source.Advance(keys[i],prior,now,now-prior,1);
                                string key=new JArray(keys[i].Cast,keys[i].Ordinal).ToString(Newtonsoft.Json.Formatting.None);if(!born.ContainsKey(key))born[key]=prior;
                            }
                            sender.Capture(now,.05);Drain(now);
                            foreach(var b in born.Values)max=Math.Max(max,now-b);
                            if(!source.Complete||sender.NeedsBaseline)throw new Exception("fairness fixture lost completeness");
                        }
                        int pendingAtEnd=born.Count;double activeMax=max;
                        for(int drain=1;drain<=60&&born.Count>0;drain++){double now=10+drain/60d;sender.Capture(now,.05);Drain(now);}
                        if(born.Count!=0)throw new Exception("fairness final dirty rows not drained");
                        output.Add(new JObject{["mode"]=mode,["updates"]=updates,["activeMaxDirtyAge"]=activeMax,["maxDirtyAgeIncludingDrain"]=max,["within200ms"]=max<=.2,["outstandingBeforeDrain"]=pendingAtEnd,["outstandingAfterDrain"]=born.Count,["note"]="actual publication/sender, unlimited transport credit; per-key first-dirty oracle; completion retirement excluded"});
                    }
                    if(budget.Used!=0)throw new Exception("fairness sender budget leak");
                }
            }
            string text=output.ToString();File.WriteAllText(Path.Combine(folder,"fairness.json"),text);return text;
        }
        public static string RunLatestSelection()
        {
            const int runCount=64;
            // Latest wire layout: 5-byte batch header, 91-byte rows, 32-row capture limit.
            int slots=Math.Min(32,(PNActionWire.ChunkBytes-5)/91);
            using var source=new PNActionPublication();
            var keys=Enumerable.Range(1,runCount).Reverse().Select(i=>new PNActionRunKey(1,i)).ToArray();
            foreach(var key in keys)
            {
                source.Begin(key,"test:e","test:d","test:a",1,0,0);
                source.Advance(key,0,1,1,1);
            }
            var stream=new PNActionStream{Session=Guid.NewGuid(),Source=source.Source,NetId=4,Entity=5,Generation=6,Subscription=1,Nonce=7,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
            using var sender=new PNActionSender(source,stream,1,1,4,new PNActionReceiveBudget());
            var remaining=Enumerable.Range(1,runCount).Select(i=>(long)i).ToList();
            var delivered=new System.Collections.Generic.List<long>();
            void Drain(double now)
            {
                while(sender.DrainOne(now,_=>true,packet=>{
                    if(packet.Kind!=1)return;
                    foreach(JArray row in PNActionWire.Decode(packet.Body))
                        if((int)row[0]==1)delivered.Add((long)row[1][1]);
                })){}
            }
            Drain(1);
            double previous=1;
            int rounds=(runCount+slots-1)/slots;
            for(int round=0;round<rounds;round++)
            {
                double now=previous+.125;
                foreach(var key in keys)source.Advance(key,previous,now,now-previous,1);
                delivered.Clear();
                sender.Capture(now);
                Drain(now);
                int count=Math.Min(slots,remaining.Count);
                if(!delivered.SequenceEqual(remaining.Take(count)))
                    throw new Exception("Latest fairness: oldest cohort did not receive its available slots in identity order");
                remaining.RemoveRange(0,count);
                previous=now;
            }
            if(remaining.Count!=0||sender.NeedsBaseline)throw new Exception("Latest fairness: cohort did not finish within its service bound");
            return $"PASS latest selector: {runCount} keys in {rounds} service opportunities ({slots} slots each), including coalesced updates";
        }
        public static string RunRequiredBacklog(string folder)
        {
            const double firstStamp=.125, secondStamp=.25;
            var profile=new PNNetworkProfile();
            var trace=new JArray();
            foreach(bool stall in new[]{false,true})
            {
                using var source=new PNActionPublication();
                var keys=Enumerable.Range(1,128).Select(i=>new PNActionRunKey(1,i)).ToArray();
                foreach(var key in keys)source.Begin(key,"test:e","test:d","test:a",1,0,0);
                var stream=new PNActionStream{Session=Guid.NewGuid(),Source=source.Source,NetId=4,Entity=5,Generation=6,Subscription=1,Nonce=7,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
                var budget=new PNActionReceiveBudget();
                using var sender=new PNActionSender(source,stream,1,0,3,budget);
                using var receiver=new PNActionReceiver(budget,stream,1,3);
                long baseline=sender.SubmittedSequence;
                int terminalCount=0;
                bool sawComplete=false;
                void Check(bool ok,string message){if(!ok)throw new Exception("Required backlog: "+message);}
                void Drain(double now)
                {
                    while(sender.DrainOne(now,_=>true,packet=>{
                        Check(receiver.Receive(Copy(packet),now),"receiver rejected packet");
                        if(packet.Kind!=1)return;
                        if(packet.CompleteThroughCapture)
                        {
                            Check(packet.N==source.Sequence,"coverage advertised while required journal had unread rows");
                            sawComplete=true;
                        }
                        foreach(JArray row in PNActionWire.Decode(packet.Body))
                        {
                            if((int)row[0]!=4)continue;
                            terminalCount++;
                            Check((long)row[1]==baseline+terminalCount&&(long)row[2][1]==terminalCount,"terminal order");
                            Check((double)row[5]==(terminalCount==1?firstStamp:secondStamp),"terminal stamp");
                        }
                    })){}
                }
                Drain(0);
                var ack=receiver.TryApply(0);
                Check(ack.HasValue&&sender.Confirm(ack.Value,0),"actual baseline ACK");
                for(int i=0;i<keys.Length;i++)
                    source.End(keys[i],i==0?firstStamp:secondStamp,PNActionRunEnd.Completed);
                Check(source.Sequence==baseline+keys.Length,"fixture journal cut");
                Check(sender.OldestRequired(secondStamp)==secondStamp-firstStamp&&sender.SubmittedSequence==baseline,"initial age and read-only cursor");
                sender.Capture(secondStamp);
                Drain(secondStamp);
                double sample=secondStamp+.125;
                Check(terminalCount>0&&terminalCount<keys.Length&&sender.Count==0,"expected unread terminals with an empty sender queue");
                Check(!sawComplete&&receiver.ReadPresentation(secondStamp,null)==PNActionPresentationCoverage.Unknown,
                    "partial drain cannot prove empty");
                Check(sender.SubmittedSequence==baseline+terminalCount&&sender.OldestRequired(sample)==sample-secondStamp,"partial submission advances to second stamp");
                Check(sender.OldestPending(sample)==sample-secondStamp,"combined age includes journal");
                trace.Add(new JObject{["stall"]=stall,["submitted"]=sender.SubmittedSequence,["published"]=source.Sequence,["queueCount"]=sender.Count,["requiredAge"]=sender.OldestRequired(sample)});
                if(stall)
                {
                    double boundary=secondStamp+profile.ActionPendingSeconds;
                    Check(!sender.PendingExpired(boundary,profile.ActionPendingSeconds),"equality must not trigger recovery");
                    Check(sender.PendingExpired(boundary+.001,profile.ActionPendingSeconds),"overdue journal must request recovery");
                    continue;
                }
                for(int i=1;i<=keys.Length&&terminalCount<keys.Length;i++)
                {
                    double now=secondStamp+i/64d;
                    Check(!sender.PendingExpired(now,profile.ActionPendingSeconds),"timely burst requires no recovery");
                    sender.Capture(now);
                    Drain(now);
                }
                Check(terminalCount==keys.Length&&receiver.AppliedSequence==source.Sequence,"terminal stream did not drain");
                Check(sawComplete&&receiver.ReadPresentation(secondStamp,null)==PNActionPresentationCoverage.Empty,
                    "final required prefix proves empty at terminal boundary");
                Check(sender.OldestRequired(.5)==0&&sender.OldestPending(.5)==0,"drained age");
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"required-backlog.json"),trace.ToString());
            return "PASS required journal age, FIFO drain, certified coverage and ACK-gated recovery predicate";
        }
        public static string RunDependencyClosure()
        {
            using var source=new PNActionPublication();
            var keys=Enumerable.Range(1,64).Select(i=>new PNActionRunKey(1,i)).ToArray();
            using var reader=new PNActionPublication.Subscription(source);
            foreach(var key in keys)
            {
                source.Begin(key,"test:e","test:d","test:a",1,0,0);
                source.Advance(key,0,.05,.05,1);
            }
            var prefix=reader.ReadRequired(3000,64);
            long cut=prefix[prefix.Count-1].N;
            reader.CommitRequiredSubmitted(cut,reader.Generation);
            var eligible=reader.ReadLatest(.05,int.MaxValue);
            if(eligible.Count==0||eligible.Count>=64||eligible.Any(sample=>sample.NeedSequence>cut))
                throw new Exception("Expected both blocked and eligible latest rows");
            var seen=eligible.Select(sample=>sample.Key).ToHashSet();
            foreach(var sample in eligible)reader.CommitLatestSubmitted(sample,reader.Generation);
            var remainder=reader.ReadRequired(int.MaxValue,256);
            if(remainder.Count!=source.Sequence-cut||remainder[0].N!=cut+1)
                throw new Exception("Required FIFO did not preserve the blocked suffix");
            reader.CommitRequiredSubmitted(source.Sequence,reader.Generation);
            var released=reader.ReadLatest(.1,int.MaxValue);
            if(released.Count!=64-seen.Count||released.Any(sample=>!seen.Add(sample.Key)))
                throw new Exception("Barrier release lost or repeated a run");
            foreach(var sample in released)reader.CommitLatestSubmitted(sample,reader.Generation);
            if(reader.OldestPending(.1)!=0||reader.ReadLatest(.1,int.MaxValue).Count!=0)
                throw new Exception("Dependency closure did not drain");
            return $"PASS blocked needN: {eligible.Count} eligible and {released.Count} released";
        }
        public static string RunPresentation()
        {
            int checks=0;
            void Check(bool ok,string label)
            {
                if(!ok)throw new Exception("Action presentation "+label);
                checks++;
            }
            var stream=new PNActionStream{Session=Guid.NewGuid(),Source=Guid.NewGuid(),NetId=1,Entity=2,
                Generation=3,Subscription=1,Nonce=4,Scope=new PNWorldBindingScope{
                    World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
            var budget=new PNActionReceiveBudget();
            var rows=new System.Collections.Generic.List<PNObservedAction>();
            using(var receiver=new PNActionReceiver(budget,stream,1,10))
            {
                long request=1;
                JArray Key(long ordinal)=>new JArray(8L,ordinal);
                JArray Context(double start)=>new JArray("test:e","test:d","test:a",1L,0d,1d,start);
                void Baseline(double stamp,long seq,JArray runs,double? pose=null)
                {
                    var identity=new JArray(stream.Session.ToString("N"),stream.Scope.World.ToString("N"),
                        1L,stream.Entity,stream.Generation,stream.Source.ToString("N"));
                    foreach(var packet in PNActionWire.Pack(stream,request,1,stamp,seq,
                        new JArray(2L,identity,1L,stamp,seq,runs),true))
                        Check(receiver.Receive(Copy(packet),stamp),"accept baseline fragment");
                    Check(receiver.TryApply(stamp,pose).HasValue,"commit baseline");
                }
                void Live(double stamp,long seq,bool complete,params JArray[] records)
                {
                    var batch=new JArray();
                    foreach(var row in records)batch.Add(row);
                    var packet=PNActionWire.Pack(stream,request,2,stamp,seq,batch,false).Single();
                    packet.CompleteThroughCapture=complete;
                    var roundtrip=Copy(packet);
                    Check(roundtrip.CompleteThroughCapture==complete,"coverage bit roundtrip");
                    Check(receiver.Receive(roundtrip,stamp),"apply live");
                }
                var unknown=PNActionPresentationCoverage.Unknown;
                var empty=PNActionPresentationCoverage.Empty;
                var active=PNActionPresentationCoverage.Active;
                PNActionPresentationCoverage At(double stamp)=>receiver.ReadPresentation(stamp,rows);
                Check(At(0)==unknown,"uncommitted");
                Baseline(1,0,new JArray());
                Check(At(.99)==unknown&&At(1)==empty&&At(1.01)==unknown,"initial cut");
                Live(2.5,2,false,new JArray(0,1,Key(1),Context(2)),
                    new JArray(2,2,Key(1),1,2,new JArray(2d,0d,2d)));
                Check(At(2)==unknown&&rows.Count==0,"partial prefix does not certify");
                Live(3.5,7,true,new JArray(0,3,Key(2),Context(2.25)),
                    new JArray(2,4,Key(2),1,2,new JArray(2.25,0d,1d)),
                    new JArray(2,5,Key(1),1,3,new JArray(2.5,1d,0d)),
                    new JArray(4,6,Key(2),3,2,3d,.75,1),
                    new JArray(2,7,Key(1),1,4,new JArray(3.5,1d,2d)));
                Check(At(1.99)==empty,"known empty before start");
                Check(At(2.375)==active&&rows.Count==2&&rows.Single(r=>r.Key.Ordinal==1).Progress==.75,"overlap at selected stamp");
                Check(At(2.99)==active&&rows.Count==2,"cancel already received remains visible before end");
                Check(At(3)==active&&rows.Count==1&&rows[0].Progress==1&&rows[0].Rate==0,"half-open end and hold");
                Check(At(3.5)==active&&rows[0].Rate==2&&At(3.51)==unknown,"resume boundary and no extrapolation");
                stream.Subscription++;request++;
                receiver.Reset(stream,request,10);
                Check(At(3.25)==active,"same-life recovery preserves covered pose");
                Baseline(4,7,new JArray{new JArray(Key(1),Context(2),5,1,2d,2d)},3.25);
                Check(At(3.25)==active&&rows[0].Progress==1&&At(3.75)==unknown,"early baseline preserves only known pre-cut data");
                Check(At(4)==active&&rows.Count==1&&rows[0].Progress==2,"exact cut selects baseline for same run");
                Live(4.5,7,true,new JArray(1,Key(1),6,4.5,3d,2d,1,7));
                Check(At(4.25)==active&&rows[0].Progress==2.5,"post-cut seek");
                Live(5,8,true,new JArray(3,8,Key(1),2,7,1,new JArray(4.75,3.5,0d)));
                Check(At(4.5)==unknown&&rows.Count==0,"reset lost history");
                Live(9,9,true,new JArray(4,9,Key(1),8,1,5d,3.5,2));
                Check(At(5)==unknown&&At(6)==empty,"retention floor hides pruned terminal history");
                stream.Subscription++;stream.Generation++;request++;
                receiver.Reset(stream,request,12);
                Check(At(6)==unknown&&budget.Used==0,"new life releases old coverage and charges");
            }
            Check(budget.Used==0,"dispose charge");
            return $"PASS {checks} Action presentation checks";
        }

        public static string Run(string folder)
        {
            int checks=0;void C(bool ok,string why){if(!ok)throw new Exception("Action B "+why);checks++;}
            void Reject(Action a,string why){try{a();}catch{C(true,why);return;}throw new Exception("Expected rejection "+why);}
            var logicalBytes=typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState").GetMethod("Bytes");
            foreach(var value in new JToken[]{new JArray(long.MinValue,long.MaxValue),new JArray("test:e","test:d","test:a",1,0d,-1d,1d),new JArray(0d,-0d,double.Epsilon,double.MaxValue,1e-300),new JArray("quote\"", "back\\slash", "\u1ebf", "\n",new JArray(1,2d))})
                C((int)logicalBytes.Invoke(null,new object[]{value})==System.Text.Encoding.UTF8.GetByteCount(value.ToString(Newtonsoft.Json.Formatting.None)),"logical JSON byte accounting parity preserves integer/float/escaped boundaries");
            var canonical=typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState").GetMethod("Canonical");
            foreach(var value in new JToken[]{new JArray(long.MinValue,long.MaxValue),new JArray("test:e","test:d","test:a",1,0d,-1d,1d),new JArray(0d,-0d,double.Epsilon,double.MaxValue,1e-300,0.1f,0.1m),new JArray("quote\"", "back\\slash", "\u1ebf", "\n",new JArray(1,2d))})
                C((string)canonical.Invoke(null,new object[]{value})==value.ToString(Newtonsoft.Json.Formatting.None),"duplicate canonical text exactly matches original JSON writer");
            // Independent slow oracle for the incremental logical accounting.
            var decoderType=typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState+Decoder");
            object decoder=Activator.CreateInstance(decoderType,new object[]{0,0});
            int Charge(object target)
            {
                var runs=(System.Collections.IDictionary)decoderType.GetField("Runs").GetValue(target);int n=0;
                foreach(var value in runs.Values){var rt=value.GetType();n+=(int)rt.GetField("BaseCharge").GetValue(value)+((System.Collections.ICollection)rt.GetField("Anchors").GetValue(value)).Count*64;}
                var duplicates=(System.Collections.IDictionary)decoderType.GetField("duplicates",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
                foreach(string value in duplicates.Values)n+=64+value.Length*2;
                C((int)decoderType.GetProperty("ResidentCharge").GetValue(target)==n,"incremental charge matches independently enumerated model");return n;
            }
            void Apply(object target,JArray row){decoderType.GetMethod("Apply").Invoke(target,new object[]{row,true});Charge(target);}
            var runKey=new JArray(7,1);var ctx=new JArray("test:e","test:d","test:a",1,0d,1d,1d);
            Apply(decoder,new JArray(0,1,runKey.DeepClone(),ctx));
            Apply(decoder,new JArray(2,2,runKey.DeepClone(),1,2,new JArray(1d,0d,1d)));
            object branch=decoderType.GetMethod("Clone").Invoke(decoder,null);int oldCharge=Charge(decoder);
            Apply(branch,new JArray(1,runKey.DeepClone(),3,2d,1d,1d,1,2));
            C(Charge(decoder)==oldCharge,"U branch leaves parent accounting unchanged");
            Apply(branch,new JArray(3,3,runKey.DeepClone(),2,4,1,new JArray(2d,1d,0d)));
            C(Charge(decoder)==oldCharge,"required branch copy leaves parent duplicates unchanged");
            Apply(branch,new JArray(4,4,runKey.DeepClone(),5,1,3d,1d,2));
            decoderType.GetMethod("Prune").Invoke(branch,new object[]{10d});Charge(branch);C(Charge(decoder)==oldCharge,"pruning branch leaves parent accounting unchanged");
            object trimming=decoderType.GetMethod("Clone").Invoke(decoder,null);
            Apply(trimming,new JArray(2,3,runKey.DeepClone(),1,3,new JArray(2d,1d,0d)));
            decoderType.GetMethod("Prune").Invoke(trimming,new object[]{5.5d});Charge(trimming);
            C(Charge(decoder)==oldCharge,"active anchor trimming leaves parent unchanged");
            for(int index=4;index<=300;index++)Apply(trimming,new JArray(2,index,runKey.DeepClone(),1,index,new JArray(2d+index,1d,0d)));
            Charge(trimming);C(Charge(decoder)==oldCharge,"duplicate window rotation leaves parent unchanged");
            var emptyScope=new JArray(new string('1',32),new string('2',32),1,1,1,new string('3',32));
            decoderType.GetMethod("Baseline").Invoke(branch,new object[]{new JArray(2,emptyScope,1,10d,4,new JArray()),2L});C(Charge(branch)==0,"empty baseline clears cached charge");
            foreach(var v in new[]{new JArray(),new JArray(long.MinValue,long.MaxValue,0,0d,1e300,new string('x',384)),new JArray{new JArray("\u1ebf",new JArray(1,2.5))}})
                C(PNActionWire.EncodedSize(v)==PNActionWire.Encode(v).Length,"allocation-free exact wire size");
            Reject(()=>PNActionWire.EncodedSize(new JArray(double.NaN)),"size rejects nonfinite");
            Reject(()=>PNActionWire.EncodedSize(new JArray(new string('x',385))),"size rejects oversized string");
            using(var scalarSource=new PNActionPublication())using(var scalarReader=new PNActionPublication.Subscription(scalarSource))
            {
                var key=new PNActionRunKey(long.MinValue,long.MaxValue);
                scalarSource.Begin(key,"test:e","test:d","test:a",0,-1,1,2.5);scalarSource.Advance(key,1,2,1,1);scalarSource.Advance(key,2,2,.25,1);scalarSource.Advance(key,2,2,.25,1);scalarSource.End(key,2,(PNActionRunEnd)1);
                var records=scalarReader.ReadRequired(3000,256);
                C(new[]{0,2,3,4}.All(kind=>records.Any(row=>row.RecordKind==kind)),"required parity fixture covers S/A/R/E");
                var stream=new PNActionStream{Session=Guid.NewGuid(),Source=scalarSource.Source,NetId=4,Entity=5,Generation=6,Subscription=1,Nonce=7,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
                var sizeMethod=typeof(PNActionWire).GetMethod("RequiredSize",BindingFlags.Static|BindingFlags.NonPublic);var packMethod=typeof(PNActionWire).GetMethod("PackRequired",BindingFlags.Static|BindingFlags.NonPublic);
                var expected=new JArray();
                for(int index=0;index<records.Count;index++)
                {
                    var row=records[index];var array=row.ToArray();C((string)canonical.Invoke(null,new object[]{array})==array.ToString(Newtonsoft.Json.Formatting.None),"all required kinds retain exact duplicate canonical text");C((int)sizeMethod.Invoke(null,new object[]{row})==PNActionWire.EncodedSize(array),"direct required exact size equals independent tree size");expected.Add(array);
                    var packets=(PNActionPacket[])packMethod.Invoke(null,new object[]{stream,1L,1L,2d,row.N,records,index+1});
                    C(packets.Length==1&&packets[0].Body.SequenceEqual(PNActionWire.Encode(expected)),"direct required prefix byte parity all record kinds and signed keys");C(Copy(packets[0]).Body.SequenceEqual(packets[0].Body),"direct required full Mirror roundtrip");
                }
                C(typeof(PNActionPublication.Required).GetProperty("RecordKind").SetMethod==null&&typeof(PNActionPublication.Required).GetProperty("EntryId").SetMethod==null,"frozen required scalar view has no setter");
                Reject(()=>packMethod.Invoke(null,new object[]{stream,1L,1L,2d,scalarSource.Sequence,records,257}),"direct required record count bound");
            }
            using(var source=new PNActionPublication())
            {
                var s=new PNActionStream{Session=Guid.NewGuid(),Source=source.Source,NetId=4,Entity=5,Generation=6,Subscription=1,Nonce=7,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=1,Subscription=1}};
                var scope=new JArray(s.Session.ToString("N"),s.Scope.World.ToString("N"),1,5,6,source.Source.ToString("N"));
                for(int i=1;i<=64;i++)source.Begin(new PNActionRunKey(1,i),"test:e","test:d","test:a",1,0,1);
                using(var latestReader=new PNActionPublication.Subscription(source))
                {
                    latestReader.Reset(source.Sequence);var latestRows=latestReader.ReadLatest(1,int.MaxValue);
                    var packLatest=typeof(PNActionWire).GetMethod("PackLatest",BindingFlags.Static|BindingFlags.NonPublic);
                    foreach(int count in new[]{1,16,32})
                    {
                        var selected=latestRows.Take(count).ToArray();var expected=new JArray();
                        foreach(var value in selected)expected.Add(new JArray(1,new JArray(value.Key.Cast,value.Key.Ordinal),value.Revision,value.Stamp,value.Progress,value.Rate,value.HistoryGeneration,value.NeedSequence));
                        var actual=(PNActionPacket[])packLatest.Invoke(null,new object[]{s,1L,1L,1d,source.Sequence,selected});
                        C(actual.Length==1&&actual[0].Body.SequenceEqual(PNActionWire.Encode(expected)),"direct latest encoding byte parity including numeric token types");
                        C(Copy(actual[0]).Body.SequenceEqual(actual[0].Body),"direct latest full Mirror packet roundtrip");
                        var decodeLatest=typeof(PNActionWire).GetMethod("DecodeLatest",BindingFlags.Static|BindingFlags.NonPublic);
                        var decoded=(Array)decodeLatest.Invoke(null,new object[]{actual[0].Body});C(decoded.Length==selected.Length,"compact U decode count");
                        for(int item=0;item<selected.Length;item++)
                        {
                            object value=decoded.GetValue(item);var type=value.GetType();var original=selected[item];
                            C((long)type.GetField("Cast").GetValue(value)==original.Key.Cast&&(long)type.GetField("Ordinal").GetValue(value)==original.Key.Ordinal&&(long)type.GetField("Revision").GetValue(value)==original.Revision&&(long)type.GetField("HistoryGeneration").GetValue(value)==original.HistoryGeneration&&(long)type.GetField("NeedSequence").GetValue(value)==original.NeedSequence&&(double)type.GetField("Stamp").GetValue(value)==original.Stamp&&(double)type.GetField("Progress").GetValue(value)==original.Progress&&(double)type.GetField("Rate").GetValue(value)==original.Rate,"compact U decode equals independent tree/scalar oracle");
                        }
                        var nan=(byte[])actual[0].Body.Clone();Buffer.BlockCopy(BitConverter.GetBytes(double.NaN),0,nan,52,8);
                        Reject(()=>decodeLatest.Invoke(null,new object[]{nan}),"compact U rejects NaN before model apply");
                        Reject(()=>decodeLatest.Invoke(null,new object[]{actual[0].Body.Concat(new byte[]{0}).ToArray()}),"compact U rejects trailing data");
                        var integerNumbers=(JArray)expected.DeepClone();integerNumbers[0][3]=new JValue(1L);C(((Array)decodeLatest.Invoke(null,new object[]{PNActionWire.Encode(integerNumbers)})).Length==count,"compact U accepts integer numeric token");
                        var mixed=(JArray)expected.DeepClone();mixed.Add(new JArray(4,source.Sequence+1,new JArray(1,1),1,1,1d,0d,1));C(decodeLatest.Invoke(null,new object[]{PNActionWire.Encode(mixed)})==null,"mixed U/required preserves full tree decoder fallback");

                    }
                    var compact=typeof(PNActionWire).GetMethod("DecodeLatest",BindingFlags.Static|BindingFlags.NonPublic);
                    var recordCheck=typeof(PNActionReceiver).Assembly.GetType("ProjectN.Coop.PNActionState").GetMethod("Record");
                    bool Accepted(JArray row,bool fast)
                    {
                        try{var bytes=PNActionWire.Encode(new JArray{row});if(fast&&compact.Invoke(null,new object[]{bytes})!=null)return true;foreach(JArray value in PNActionWire.Decode(bytes))recordCheck.Invoke(null,new object[]{value});return true;}catch{return false;}
                    }
                    var validLatest=new JArray(1,new JArray(1,1),1,1d,0d,0d,1,source.Sequence);C(Accepted(validLatest,true)&&Accepted(validLatest,false),"both U validation oracles accept known valid input");
                    foreach(int field in new[]{0,2,3,4,5,6,7})foreach(object scalar in new object[]{-1L,0L,1L,long.MinValue,long.MaxValue,.5d,double.Epsilon,double.MaxValue,"invalid"})
                    {
                        var row=new JArray(1,new JArray(1,1),1,1d,0d,0d,1,source.Sequence);row[field]=JToken.FromObject(scalar);C(Accepted(row,true)==Accepted(row,false),"compact U scalar validation agrees with original tree grammar");
                    }
                    foreach(int keyField in new[]{0,1})foreach(object scalar in new object[]{-1L,0L,1L,long.MinValue,long.MaxValue,.5d,"invalid"})
                    {
                        var row=new JArray(1,new JArray(1,1),1,1d,0d,0d,1,source.Sequence);row[1][keyField]=JToken.FromObject(scalar);C(Accepted(row,true)==Accepted(row,false),"compact U key validation agrees with original grammar");
                    }
                    Reject(()=>packLatest.Invoke(null,new object[]{s,1L,1L,1d,source.Sequence,latestRows.Take(33).ToArray()}),"direct latest count bound");
                    var sample=latestRows[0];var oldKey=sample.Key;
                    sample.GetType().GetProperty("Key").SetValue(sample,new PNActionRunKey(long.MinValue,long.MaxValue));
                    var extreme=(PNActionPacket[])packLatest.Invoke(null,new object[]{s,1L,1L,1d,source.Sequence,new[]{sample}});
                    C((long)PNActionWire.Decode(extreme[0].Body)[0][1][0]==long.MinValue&&(long)PNActionWire.Decode(extreme[0].Body)[0][1][1]==long.MaxValue,"direct latest signed cast/ordinal extremes");
                    sample.GetType().GetProperty("Key").SetValue(sample,oldKey);
                    sample.GetType().GetProperty("Rate").SetValue(sample,double.NaN);
                    Reject(()=>packLatest.Invoke(null,new object[]{s,1L,1L,1d,source.Sequence,new[]{sample}}),"direct latest nonfinite rejection");
                }
                using(var lease=source.Capture(scope,1))
                {
                    var b=JArray.Parse(lease.Json);var packets=PNActionWire.Pack(s,1,(long)b[2],1,source.Sequence,b,true);C(packets.Length>1,"multipart baseline");
                    using(var atomic=new PNActionReceiver(new PNActionReceiveBudget(),s,1,4))
                    {
                        foreach(var packet in packets)atomic.Receive(packet,1);C(atomic.TryApply(1).HasValue,"compact rollback fixture baseline");
                        var updates=new JArray(new JArray(1,new JArray(1,1),2,2d,0d,0d,1,source.Sequence),new JArray(1,new JArray(1,2),2,2d,0d,0d,1,source.Sequence));
                        var bad=PNActionWire.Pack(s,1,2,2,source.Sequence,updates,false)[0];Buffer.BlockCopy(BitConverter.GetBytes(double.NaN),0,bad.Body,143,8);bad.Digest=PNActionWire.Hash(bad.Body);
                        C(!atomic.Receive(bad,2)&&atomic.State==PNActionReceiveState.NeedsRecovery,"late invalid compact U fails whole packet");
                        var prior=typeof(PNActionReceiver).GetField("model",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(atomic);
                        var priorRuns=(System.Collections.IDictionary)decoderType.GetField("Runs").GetValue(prior);var priorRun=priorRuns["1:1"];
                        C((double)priorRun.GetType().GetField("T").GetValue(priorRun)==1&&atomic.AppliedSequence==source.Sequence,"compact U never publishes valid prefix before later error");
                    }
                    var budget=new PNActionReceiveBudget();using(var receiver=new PNActionReceiver(budget,s,1,4))
                    {
                        foreach(var p in packets.Reverse())C(receiver.Receive(Copy(p),1),"fragment roundtrip");C(receiver.RunCount==0&&receiver.State==PNActionReceiveState.ReadyToApply,"receive not apply");
                        using(var peer=new PNActionPublication.Subscription(source))
                        {
                            peer.Reset(source.Sequence);source.Advance(new PNActionRunKey(1,1),1,2,1,1);
                            var rows=new JArray(peer.ReadRequired(3000,8).Select(x=>JArray.Parse(x.Json)));var live=PNActionWire.Pack(s,1,2,2,source.Sequence,rows,false)[0];
                            C(receiver.Receive(Copy(live),2),"bounded post-cut required");var ack=receiver.TryApply(2);C(ack.HasValue&&receiver.RunCount==64&&receiver.Progress(1,1)==0,"baseline then rate anchor");
                            C(PNActionTransport.MatchesApplied(packets[0],Copy(ack.Value)),"exact actual apply ACK");var stale=ack.Value;stale.Request++;C(!PNActionTransport.MatchesApplied(packets[0],stale),"stale ACK");
                            peer.CommitRequiredSubmitted(source.Sequence,peer.Generation);rows=new JArray(peer.ReadLatest(2,3000).Take(1).Select(x=>JArray.Parse(x.Json)));live=PNActionWire.Pack(s,1,3,2,source.Sequence,rows,false)[0];C(receiver.Receive(Copy(live),2)&&receiver.Progress(1,1)==1&&receiver.LastLiveCommit.Latest==1,"live U oracle and committed outcome");
                            source.End(new PNActionRunKey(1,1),2,PNActionRunEnd.Completed);rows=new JArray(peer.ReadRequired(3000,8).Select(x=>JArray.Parse(x.Json)));live=PNActionWire.Pack(s,1,4,2,source.Sequence,rows,false)[0];C(receiver.Receive(Copy(live),2)&&receiver.LastLiveCommit.Required>0,"terminal FIFO and committed outcome");C(receiver.Receive(Copy(live),2)&&receiver.LastLiveCommit.Required==0&&receiver.LastLiveCommit.Ignored>0,"required replay reports ignored");
                            var other=live;other.Stream.Subscription++;C(!receiver.Receive(other,2),"old scope no resurrection");
                        }
                    }C(budget.Used==0,"all receiver charges released");
                    using(var r=new PNActionReceiver(budget,s,1,4)){r.Receive(Copy(packets[0]),1);r.Tick(4);C(r.State==PNActionReceiveState.Failed&&budget.Used==0,"original empty-queue deadline");}
                    using(var r=new PNActionReceiver(budget,s,1,4)){r.Receive(Copy(packets[0]),1);var bad=Copy(packets[0]);bad.Body[0]^=1;C(!r.Receive(bad,1)&&r.State==PNActionReceiveState.NeedsRecovery,"duplicate conflict");}
                    using(var r=new PNActionReceiver(budget,s,1,4)){r.Receive(Copy(packets[0]),1);var bad=Copy(packets[1]);bad.Q++;C(!r.Receive(bad,1),"mixed cut rejected");}
                    using(var r=new PNActionReceiver(budget,s,1,4)){foreach(var p in packets){var bad=Copy(p);bad.Digest[0]^=1;r.Receive(bad,1);}C(!r.TryApply(1).HasValue&&r.RunCount==0,"digest failure atomic");}
                    var tiny=new PNActionReceiveBudget(1);using(var r=new PNActionReceiver(tiny,s,1,4)){C(!r.Receive(Copy(packets[0]),1)&&tiny.Used==0,"shared staging bound");}
                    Reject(()=>PNActionWire.Decode(new byte[]{0,255,255,255,127}),"malformed count");Reject(()=>PNActionWire.Encode(new JArray(double.NaN)),"nonfinite");
                    var oversized=packets[0];oversized.Total=int.MaxValue;Reject(()=>Copy(oversized),"oversized header");
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {foreach(var p in packets)r.Receive(Copy(p),1);r.TryApply(1);var gap=new JArray{new JArray(4,source.Sequence+10,new JArray(1,2),99,1,2,0,1)};var gapPacket=PNActionWire.Pack(s,1,5,2,source.Sequence+10,gap,false)[0];C(!r.Receive(gapPacket,2)&&r.RunCount==64&&r.Failure=="required gap","gap rollback keeps old model");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {foreach(var p in packets)r.Receive(Copy(p),1);r.TryApply(1);long cut=(long)b[4];var mixed=new JArray{
                        new JArray(2,cut+1,new JArray(1,2),1,2,new JArray(1,0,1)),
                        new JArray(1,new JArray(1,2),3,2,1,1,1,cut+1),
                        new JArray(4,cut+99,new JArray(1,2),4,1,2,1,1)};
                     C(!r.Receive(PNActionWire.Pack(s,1,5,2,cut+99,mixed,false)[0],2)&&r.Failure=="required gap"&&r.Progress(1,2)==0&&r.AppliedSequence==cut&&r.LastLiveCommit==default,"late batch failure rolls back earlier valid anchor and progress");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {foreach(var p in packets)r.Receive(Copy(p),1);r.TryApply(1);
                     var dep=PNActionWire.Pack(s,1,2,2,source.Sequence,new JArray{new JArray(1,new JArray(1,2),2,2,0,0,1,999)},false)[0];
                     C(!r.Receive(dep,2)&&r.Failure=="state dependency"&&r.Progress(1,2)==0,"U cannot bypass dependency; rollback");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {foreach(var p in packets)r.Receive(Copy(p),1);r.TryApply(1);var next=s;next.Subscription++;
                     r.Reset(next,2,5);C(r.RunCount==64&&r.State==PNActionReceiveState.Waiting,"reset keeps prior model until atomic swap");
                     C(!r.Receive(Copy(packets[0]),2),"old fragment ignored after subscription reset");
                     var empty=new JArray(2,b[1].DeepClone(),2,2,source.Sequence,new JArray());var parts2=PNActionWire.Pack(next,2,2,2,source.Sequence,empty,true);
                     foreach(var p in parts2)r.Receive(Copy(p),2);C(r.RunCount==64,"empty baseline held before apply");C(r.TryApply(2).HasValue&&r.RunCount==0,"complete empty atomically retires old model");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {var wrong=(JArray)b.DeepClone();wrong[1][5]=Guid.NewGuid().ToString("N");foreach(var p in PNActionWire.Pack(s,1,(long)b[2],1,(long)b[4],wrong,true))r.Receive(Copy(p),1);C(!r.TryApply(1).HasValue&&r.RunCount==0,"authenticated envelope cannot mix baseline body scope");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {var next=s;next.Subscription++;r.Reset(next,2,99);C(r.Deadline==4,"reset before completion retains original deadline");r.Tick(4);C(r.State==PNActionReceiveState.Failed,"reset timeout with zero packets");}
                    using(var r=new PNActionReceiver(budget,s,1,4))
                    {foreach(var p in packets)r.Receive(Copy(p),1);r.TryApply(1);var gap2=PNActionWire.Pack(s,1,2,10,999,new JArray{new JArray(4,999,new JArray(1,2),99,1,10,0,1)},false)[0];
                     r.Receive(gap2,10);C(r.Deadline==13&&r.State==PNActionReceiveState.NeedsRecovery,"new prepared-stream failure starts a new bounded episode");var next=s;next.Subscription++;r.Reset(next,2,12);C(r.Deadline==12,"actual remaining recovery budget shortens episode");r.Tick(12);C(r.State==PNActionReceiveState.Failed,"new episode timeout");}
                    C(budget.Used==0,"all negative-case staging returned");
                    Loopback(packets,C);
                }
            }
            var text="PASS "+checks+" Action B wire/receiver/Mirror loopback checks";Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"result.txt"),text);return text;
        }
        static void Loopback(PNActionPacket[] packets,Action<bool,string> check)
        {
            if(NetworkServer.active||NetworkClient.active)throw new Exception("Mirror already active");
            var oldInfo=kcp2k.Log.Info;var oldWarning=kcp2k.Log.Warning;var oldError=kcp2k.Log.Error;
            var previous=Transport.active;var go=new GameObject("Action B disposable transport");go.hideFlags=HideFlags.HideAndDontSave;
            var handlerFlags=BindingFlags.Static|BindingFlags.NonPublic;
            var serverMap=(System.Collections.IDictionary)typeof(NetworkServer).GetField("handlers",handlerFlags).GetValue(null);
            var clientMap=(System.Collections.IDictionary)typeof(NetworkClient).GetField("handlers",handlerFlags).GetValue(null);
            var serverHandlers=serverMap.Cast<System.Collections.DictionaryEntry>().ToArray();var clientHandlers=clientMap.Cast<System.Collections.DictionaryEntry>().ToArray();
            PNActionTransport endpoint=null;var budget=new PNActionReceiveBudget();
            using(var receiver=new PNActionReceiver(budget,packets[0].Stream,packets[0].Request,4))
            try
            {
                var transport=go.AddComponent<kcp2k.KcpTransport>();transport.Port=0;typeof(kcp2k.KcpTransport).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(transport,null);Transport.active=transport;NetworkServer.Listen(1);NetworkClient.ConnectHost();HostMode.InvokeOnConnected();
                NetworkServer.localConnection.isAuthenticated=true;NetworkClient.connection.isAuthenticated=true;
                int got=0,acks=0;var receipt=new PNActionBaselineReceipt(packets[0],4);
                endpoint=new PNActionTransport((c,s)=>c.isAuthenticated&&s.Equals(packets[0].Stream),(c,p)=>{if(receipt.Confirm(p,1))acks++;},s=>s.Equals(packets[0].Stream),p=>{got++;receiver.Receive(p,1);});
                foreach(var p in packets)NetworkServer.localConnection.Send(p,Channels.Reliable);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(LocalConnectionToServer).GetMethod("Update",flags).Invoke(NetworkClient.connection,null);
                check(got==packets.Length&&acks==0,"real Mirror handlers receive; ACK held before apply");
                var ack=receiver.TryApply(1);check(ack.HasValue,"Mirror baseline actual apply");NetworkClient.Send(ack.Value,Channels.Reliable);typeof(LocalConnectionToClient).GetMethod("Update",flags).Invoke(NetworkServer.localConnection,null);check(acks==1&&receipt.Applied,"Mirror actual-apply ACK return");check(!receipt.Confirm(ack.Value,1),"duplicate ACK is idempotent");
                var live=PNActionWire.Pack(packets[0].Stream,packets[0].Request,2,1.25,packets[0].N+1,new JArray{new JArray(0,packets[0].N+1,new JArray(2,1),new JArray("test:e","test:d","test:a",1,1,0,1.25))},false)[0];
                NetworkServer.localConnection.Send(live,Channels.Reliable);typeof(LocalConnectionToServer).GetMethod("Update",flags).Invoke(NetworkClient.connection,null);check(receiver.RunCount==65&&receiver.AppliedSequence==live.N,"Mirror live required after baseline got="+got+" runs="+receiver.RunCount+" n="+receiver.AppliedSequence+" error="+receiver.Failure);
            }
            finally
            {endpoint?.Dispose();try{NetworkClient.Shutdown();}finally{NetworkServer.Shutdown();}serverMap.Clear();foreach(var p in serverHandlers)serverMap[p.Key]=p.Value;clientMap.Clear();foreach(var p in clientHandlers)clientMap[p.Key]=p.Value;Transport.active=previous;kcp2k.Log.Info=oldInfo;kcp2k.Log.Warning=oldWarning;kcp2k.Log.Error=oldError;UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
#endif
