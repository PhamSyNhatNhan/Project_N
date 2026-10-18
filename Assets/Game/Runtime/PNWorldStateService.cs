#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class PNWorldStateRecord
{
    public string Json { get; }
    public int Bytes { get; }
    public PNWorldStateRecord(string json)
    {
        if(json==null||Encoding.UTF8.GetByteCount(json)>64*1024*1024)throw new FormatException("State exceeds 64 MiB.");
        JToken.Parse(json,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Json=json;Bytes=Encoding.UTF8.GetByteCount(json);
    }
    public JArray Read()=>(JArray)JToken.Parse(Json,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
}
// Provider lifetime remains owned even when extension cleanup throws.
public sealed class PNWorldStateService : IDisposable
{
    private readonly EveSessionContext session;
    private readonly HashSet<IPNWorldStateProvider> providers=new HashSet<IPNWorldStateProvider>();
    public int PendingCleanup=>providers.Count;
    public PNWorldStateService(EveSessionContext session){this.session=session;session.RegisterCleanup(Dispose);}
    private T Invoke<T>(EveId id,Func<PNWorldStateDefinition,IPNWorldStateProvider,T> run)
    {
        var definition=session.Registry.Get<PNWorldStateDefinition>().Get(id);var provider=definition.Create();providers.Add(provider);
        try{return run(definition,provider);}finally{provider.Dispose();providers.Remove(provider);}
    }
    public PNWorldStateRecord Capture(PNWorldStateContext context,IReadOnlyList<EveId> ids)
    {
        if(!ReferenceEquals(context.Session,session)||!session.HasAuthority)throw new InvalidOperationException("State capture requires play authority.");
        var result=new JArray();long total=0;
        foreach(var id in ids)
        {
            var entry=Invoke(id,(definition,provider)=>{var data=provider.Capture(context)??throw new InvalidOperationException("Provider omitted required state.");provider.Validate(context,data);string json=data.ToString(Formatting.None);int bytes=Encoding.UTF8.GetByteCount(json);if(bytes>definition.MaximumBytes)throw new InvalidOperationException("Provider state exceeds its byte bound.");return new JObject{["id"]=id.ToString(),["schema"]=definition.Schema,["data"]=data.DeepClone()};});
            total+=Encoding.UTF8.GetByteCount(entry.ToString(Formatting.None));if(total>64*1024*1024)throw new InvalidOperationException("World state bound exceeded.");result.Add(entry);
        }
        return new PNWorldStateRecord(result.ToString(Formatting.None));
    }
    public void Validate(PNWorldStateContext context,PNWorldStateRecord record,IReadOnlyList<EveId> ids)=>Apply(context,record,ids,false);
    public void Restore(PNWorldStateContext context,PNWorldStateRecord record,IReadOnlyList<EveId> ids)=>Apply(context,record,ids,true);
    private void Apply(PNWorldStateContext context,PNWorldStateRecord record,IReadOnlyList<EveId> ids,bool restore)
    {
        if(!ReferenceEquals(context.Session,session)||!session.HasAuthority)throw new InvalidOperationException("State restoration requires play authority.");
        var values=record.Read();if(values.Count!=ids.Count||values.Select(x=>(string)x["id"]).Distinct().Count()!=values.Count||!values.Select(x=>EveId.Parse((string)x["id"])).SequenceEqual(ids))throw new FormatException("Required selected serializers differ.");
        foreach(var entry in values)
        {
            var id=EveId.Parse((string)entry["id"]);Invoke(id,(definition,provider)=>{if(definition.Schema!=(int)entry["schema"]||Encoding.UTF8.GetByteCount(entry["data"].ToString(Formatting.None))>definition.MaximumBytes)throw new FormatException("Serializer schema/bound differs.");provider.Validate(context,entry["data"]);return true;});
        }
        if(restore)foreach(var entry in values)Invoke(EveId.Parse((string)entry["id"]),(definition,provider)=>{provider.Restore(context,entry["data"]);return true;});
    }
    public void Dispose(){foreach(var provider in providers.ToArray()){provider.Dispose();providers.Remove(provider);}}
}
// Restoration runs inside the loader's module-access scope, before publication.
public sealed class PNWorldRestoreStage : UnityEngine.MonoBehaviour,EveIEntityCommitStep
{
    public Action<EveEntityLoadContext> Restore;
    public void CommitEntity(EveEntityLoadContext context){var restore=Restore;Restore=null;restore?.Invoke(context);}
    public void EntityPublished(EveEntityLoadContext context) { }
}
#endif
