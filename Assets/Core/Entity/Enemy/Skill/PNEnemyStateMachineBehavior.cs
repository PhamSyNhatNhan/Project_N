#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;

// Optional local FSM. Decisions/actions remain in the concrete behavior.
public abstract class PNEnemyStateMachineBehavior : PNEnemyBehavior
{
    private sealed class State
    { public Action Enter, Exit; public Action<float> Tick; }
    private readonly Dictionary<string, State> states = new Dictionary<string, State>(StringComparer.Ordinal);
    private string pending;
    private bool dispatching;
    public string CurrentState { get; private set; }
    protected void ClearStates()
    { if (dispatching) throw new InvalidOperationException("Cannot rebuild a running state transition."); states.Clear(); CurrentState = pending = null; }
    protected void AddState(string id, Action enter, Action<float> tick, Action exit = null)
    {
        if (string.IsNullOrWhiteSpace(id) || tick == null || states.ContainsKey(id)) throw new ArgumentException("Unique state and Tick required.");
        states.Add(id, new State { Enter = enter, Tick = tick, Exit = exit });
    }
    protected void ChangeState(string id)
    { if (!states.ContainsKey(id)) throw new ArgumentException("Unknown local state: " + id); pending = id; }
    public sealed override void Tick(float delta)
    {
        Drain();
        if (CurrentState != null) { dispatching = true; try { states[CurrentState].Tick(delta); } finally { dispatching = false; } }
        Drain();
    }
    private void Drain()
    {
        int transitions = 0;
        while (pending != null)
        {
            if (++transitions > 16) throw new InvalidOperationException("Enemy FSM transition loop.");
            var next = pending; pending = null; if (next == CurrentState) continue;
            dispatching = true;
            try { if (CurrentState != null) states[CurrentState].Exit?.Invoke(); CurrentState = next; states[next].Enter?.Invoke(); }
            finally { dispatching = false; }
        }
    }
    public override void Exit(PNEnemyBehaviorExit reason)
    {
        var previous = CurrentState; CurrentState = pending = null;
        if (previous != null) states[previous].Exit?.Invoke(); states.Clear();
    }
}
#endif
