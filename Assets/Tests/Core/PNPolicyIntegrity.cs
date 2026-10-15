using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EveEngine;

// Fixture-only constant-time event accounting plus an allocation-free host-clock reader.
// These checks are identical in legacy/opt-in controls and never execute behavior getters.
public sealed class PNPolicyIntegrity : IDisposable
{
    private sealed class Slot
    {
        internal EveId Expected;
        internal bool Dot, Transition;
        internal int ExpectedStacks=1;
        internal PNBuffWireState State;
        internal double NextTick;
    }
    private readonly PNBuffManager buff;
    private readonly PNStat stat;
    private readonly Slot[] slots;
    private readonly Dictionary<EveId,int> indices = new Dictionary<EveId,int>();
    private readonly Dictionary<long,int> network = new Dictionary<long,int>();
    private static readonly Func<PNBuffManager,double> readClock = ClockReader();
    public string Failure { get; private set; }
    public long Hits { get; private set; }
    public long Cues { get; private set; }
    public int Alive { get; private set; }
    public int Dots { get; private set; }
    public double Now => readClock(buff);
    private static Func<PNBuffManager,double> ClockReader()
    {
        var field = typeof(PNBuffManager).GetField("presentationTime", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(double)) throw new InvalidOperationException("Fixture clock reader requires the existing host presentation timeline.");
        var method = new DynamicMethod("ReadFixtureBuffClock", typeof(double), new[] { typeof(PNBuffManager) }, typeof(PNPolicyIntegrity), true);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        return (Func<PNBuffManager,double>)method.CreateDelegate(typeof(Func<PNBuffManager,double>));
    }
    public PNPolicyIntegrity(PNBuffManager buff, int count, Func<int,string> kind)
    {
        this.buff = buff; stat=buff.GetComponent<PNStat>(); slots = new Slot[count];
        for (int i=0;i<count;i++)
        {
            slots[i] = new Slot { Expected=PNPolicyContent.BuffId(kind(i),i), Dot=kind(i)=="dot" };
            foreach (string name in new[]{"dot","timed","stable"}) indices.Add(PNPolicyContent.BuffId(name,i),i);
        }
        buff.Publisher.Message += Message; buff.Publisher.OptionalCue += Cue;
        stat.DamageReceived += Damage;
    }
    private void Error(string message) { if(Failure==null) Failure=message; }
    private void Damage(PNDamageResult result)
    {
        // Only fixture DOTs use the real combat pipeline. Projectile contacts in
        // the existing projectile fixture have their own contact accounting.
        if(result.TargetId!=stat.EntityId || result.Damage.SourceId!=stat.EntityId || result.HealthBefore<=0)
            Error("Unexpected combat receiver/source or dead target in DOT fixture.");
        Hits++;
    }
    public void ExpectStackDelta(int index,int delta)
    { var s=slots[index]; if(s.Dot) s.ExpectedStacks=checked(s.ExpectedStacks+delta); }
    private void Message(PNBuffWireMessage message)
    {
        if(message.Kind==PNBuffWireKind.Removed)
        {
            if(!network.TryGetValue(message.RemovedId,out int index)) { Error("Removed unknown fixture instance"); return; }
            var slot=slots[index];
            if(!slot.Transition) Error("Instance ended outside fixture mutation: slot"+index+", reason="+message.EndReason);
            network.Remove(message.RemovedId); slot.State=null; Alive--; if(slot.Dot) Dots--; return;
        }
        if(message.Kind!=PNBuffWireKind.Added && message.Kind!=PNBuffWireKind.Updated) return;
        var state=message.States[0];
        if(!indices.TryGetValue(state.BuffId,out int j)) { Error("Unexpected Buff type in fixture"); return; }
        var entry=slots[j];
        if(state.BuffId!=entry.Expected) Error("Fixture changed slot kind without a planned mutation: "+j);
        if(message.Kind==PNBuffWireKind.Added)
        {
            if(entry.State!=null) Error("Duplicate live fixture slot: "+j);
            network[state.InstanceId]=j; Alive++; if(entry.Dot) { Dots++; entry.NextTick=state.AnchorTime+.5; }
        }
        else if(entry.State==null || entry.State.InstanceId!=state.InstanceId) Error("Updated wrong fixture incarnation");
        entry.State=state;
        if(entry.Dot && (!state.Stacks.HasValue || state.Stacks.Value!=entry.ExpectedStacks)) Error("DOT stack differs from exact fixture mutation schedule: slot"+j);
    }
    private void Cue(PNBuffOptionalCueMessage message)
    {
        if(message.Cue.Name!="tick") return;
        if(!network.TryGetValue(message.Cue.InstanceId,out int index)) { Error("DOT tick for unknown fixture instance"); return; }
        var entry=slots[index];
        if(!entry.Dot || entry.State==null) { Error("Tick from non-DOT fixture slot"); return; }
        if(entry.State.Stacks!=entry.ExpectedStacks) Error("DOT stack differs at tick: slot"+index);
        double tolerance=1e-7*Math.Max(1,Math.Abs(entry.NextTick));
        if(Math.Abs(message.Cue.At-entry.NextTick)>tolerance) Error("DOT tick differs from actual apply/lifecycle schedule at slot"+index+": expected="+entry.NextTick+", actual="+message.Cue.At);
        entry.NextTick+=.5; Cues++;
        if(Cues>Hits) Error("DOT tick cue has no actual DamageReceived hit.");
    }
    public void BeginChange(int index,string kind)
    { slots[index].Transition=true; slots[index].Expected=PNPolicyContent.BuffId(kind,index); slots[index].ExpectedStacks=1; }
    public void EndChange(int index,string kind)
    { var s=slots[index]; s.Dot=kind=="dot"; s.Transition=false; }
    // Replacement does not change DOT composition in corrected AoE; ordinary last-slot
    // churn may change dot->timed, so counters need the old kind until removal occurs.
    public void MarkNewKind(int index,string kind) { slots[index].Dot=kind=="dot"; }
    public bool IsDot(int index) => slots[index].Dot && slots[index].State!=null;
    public void Validate(PNBuffHandle[] handles)
    {
        if(Failure!=null) throw new InvalidOperationException(Failure);
        if(Alive!=handles.Length) throw new InvalidOperationException("Fixture live population differs from declared workload.");
        if(Hits!=Cues) throw new InvalidOperationException("Actual DamageReceived hits differ from scheduled DOT tick cues.");
        double now=Now, tolerance=1e-7*Math.Max(1,Math.Abs(now));
        for(int i=0;i<slots.Length;i++)
        {
            var s=slots[i];
            if(s.State==null || !handles[i].IsAlive || s.State.BuffId!=s.Expected) throw new InvalidOperationException("Fixture slot missing or changed type: "+i);
            if(s.Dot && s.State.Stacks!=s.ExpectedStacks) throw new InvalidOperationException("Fixture DOT stack differs from exact scheduled value.");
            if(s.Dot && s.NextTick<now-tolerance) throw new InvalidOperationException("Expected DOT tick missing by host clock: "+i+", next="+s.NextTick+", now="+now);
        }
    }
    public void Dispose() { stat.DamageReceived-=Damage; if(buff.Publisher!=null) { buff.Publisher.Message-=Message; buff.Publisher.OptionalCue-=Cue; } }
}
