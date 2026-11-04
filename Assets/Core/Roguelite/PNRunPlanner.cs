using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Moves a run along its floors (Docs/RogueliteFramePlan.md sections 5.2 and 7.2). Pure: given the run definition and a way to find floors, it picks which rooms the gates
//     offer (weighted, without repeats, from the Layout stream), and moves the state into the room a gate leads to. The first room of a run is the one room of step 0 of floor 0.
// VI: Đưa run đi qua các tầng: chọn phòng cho cổng (theo trọng số, không lặp, luồng Layout) và chuyển trạng thái vào phòng của cổng được chọn.
public sealed class PNRunPlanner
{
    private readonly PNRunDefinition run;
    private readonly Func<EveId, PNFloorDefinition> floors;

    public PNRunPlanner(PNRunDefinition run, Func<EveId, PNFloorDefinition> floors)
    { this.run = run ?? throw new ArgumentNullException(nameof(run)); this.floors = floors ?? throw new ArgumentNullException(nameof(floors)); }

    public PNFloorDefinition FloorAt(int floorIndex)
    {
        if (floorIndex < 0 || floorIndex >= run.Floors.Count) throw new ArgumentOutOfRangeException(nameof(floorIndex));
        return floors(run.Floors[floorIndex]) ?? throw new InvalidOperationException("Floor " + run.Floors[floorIndex] + " is not registered.");
    }
    public PNFloorStep StepAt(int floorIndex, int step)
    {
        var floor = FloorAt(floorIndex);
        if (step < 0 || step >= floor.Steps.Count) throw new ArgumentOutOfRangeException(nameof(step));
        return floor.Steps[step];
    }

    public PNRunState Begin(Guid runId, ulong seed, IEnumerable<KeyValuePair<long, EveId>> players)
    {
        var state = new PNRunState(runId, run.Id, seed, run.StartMoney);
        foreach (var player in players ?? throw new ArgumentNullException(nameof(players))) state.AddPlayer(player.Key, player.Value);
        if (state.Players.Count == 0) throw new ArgumentException("A run needs at least one player.", nameof(players));
        state.Floor = 0; state.Step = 0;
        // The run opens in one room: the first draw of step 0 of floor 0, whatever that step's choices say.
        state.Room = Draw(StepAt(0, 0), 1, state.Random)[0];
        return state;
    }

    public bool IsLastRoom(PNRunState state)
    {
        RequireRunning(state);
        return state.Floor == run.Floors.Count - 1 && state.Step == FloorAt(state.Floor).Steps.Count - 1;
    }

    // The position the gates of the current room lead to, or false after the last room.
    public bool TryNext(PNRunState state, out int floor, out int step)
    {
        RequireRunning(state);
        floor = state.Floor; step = state.Step + 1;
        if (step < FloorAt(floor).Steps.Count) return true;
        floor = state.Floor + 1; step = 0;
        return floor < run.Floors.Count;
    }

    // EN: Draw the rooms for the gates of the current room and keep them in state.Options. Asking again for the same room gives the same options and takes no more numbers
    //     from the stream. After the last room there are no gates.
    public IReadOnlyList<EveId> PrepareExits(PNRunState state)
    {
        RequireRunning(state);
        if (state.Options.Count > 0) return state.Options;
        if (!TryNext(state, out int floor, out int step)) return state.Options;
        var next = StepAt(floor, step);
        state.Options.AddRange(Draw(next, next.Choices, state.Random));
        return state.Options;
    }

    // EN: Go through the gate with this index: the state moves into that room and the options are cleared. Returns false for a bad index or when there are no gates.
    public bool Choose(PNRunState state, int optionIndex)
    {
        RequireRunning(state);
        if (optionIndex < 0 || optionIndex >= state.Options.Count || !TryNext(state, out int floor, out int step)) return false;
        state.Room = state.Options[optionIndex]; state.Floor = floor; state.Step = step; state.Options.Clear();
        return true;
    }

    // EN: Pick `count` distinct rooms from a step, each draw proportional to the weights that are left. Uses only the Layout stream.
    public static IReadOnlyList<EveId> Draw(PNFloorStep step, int count, PNRunRandom random)
    {
        if (step == null) throw new ArgumentNullException(nameof(step));
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (count < 1 || count > step.Rooms.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var pool = step.Rooms.ToList(); var result = new List<EveId>(count);
        for (int n = 0; n < count; n++)
        {
            double total = 0; foreach (var entry in pool) total += entry.Weight;
            double roll = random.NextDouble(PNRunStream.Layout) * total; int pick = pool.Count - 1;
            for (int i = 0; i < pool.Count; i++) { roll -= pool[i].Weight; if (roll < 0) { pick = i; break; } }
            result.Add(pool[pick].Room); pool.RemoveAt(pick);
        }
        return result;
    }

    private static void RequireRunning(PNRunState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (!state.IsRunning) throw new InvalidOperationException("The run already ended: " + state.Result);
    }
}
