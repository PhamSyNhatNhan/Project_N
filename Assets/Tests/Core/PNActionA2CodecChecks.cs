// Fixture-only native JSON codec and bounded-send simulation. Not registered on wire.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
public static class PNActionA2CodecChecks {
 public static string Run(string folder) {
  int records=0,max=0,parts=0,used=256,queueBytes=0,peakQueue=0,sent=0;long bytes=0;double next=0,oldest=0;long sequence=0;
  var lifecycle=new Queue<(double time,int size)>();var states=new Dictionary<string,(double time,int size)>();var history=new Dictionary<string,JToken>();
  var activeImage=new Dictionary<string,JToken>();JArray frozen=null;
  void Flush(double now) {if(next==0)next=now;while(next<=now){if(next<2||next>=4){int available=16384;while(lifecycle.Count>0&&lifecycle.Peek().size<=available){var p=lifecycle.Dequeue();available-=p.size;queueBytes-=p.size;oldest=Math.Max(oldest,next-p.time);sent++;}foreach(var pair in states.OrderBy(p=>p.Value.time).ToArray()){if(pair.Value.size>available)break;available-=pair.Value.size;queueBytes-=pair.Value.size;oldest=Math.Max(oldest,next-pair.Value.time);states.Remove(pair.Key);sent++;}}next+=.05;}}
  void Encode(string stream,string key,JToken body,double stamp){var packet=new JObject{["schema"]=1,["stream"]=stream,["scope"]=1,["sequence"]=++sequence,["body"]=body};string text=packet.ToString(Formatting.None);if(!JToken.DeepEquals(packet,JObject.Parse(text)))throw new Exception("Candidate roundtrip");int size=System.Text.Encoding.UTF8.GetByteCount(text);if(size>3840)throw new Exception("Record exceeds cap "+size);if(used+size+1>4096){parts++;used=256;}used+=size+1;records++;bytes+=size;max=Math.Max(max,size);if(stream=="state"){if(states.TryGetValue(key,out var prior)){queueBytes-=prior.size;states[key]=(prior.time,size);}else states[key]=(stamp,size);}else lifecycle.Enqueue((stamp,size));queueBytes+=size;peakQueue=Math.Max(peakQueue,queueBytes);if(queueBytes>4*1024*1024)throw new Exception("Queue exceeded experiment budget");}
  foreach(var line in File.ReadLines(Path.Combine(folder,"native-a2.jsonl"))) {
   var e=JObject.Parse(line);if(e["error"]!=null)throw new Exception("Invalid source dataset");double stamp=(double)e["stamp"];Flush(stamp);var row=e["reconstructed"] as JArray;string kind=(string)e["kind"],key=e["source"]+":"+e["cast"]+":"+e["ordinal"];
   if(frozen==null && stamp>=2)frozen=new JArray(activeImage.Values.Select(x=>x.DeepClone()));
   if(kind=="attempt")activeImage[key]=e["candidate"].DeepClone();
   if(row!=null)activeImage[key]=row.DeepClone();
   if(kind=="finish-attempt")activeImage.Remove(key);
   if(kind=="attempt"){var start=(JArray)e["candidate"];Encode("start",key,new JArray(e["source"],start[0],start[1],start[2],start[3],start[4],start[5],start[6],start[7],start[8]),stamp);}
   if(row==null)continue;
   if(kind=="finish-attempt")Encode("terminal",key,new JArray(e["source"],row[0],row[1],row[12],row[13],row[10],row[15]),stamp);
   else Encode("state",key,new JArray(e["source"],row[0],row[1],row[9],row[10],row[11],row[14],row[15]),stamp);
   if(!history.TryGetValue(key,out var old)||!JToken.DeepEquals(old,row[16])){history[key]=row[16].DeepClone();Encode("history",key,new JArray(e["source"],row[0],row[1],row[14],row[16].DeepClone()),stamp);}
  }
  Flush(next+60);if(lifecycle.Count!=0||states.Count!=0)throw new Exception("Queue did not drain");if(used>256)parts++;
  var maxId = "test:" + new string('a',91);
  PNActionRunWire.Id(maxId);
  var extreme = new JObject { ["id"] = maxId, ["integer"] = long.MaxValue, ["number"] = double.MaxValue };
  if(!JToken.DeepEquals(extreme,JObject.Parse(extreme.ToString(Formatting.None))))throw new Exception("Boundary codec roundtrip");
  if(frozen==null)frozen=new JArray(activeImage.Values.Select(x=>x.DeepClone()));
  var baselineParts=new List<JArray>();var part=new JArray();
  foreach(var record in frozen){part.Add(record.DeepClone());if(PNActionRunWire.Bytes(part)>3840){part.RemoveAt(part.Count-1);if(part.Count==0)throw new Exception("Baseline record too large");baselineParts.Add(part);part=new JArray(record.DeepClone());}}
  if(part.Count>0 || baselineParts.Count==0)baselineParts.Add(part);
  var received=new Dictionary<int,JArray>();var staged=new JArray();bool committed=false;
  for(int i=baselineParts.Count-1;i>=0;i--){received[i]=(JArray)baselineParts[i].DeepClone();received[i]=(JArray)baselineParts[i].DeepClone();if(received.Count==baselineParts.Count){for(int j=0;j<baselineParts.Count;j++)foreach(var item in received[j])staged.Add(item.DeepClone());committed=true;}else if(committed)throw new Exception("Partial baseline committed");}
  if(!committed || !JToken.DeepEquals(frozen,staged))throw new Exception("Atomic baseline assembly");
  var report=new JObject{["candidate"]="E1-state-lifecycle-history",["records"]=records,["totalBytes"]=bytes,["maxRecordBytes"]=max,["maxMessageBytes"]=4096,["envelopeReserve"]=256,["parts"]=parts,["sendBudgetPer50ms"]=16384,["stallSeconds"]=2,["peakQueueBytes"]=peakQueue,["oldestPendingSeconds"]=oldest,["sentRecords"]=sent,["roundtrip"]=true,["networkAcceptance"]=false};
  report["baselineParts"]=baselineParts.Count;report["baselineBytes"]=PNActionRunWire.Bytes(frozen);report["atomicAssemblyReversedDuplicates"]=true;
  File.WriteAllText(Path.Combine(folder,"encoder.json"),report.ToString());return report.ToString(Formatting.None);
 }
}
#endif
