using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
namespace EveEngine
{
    public sealed class EvePreparationProfile
    {
        public int Pending=64, Active=256, Resident=320, Steps=8;
        public int CleanupStepsPerFrame=16,CleanupStepsPerLease=32,CleanupAttempts=3,PauseAtBacklog=64;
        public double CleanupMs=1,CleanupRetryMs=250;
        public double LifecycleMs=2, PrepareMs=1, TimeoutMs=30000, CommitWarningMs=4;
        public void Validate()
        {
            foreach(double value in new[]{LifecycleMs,PrepareMs,TimeoutMs,CommitWarningMs,CleanupMs,CleanupRetryMs})if(double.IsNaN(value)||double.IsInfinity(value)||value<=0)throw new ArgumentOutOfRangeException();
            if(Pending<1||Active<1||Resident<Active||Steps<1||PrepareMs>LifecycleMs||CleanupMs>LifecycleMs||CleanupStepsPerFrame<1||CleanupStepsPerLease<1||CleanupStepsPerLease==int.MaxValue||CleanupAttempts<1||PauseAtBacklog<1)throw new ArgumentOutOfRangeException();
        }
    }
    // Owns every factory borrow from Get until successful return, including failures before a cleanup lease.
    public sealed class EveEntityPreparationScheduler
    {
        public sealed class Candidate
        {
            internal Func<EveEntityLoader> Create;
            internal Action<EveEntityLoader> Configure,Return,Published;
            internal Action<EveEntityLoader> BeforePublish;
            internal Func<EveEntityLoader,bool> PreparePublication;
            internal Action<Exception> Failed;
            internal bool Leased,Retiring,Returned;
            internal double Started,RetryAt;
            internal int Attempts;
            public EveEntityLoader Loader {get; internal set;}
            public Exception LastError {get; internal set;}
            public bool IsReturned=>Returned;
        }
        private readonly EveSessionContext session;
        private readonly EvePreparationProfile profile;
        private readonly Func<double> clock;
        private readonly Queue<Candidate> pending=new Queue<Candidate>();
        private readonly List<Candidate> owned=new List<Candidate>();
        private bool stopping;
        private int cursor,publicationCursor;
        private long frames;
        private readonly List<PreparationJob> jobs=new List<PreparationJob>();
        private int jobCursor;
        private sealed class PreparationJob:IDisposable
        {
            public Func<bool> Advance;public Action<Exception> Failed;public bool Disposed;
            public void Dispose(){Disposed=true;Advance=null;Failed=null;}
        }
        public IDisposable SchedulePreparation(Func<bool> advance,Action<Exception> failed)
        {
            jobs.RemoveAll(x=>x.Disposed);
            if(stopping||!session.IsActive||jobs.Count>=profile.Resident)throw new InvalidOperationException("Preparation work queue closed/full.");
            var job=new PreparationJob{Advance=advance??throw new ArgumentNullException(nameof(advance)),Failed=failed};jobs.Add(job);return job;
        }
        private double frameStart;
        public double TimeoutMilliseconds=>profile.TimeoutMs;
        public int PendingCount=>pending.Count;
        public int ResidentCount=>owned.Count;
        public int ActiveCount=>owned.Count(x=>!x.Retiring);
        public int AdmissionCount=>owned.Count(x=>!x.Retiring||x.LastError!=null);
        public double RemainingFrameMs=>Math.Max(0,profile.LifecycleMs-(clock()-frameStart));
        public EveEntityPreparationScheduler(EveSessionContext session,EvePreparationProfile profile,Func<double> clock=null)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));this.profile=profile??throw new ArgumentNullException(nameof(profile));profile.Validate();
            this.clock=clock??(()=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency);frameStart=this.clock();
            if(session.Cleanup!=null)throw new InvalidOperationException("Preparation owns the shared cleanup budget.");
            session.ConfigureCleanup(new Experimental.Lifecycle.CleanupPolicy(profile.Resident,profile.Resident,Math.Min(profile.PauseAtBacklog,profile.Resident),profile.CleanupStepsPerFrame,profile.CleanupStepsPerLease,profile.CleanupMs,profile.CleanupAttempts,profile.CleanupRetryMs),this.clock,()=>RemainingFrameMs);
            session.RegisterCleanup(Stop);
        }
        public Candidate Enqueue(Func<EveEntityLoader> create,Action<EveEntityLoader> configure,Action<EveEntityLoader> returned,Action<EveEntityLoader> published,Action<Exception> failed,Action<EveEntityLoader> beforePublish=null)
            => Enqueue(create,configure,returned,published,failed,beforePublish,null);
        public Candidate Enqueue(Func<EveEntityLoader> create,Action<EveEntityLoader> configure,Action<EveEntityLoader> returned,Action<EveEntityLoader> published,Action<Exception> failed,Action<EveEntityLoader> beforePublish,Func<EveEntityLoader,bool> preparePublication)
        {
            if(stopping||!session.IsActive||pending.Count>=profile.Pending)throw new InvalidOperationException("Preparation queue is closed or full.");
            var candidate=new Candidate {Create=create??throw new ArgumentNullException(nameof(create)),Configure=configure??throw new ArgumentNullException(nameof(configure)),Return=returned??throw new ArgumentNullException(nameof(returned)),Published=published,Failed=failed,BeforePublish=beforePublish,PreparePublication=preparePublication,Started=clock()};
            pending.Enqueue(candidate);return candidate;
        }
        private void Return(Candidate candidate)
        {
            candidate.Return(candidate.Loader);candidate.Returned=true;owned.Remove(candidate);
        }
        private void Fail(Candidate candidate,Exception error)
        {
            candidate.LastError=error;
            Retire(candidate);
            try {candidate.Failed?.Invoke(error);}catch(Exception callback){UnityEngine.Debug.LogException(callback,null);}
        }
        public void Retire(Candidate candidate)
        {
            if(candidate==null||candidate.Returned||candidate.Retiring)return;
            candidate.Retiring=true;
            if(ReferenceEquals(candidate.Loader,null)){candidate.Returned=true;return;}
            if(candidate.Loader!=null)candidate.Loader.InvalidateAndHide();
            if(candidate.Leased)session.Cleanup.Retire(candidate.Loader);
            else CleanupProvisional(candidate);
        }
        private void CleanupProvisional(Candidate candidate)
        {
            try {candidate.Loader.Unload();Return(candidate);candidate.LastError=null;}
            catch(Exception error){candidate.LastError=error;candidate.Attempts++;candidate.RetryAt=clock()+profile.CleanupRetryMs;}
        }
        public void RetryQuarantines()
        {
            foreach(var candidate in owned.Where(x=>x.Retiring&&!x.Leased).ToArray()){candidate.Attempts=0;CleanupProvisional(candidate);}
            session.Cleanup.RetryQuarantines();
        }
        public void AdvanceFrame()
        {
            frameStart=clock();session.Cleanup.AdvanceFrame();
            frames++;
            foreach(var candidate in owned.Where(x=>x.Retiring&&!x.Leased&&x.Attempts<profile.CleanupAttempts&&clock()>=x.RetryAt).ToArray())CleanupProvisional(candidate);
            if(stopping||!session.IsActive)return;
            foreach(var candidate in owned.Where(x=>!x.Retiring&&x.Loader!=null&&x.Loader.State==EveEntityLoadState.Prepared&&clock()-x.Started>profile.TimeoutMs).ToArray())Fail(candidate,new TimeoutException("Prepared entity publication timed out."));
            double prepareStart=clock();int steps=0;
            bool HasTime()=>RemainingFrameMs>0&&clock()-prepareStart<profile.PrepareMs&&steps<profile.Steps;
            jobs.RemoveAll(x=>x.Disposed);
            var work=jobs.ToArray();
            bool jobTurn=profile.Steps>1||frames%2==0||pending.Count==0&&!owned.Any(x=>!x.Retiring&&x.Loader!=null&&(x.Loader.State==EveEntityLoadState.Preparing||x.Loader.State==EveEntityLoadState.Prepared));
            for(int i=0;jobTurn&&i<Math.Min(work.Length,Math.Max(1,profile.Steps/2))&&HasTime();i++)
            {
                jobCursor%=work.Length;var job=work[jobCursor++];if(job.Disposed)continue;
                steps++;try{if(job.Advance())job.Dispose();}catch(Exception error){var failed=job.Failed;job.Dispose();try{failed?.Invoke(error);}catch(Exception callback){UnityEngine.Debug.LogException(callback,null);}}
            }
            while(pending.Count>0&&HasTime())
            {
                var next=pending.Peek();
                if(next.Retiring){pending.Dequeue();continue;}
                if(clock()-next.Started>profile.TimeoutMs){pending.Dequeue();Fail(next,new TimeoutException("Pending entity timed out."));continue;}
                if(!session.Cleanup.CanBeginPreparation||AdmissionCount>=profile.Active||ResidentCount>=profile.Resident)break;
                pending.Dequeue();owned.Add(next);
                try
                {
                    next.Loader=next.Create();if(next.Loader==null)throw new InvalidOperationException("Factory returned no entity.");
                    next.Configure(next.Loader);next.Loader.BeginPrepare(session.Registry);
                    if(!session.Cleanup.Track(next.Loader,()=>Return(next)))throw new InvalidOperationException("Cleanup reservation rejected.");
                    next.Leased=true;
                }
                catch(Exception error){if(next.Loader==null){owned.Remove(next);next.Returned=true;}Fail(next,error);}
                // BeginPrepare is non-preemptible and consumes the shared elapsed budget too.
            }
            var batch=new List<Candidate>();
            var preparing=owned.Where(x=>!x.Retiring&&x.Loader.State==EveEntityLoadState.Preparing).ToArray();
            while(preparing.Length>0&&HasTime())
            {
                cursor%=preparing.Length;var candidate=preparing[cursor];cursor=(cursor+1)%preparing.Length;
                if(candidate.Retiring||candidate.Loader.State!=EveEntityLoadState.Preparing)
                {preparing=preparing.Where(x=>!x.Retiring&&x.Loader.State==EveEntityLoadState.Preparing).ToArray();continue;}
                try
                {
                    steps++;if(clock()-candidate.Started>profile.TimeoutMs)throw new TimeoutException("Entity preparation timed out.");
                    if(candidate.Loader.PrepareNext()&&candidate.PreparePublication==null)batch.Add(candidate);
                }
                catch(Exception error){Fail(candidate,error);}
            }
            var prepared=owned.Where(x=>!x.Retiring&&x.Loader!=null&&x.Loader.State==EveEntityLoadState.Prepared&&x.PreparePublication!=null).ToArray();
            for(int i=0;i<prepared.Length&&HasTime();i++)
            {
                publicationCursor%=prepared.Length;var candidate=prepared[publicationCursor++];
                try{steps++;if(candidate.PreparePublication==null||candidate.PreparePublication(candidate.Loader))batch.Add(candidate);}catch(Exception error){Fail(candidate,error);}
            }
            if(batch.Count==0)return;
            try {EveEntityLoader.CommitBatch(batch.Select(x=>x.Loader),()=> {foreach(var candidate in batch)candidate.BeforePublish?.Invoke(candidate.Loader);});}
            catch(Exception error){foreach(var candidate in batch)Fail(candidate,error);return;}
            if(session.LastCommitMilliseconds>profile.CommitWarningMs)UnityEngine.Debug.LogWarning("Entity commit exceeded warning threshold: "+session.LastCommitMilliseconds+" ms (record only).");
            foreach(var candidate in batch)try {candidate.Published?.Invoke(candidate.Loader);}catch(Exception error){Fail(candidate,error);}
        }
        public void Stop()
        {
            stopping=true;
            foreach(var job in jobs)job.Dispose();jobs.Clear();
            while(pending.Count>0)Retire(pending.Dequeue());
            foreach(var candidate in owned.ToArray())Retire(candidate);
            if(owned.Any(x=>!x.Leased))throw new InvalidOperationException("Provisional entity cleanup is pending; explicit retry required.");
        }
    }
}
