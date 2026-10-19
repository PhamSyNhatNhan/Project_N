#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EveEngine;
using UnityEngine;

public enum PNWorldSaveState { Capturing,Writing,Committing,Saved,Restoring,Completed,Failed,Cancelled }
public sealed class PNWorldSaveOperation
{
    public PNWorldSaveState State {get;internal set;}
    public string Error {get;internal set;}
    public long Revision {get;internal set;}
    public bool Recovered {get;internal set;}
    public bool CleanupPending {get;internal set;}
    public PNWorldLifetimeOperation Restore {get;internal set;}
    public bool IsTerminal=>!CleanupPending&&(State==PNWorldSaveState.Saved||State==PNWorldSaveState.Completed||State==PNWorldSaveState.Failed||State==PNWorldSaveState.Cancelled);
    internal readonly CancellationTokenSource Cancellation=new CancellationTokenSource();
    public void Cancel()=>Cancellation.Cancel();
}
// Per-play coordinator. Unity/provider work stays on the main thread, file IO receives immutable data.
public sealed class PNWorldSaveService : IDisposable
{
    private sealed class Work
    {public string Key;public PNWorldSaveOperation Result;public PNWorldLifetimeOperation Capture;public PNWorldSaveEnvelope Envelope;public Task<long> Write;public Task<PNWorldFileRead> Read;public EveWorldContext Source;public bool Unload,Paused;public IDisposable Lease;public long Bytes;public Guid Imported;}
    private readonly EveSessionContext session;
    private readonly PNWorldRuntime worlds;
    private readonly PNWorldLifetimeController lifetime;
    private readonly PNWorldFileStore files;
    private readonly List<Work> pending=new List<Work>();
    private bool closing;
    public int PendingCount=>pending.Count;
    public long ReservedBytes=>pending.Sum(x=>x.Bytes);
    public PNWorldSaveService(EveSessionContext session,PNWorldRuntime worlds,PNWorldLifetimeController lifetime,string root)
    {this.session=session;this.worlds=worlds;this.lifetime=lifetime;files=new PNWorldFileStore(root);lifetime.TrackIO(this,()=>ReservedBytes);session.RegisterCleanup(Dispose);}
    private Work New(string key)
    {
        PNWorldFileStore.ValidateKey(key);if(closing||!session.IsActive||!session.HasAuthority)throw new InvalidOperationException("Current authority save service required.");
        if(pending.Count>=4||pending.Any(x=>x.Key==key))throw new InvalidOperationException("Save service is busy for this key or queue is full.");
        var work=new Work{Key=key,Result=new PNWorldSaveOperation()};pending.Add(work);return work;
    }
    public PNWorldSaveOperation SaveResident(string key,EveWorldContext world,bool unloadAfterCommit=false)
    {
        var work=New(key);
        try{work.Source=world;work.Paused=world.State==EveWorldState.Paused;work.Unload=unloadAfterCommit;work.Capture=lifetime.CaptureSnapshot(world,unloadAfterCommit);work.Result.State=PNWorldSaveState.Capturing;return work.Result;}
        catch{pending.Remove(work);throw;}
    }
    public PNWorldSaveOperation SaveCheckpoint(string key,PNWorldCheckpoint checkpoint)
    {var work=New(key);try{work.Envelope=lifetime.Export(checkpoint);Reserve(work);work.Result.State=PNWorldSaveState.Writing;return work.Result;}catch{pending.Remove(work);throw;}}
    public PNWorldSaveOperation RestoreSaved(string key)
    {var work=New(key);work.Result.State=PNWorldSaveState.Restoring;return work.Result;}
    private void Reserve(Work work)
    {long bytes=checked((long)work.Envelope.Bytes*2);if(lifetime.TrackedBytes+bytes>128L*1024*1024)throw new InvalidOperationException("Save IO/checkpoint byte budget exceeded.");work.Bytes=bytes;}
    public void Tick()
    {
        foreach(var work in pending.ToArray())
        {
            try
            {
                if(!session.IsActive||closing)work.Result.Cancel();
                if(work.Result.Cancellation.IsCancellationRequested&&work.Write==null&&work.Read==null)
                {if(work.Capture!=null&&!work.Capture.IsTerminal)continue;Finish(work,PNWorldSaveState.Cancelled);continue;}
                if(work.Capture!=null)
                {
                    if(!work.Capture.IsTerminal)continue;
                    if(work.Capture.State!=PNWorldLifetimeState.Captured)throw new InvalidOperationException(work.Capture.Error);
                    work.Lease=work.Capture.CaptureLease;work.Capture.CaptureLease=null;work.Envelope=lifetime.Export(work.Capture.Snapshot);Reserve(work);work.Capture=null;
                    work.Result.State=PNWorldSaveState.Writing;
                }
                if(work.Result.State==PNWorldSaveState.Writing)
                {
                    if(work.Write==null){if(pending.Any(x=>x.Write!=null&&!x.Write.IsCompleted||x.Read!=null&&!x.Read.IsCompleted))continue;work.Write=Task.Run(()=>files.Write(work.Key,work.Envelope,work.Result.Cancellation.Token));}
                    if(!work.Write.IsCompleted)continue;
                    work.Result.Revision=work.Write.GetAwaiter().GetResult();work.Result.State=PNWorldSaveState.Committing;
                }
                if(work.Result.State==PNWorldSaveState.Committing)
                {
                    work.Lease?.Dispose();work.Lease=null;
                    if(work.Unload&&!worlds.Unload(work.Source)){work.Result.CleanupPending=true;continue;}
                    Finish(work,PNWorldSaveState.Saved);continue;
                }
                if(work.Result.State==PNWorldSaveState.Restoring)
                {
                    if(work.Result.Cancellation.IsCancellationRequested&&work.Result.Restore!=null&&!work.Result.Restore.IsTerminal){lifetime.Cancel(work.Result.Restore);continue;}
                    if(work.Result.Restore==null)
                    {
                        if(work.Read==null)
                        {if(pending.Any(x=>x.Write!=null&&!x.Write.IsCompleted||x.Read!=null&&!x.Read.IsCompleted))continue;long available=128L*1024*1024-lifetime.TrackedBytes;if(available<4096)throw new InvalidOperationException("Checkpoint memory leaves no save IO budget.");work.Bytes=available;int bound=(int)Math.Min(PNWorldSaveEnvelope.MaximumBytes,available/2);work.Read=Task.Run(()=>files.Read(work.Key,bound));}
                        if(!work.Read.IsCompleted)continue;var read=work.Read.GetAwaiter().GetResult();
                        work.Bytes=2L*read.Envelope.Bytes;if(lifetime.TrackedBytes>128L*1024*1024)throw new InvalidOperationException("Checkpoint memory changed during save read.");
                        work.Result.Cancellation.Token.ThrowIfCancellationRequested();if(closing||!session.IsActive)throw new OperationCanceledException();
                        work.Result.Revision=read.Revision;work.Result.Recovered=read.Recovered;work.Imported=lifetime.Import(read.Envelope);work.Result.Restore=lifetime.Restore(work.Imported);work.Bytes=0;
                    }
                    if(!work.Result.Restore.IsTerminal)continue;
                    if(work.Result.Cancellation.IsCancellationRequested)throw new OperationCanceledException();
                    if(work.Result.Restore.State!=PNWorldLifetimeState.Completed)throw new InvalidOperationException(work.Result.Restore.Error);
                    Finish(work,PNWorldSaveState.Completed);
                }
            }
            catch(Exception error)
            {if(work.Result.State==PNWorldSaveState.Committing&&work.Unload){work.Result.Error=error.Message;work.Result.CleanupPending=true;continue;}Finish(work,error is OperationCanceledException?PNWorldSaveState.Cancelled:PNWorldSaveState.Failed,error.Message);}
        }
    }
    private void Finish(Work work,PNWorldSaveState state,string error=null)
    {
        if(work.Imported!=Guid.Empty){lifetime.ReleaseImportedCheckpoint(work.Imported);work.Imported=Guid.Empty;}
        work.Lease?.Dispose();work.Lease=null;
        if(work.Capture?.CaptureLease!=null){work.Capture.CaptureLease.Dispose();work.Capture.CaptureLease=null;}
        if(work.Unload&&work.Source?.State==EveWorldState.Paused&&!work.Paused)work.Source.SetPaused(false);
        work.Result.State=state;work.Result.Error=error;work.Result.CleanupPending=false;work.Bytes=0;work.Envelope=null;pending.Remove(work);
    }
    public void Dispose()
    {
        closing=true;foreach(var work in pending)work.Result.Cancel();Tick();
        if(pending.Count>0)throw new EveWorldCleanupPendingException("Save IO/capture cleanup is still owned; retry session cleanup.");
        lifetime.UntrackIO(this);
    }
}
#endif
