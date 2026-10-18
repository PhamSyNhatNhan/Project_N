#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using EveEngine;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // Fault injection checks the coordinator's resource ownership, not gameplay stubs.
    public static class PNBindingAdapterChecks
    {
        private sealed class Stage : EveIReplicaStage
        {
            private readonly string name;
            private readonly List<string> trace;
            public bool FailDispose;
            public Stage(string name,List<string> trace){this.name=name;this.trace=trace;}
            public void Commit() { }
            public void Published()=>trace.Add(name+" publish");
            public void Dispose(){trace.Add(name+" dispose");if(FailDispose)throw new InvalidOperationException("Injected cleanup failure");}
        }
        private sealed class Lease : IDisposable
        {
            public int Releases;
            public void Dispose(){Releases++;}
        }
        private static object State(object binding,string field)=>binding.GetType().GetField(field).GetValue(binding);
        private static List<EveIReplicaStage> Stages(object state)=>(List<EveIReplicaStage>)state.GetType().GetField("Stages").GetValue(state);
        private static void Call(PNNetworkManager manager,string method,object binding)
        {
            try{typeof(PNNetworkManager).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new[]{binding});}
            catch(TargetInvocationException error){throw error.InnerException;}
        }
        public static string Run()
        {
            int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
            var trace=new List<string>();var type=typeof(PNNetworkManager).GetNestedType("Binding",BindingFlags.NonPublic);
            object NewBinding()=>Activator.CreateInstance(type,true);
            var root=new GameObject("Disposable binding ownership check");root.SetActive(false);
            try
            {
                var manager=root.AddComponent<PNNetworkManager>();
                var first=NewBinding();var second=NewBinding();
                Stages(State(first,"Stats")).Add(new Stage("stat1",trace));
                Stages(State(first,"Stats")).Add(new Stage("stat2",trace));
                Stages(State(first,"Buffs")).Add(new Stage("buff",trace));
                Call(manager,"PublishBindingStages",first);
                Check(string.Join(",",trace)=="buff publish,buff dispose,stat1 publish,stat1 dispose,stat2 publish,stat2 dispose","publication order and owned release");
                Call(manager,"CleanupBinding",first);
                Check(trace.Count==6,"cleanup does not re-dispose published stages");

                trace.Clear();var failing=new Stage("stat-fail",trace){FailDispose=true};
                Stages(State(first,"Stats")).Add(new Stage("stat-ok",trace));Stages(State(first,"Stats")).Add(failing);
                Stages(State(first,"Buffs")).Add(new Stage("buff-rollback",trace));
                var movement=State(first,"Movement");var queued=new Lease();var applying=new Lease();
                ((Queue<(PNMovePacket Packet,IDisposable Lease)>)movement.GetType().GetField("Controls").GetValue(movement)).Enqueue((default,queued));
                movement.GetType().GetField("ApplyingLease").SetValue(movement,applying);
                var worldLease=new Lease();first.GetType().GetField("WorldLease").SetValue(first,worldLease);
                try{Call(manager,"CleanupBinding",first);throw new Exception("Cleanup failure was swallowed");}catch(AggregateException){checks++;}
                Check(string.Join(",",trace)=="stat-fail dispose,stat-ok dispose,buff-rollback dispose","failure does not skip remaining stages or other adapter");
                Check(queued.Releases==1&&applying.Releases==1,"Move leases released despite Stat failure");
                Check(worldLease.Releases==0,"failed adapter cleanup retains its world resource lease");
                Check(Stages(State(first,"Stats")).Count==1&&Stages(State(first,"Buffs")).Count==0,"only failed stage retained for retry");
                Check(Stages(State(second,"Stats")).Count==0&&Stages(State(second,"Buffs")).Count==0,"another binding's resources remain isolated");
                failing.FailDispose=false;Call(manager,"CleanupBinding",first);int completed=trace.Count;
                Call(manager,"CleanupBinding",first);
                Check(Stages(State(first,"Stats")).Count==0&&trace.Count==completed,"successful retry is idempotent");
                Check(queued.Releases==1&&applying.Releases==1,"cleanup retry does not double release Move reservations");
                Check(worldLease.Releases==1,"world lease released once after required cleanup succeeds");
                return "PASS "+checks+" binding ownership/publication/rollback checks";
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
#endif
