#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EveEngine;
using UnityEngine;

// Content-generation owner, shared by successive plays; scenes/worlds own leases.
public sealed class PNWorldBundleService : IDisposable
{
    internal sealed class Entry
    {public EveBundleDescriptor Descriptor;public int References;public Task Verify;public AssetBundleCreateRequest Loading;public AssetBundle Bundle;public Exception Failure;}
    private readonly Dictionary<string,Entry> entries;
    private static readonly Dictionary<EveSessionContext,PNWorldBundleService> sessions=new Dictionary<EveSessionContext,PNWorldBundleService>();
    private bool closing;
    public int LeaseReferences=>entries.Values.Sum(x=>x.References);
    public int LoadedCount=>entries.Values.Count(x=>x.Bundle!=null);
    public PNWorldBundleService(IEnumerable<EveBundleDescriptor> descriptors)
    {
        entries=descriptors.ToDictionary(x=>x.Id,x=>new Entry{Descriptor=x},StringComparer.Ordinal);
        if(entries.Values.Any(x=>x.Descriptor.Target!="StandaloneWindows64"||x.Descriptor.UnityVersion!=Application.unityVersion)||entries.Count>0&&(Application.platform==RuntimePlatform.Android||Application.platform==RuntimePlatform.IPhonePlayer))throw new NotSupportedException("Selected bundle target/Unity build is incompatible.");
    }
    public void BindSession(EveSessionContext session)
    {if(closing)throw new ObjectDisposedException(nameof(PNWorldBundleService));sessions.Add(session,this);session.RegisterCleanup(()=>sessions.Remove(session),afterEntities:true);}
    public static PNWorldBundleService Resolve(EveSessionContext session)=>sessions.TryGetValue(session,out var service)?service:throw new InvalidOperationException("No selected bundle content attached to this play.");
    public PNWorldBundleLease Acquire(string id)
    {
        if(closing)throw new ObjectDisposedException(nameof(PNWorldBundleService));var ordered=new List<Entry>();var visiting=new HashSet<string>();
        void Visit(string key)
        {if(!entries.TryGetValue(key,out var entry)||!visiting.Add(key))throw new InvalidOperationException("Bundle dependency missing/cyclic: "+key);if(ordered.Contains(entry)){visiting.Remove(key);return;}foreach(var dependency in entry.Descriptor.Dependencies)Visit(dependency);ordered.Add(entry);visiting.Remove(key);}
        Visit(id);foreach(var entry in ordered)entry.References++;return new PNWorldBundleLease(this,ordered.ToArray());
    }
    internal bool Pump(Entry entry)
    {
        if(entry.Failure!=null)throw new InvalidOperationException("Bundle load failed: "+entry.Descriptor.Id,entry.Failure);
        try
        {
            if(entry.Bundle!=null)return true;
            if(entry.Verify==null)entry.Verify=Task.Run(entry.Descriptor.Verify);
            if(!entry.Verify.IsCompleted)return false;entry.Verify.GetAwaiter().GetResult();
            if(entry.Loading==null)entry.Loading=AssetBundle.LoadFromFileAsync(entry.Descriptor.Path);
            if(entry.Loading==null)throw new InvalidOperationException("Unity refused bundle load: "+entry.Descriptor.Id);
            if(!entry.Loading.isDone)return false;
            entry.Bundle=entry.Loading.assetBundle;if(entry.Bundle==null)throw new InvalidOperationException("Invalid or incompatible bundle: "+entry.Descriptor.Id);return true;
        }
        catch(Exception error){entry.Failure=error;throw;}
    }
    internal bool Drain(Entry entry)
    {
        // Even an abandoned acquisition owns its unavoidable async load until completion.
        if(entry.Verify!=null&&!entry.Verify.IsCompleted)return false;
        if(entry.Loading!=null&&!entry.Loading.isDone)return false;
        if(entry.Loading!=null&&entry.Bundle==null)entry.Bundle=entry.Loading.assetBundle;
        return true;
    }
    internal void ReleaseOne(Entry entry)
    {
        if(!Drain(entry))throw new EveWorldCleanupPendingException("Bundle acquisition still pending.");
        if(entry.References<1)throw new InvalidOperationException("Bundle lease underflow.");
        if(entry.References==1){if(entry.Bundle!=null)entry.Bundle.Unload(true);entry.Bundle=null;entry.Loading=null;entry.Verify=null;entry.Failure=null;}
        entry.References--;
    }
    public void Dispose()
    {closing=true;if(LeaseReferences!=0)throw new EveWorldCleanupPendingException("Worlds still own bundle leases.");foreach(var e in entries.Values)if(e.Bundle!=null){e.Bundle.Unload(true);e.Bundle=null;}entries.Clear();}
}
public sealed class PNWorldBundleLease : IDisposable
{
    private PNWorldBundleService owner;private readonly PNWorldBundleService.Entry[] entries;private int releaseCursor;
    internal PNWorldBundleLease(PNWorldBundleService owner,PNWorldBundleService.Entry[] entries){this.owner=owner;this.entries=entries;releaseCursor=entries.Length-1;}
    public bool Ready
    {get{if(owner==null)throw new ObjectDisposedException(nameof(PNWorldBundleLease));foreach(var entry in entries)if(!owner.Pump(entry))return false;return true;}}
    public AssetBundle Bundle=>Ready?entries[entries.Length-1].Bundle:throw new InvalidOperationException("Bundle is not ready.");
    public void Dispose(){if(owner==null)return;while(releaseCursor>=0){owner.ReleaseOne(entries[releaseCursor]);releaseCursor--;}owner=null;}
}
#endif
