using System;
using System.Collections.Generic;

// EN: The phases of one room (Docs/RogueliteFramePlan.md section 7.3) as a pure state machine. The room director feeds it what happened (players entered, the switch was pressed,
//     time passed, enemies died, everyone died, a gate was taken) and carries out the commands it returns (enable the switch, show the countdown, spawn a wave, grant the reward and
//     open the gates, report defeat). Time is the room's game clock, passed in by the caller; the machine reads no clock itself.
//       Entering -> Waiting -> Countdown -> Combat -> Cleared -> Leaving      (a room without combat goes Entering -> Cleared at once)
// VI: Các pha của một phòng dưới dạng máy trạng thái thuần: nhận sự kiện, trả lệnh. Thời gian là đồng hồ game do bên gọi đưa vào.
public enum PNRoomPhase { Entering, Waiting, Countdown, Combat, Cleared, Leaving, Defeated, Failed }
public enum PNRoomCommandKind { EnableStart, StartCountdown, SpawnWave, RoomCleared, Defeat }

public readonly struct PNRoomCommand
{
    public PNRoomCommandKind Kind { get; }
    public int Wave { get; }        // SpawnWave: the wave index
    public int Count { get; }       // SpawnWave: how many enemies the wave brings
    public double Seconds { get; }  // StartCountdown: the countdown length
    public PNRoomCommand(PNRoomCommandKind kind, int wave = 0, int count = 0, double seconds = 0) { Kind = kind; Wave = wave; Count = count; Seconds = seconds; }
}

public sealed class PNRoomFlow
{
    private readonly PNRoomDefinition room;
    private double timer;
    private bool waitingForWave;
    public PNRoomPhase Phase { get; private set; } = PNRoomPhase.Entering;
    public int WaveIndex { get; private set; } = -1;
    // Enemies of the current wave still standing, as the director reports them.
    public int Alive { get; private set; }
    // Seconds left of the countdown while in Countdown, else 0.
    public double CountdownRemaining => Phase == PNRoomPhase.Countdown ? Math.Max(0, timer) : 0;

    public PNRoomFlow(PNRoomDefinition room) { this.room = room ?? throw new ArgumentNullException(nameof(room)); }

    private static readonly IReadOnlyList<PNRoomCommand> None = new PNRoomCommand[0];

    // The players are in the room and it can begin.
    public IReadOnlyList<PNRoomCommand> Entered()
    {
        if (Phase != PNRoomPhase.Entering) throw new InvalidOperationException("The room was already entered.");
        var output = new List<PNRoomCommand>();
        if (!room.HasCombat) { Phase = PNRoomPhase.Cleared; output.Add(new PNRoomCommand(PNRoomCommandKind.RoomCleared)); return output; }
        Phase = PNRoomPhase.Waiting;
        if (room.Start.Mode == PNRoomStartMode.Interact) output.Add(new PNRoomCommand(PNRoomCommandKind.EnableStart));
        else { timer = room.Start.Delay; if (timer <= 0) BeginCountdown(output); }
        return output;
    }

    // The host pressed the start switch. Ignored unless the room waits for it.
    public IReadOnlyList<PNRoomCommand> StartPressed()
    {
        if (Phase != PNRoomPhase.Waiting || room.Start.Mode != PNRoomStartMode.Interact) return None;
        var output = new List<PNRoomCommand>(); BeginCountdown(output); return output;
    }

    public IReadOnlyList<PNRoomCommand> Tick(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var output = new List<PNRoomCommand>();
        switch (Phase)
        {
            case PNRoomPhase.Waiting:
                if (room.Start.Mode != PNRoomStartMode.Auto) break;
                timer -= seconds; if (timer <= 0) BeginCountdown(output);
                break;
            case PNRoomPhase.Countdown:
                timer -= seconds; if (timer <= 0) BeginCombat(output);
                break;
            case PNRoomPhase.Combat:
                if (!waitingForWave) break;
                timer -= seconds; if (timer <= 0) SpawnWave(output);
                break;
        }
        return output;
    }

    // `count` enemies of the current wave died or were removed. Reporting more than are alive is a bug of the caller.
    public IReadOnlyList<PNRoomCommand> EnemiesCleared(int count = 1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Phase != PNRoomPhase.Combat || waitingForWave) { if (count == 0) return None; throw new InvalidOperationException("No wave is standing."); }
        if (count > Alive) throw new InvalidOperationException("More enemies cleared (" + count + ") than alive (" + Alive + ").");
        Alive -= count;
        var output = new List<PNRoomCommand>();
        if (Alive == 0) NextWave(output);
        return output;
    }

    // Every player of the room is dead.
    public IReadOnlyList<PNRoomCommand> AllPlayersDead()
    {
        if (Phase != PNRoomPhase.Waiting && Phase != PNRoomPhase.Countdown && Phase != PNRoomPhase.Combat) return None;
        Phase = PNRoomPhase.Defeated;
        return new[] { new PNRoomCommand(PNRoomCommandKind.Defeat) };
    }

    // The host took a gate. Returns false unless the room was cleared and still waits.
    public bool ExitChosen()
    {
        if (Phase != PNRoomPhase.Cleared) return false;
        Phase = PNRoomPhase.Leaving; return true;
    }

    // The room cannot go on (a missing key in the map, a spawn point that cannot be reached). The director ends the run.
    public void Fail() { if (Phase != PNRoomPhase.Leaving) Phase = PNRoomPhase.Failed; }

    private void BeginCountdown(List<PNRoomCommand> output)
    {
        if (room.Start.Countdown <= 0) { BeginCombat(output); return; }
        Phase = PNRoomPhase.Countdown; timer = room.Start.Countdown;
        output.Add(new PNRoomCommand(PNRoomCommandKind.StartCountdown, seconds: room.Start.Countdown));
    }
    private void BeginCombat(List<PNRoomCommand> output) { Phase = PNRoomPhase.Combat; WaveIndex = -1; waitingForWave = false; NextWave(output); }
    private void NextWave(List<PNRoomCommand> output)
    {
        WaveIndex++; Alive = 0;
        if (WaveIndex >= room.Waves.Count) { Phase = PNRoomPhase.Cleared; waitingForWave = false; output.Add(new PNRoomCommand(PNRoomCommandKind.RoomCleared)); return; }
        waitingForWave = true; timer = room.Waves[WaveIndex].Delay;
        if (timer <= 0) SpawnWave(output);
    }
    private void SpawnWave(List<PNRoomCommand> output)
    {
        waitingForWave = false; Alive = room.Waves[WaveIndex].Total;
        output.Add(new PNRoomCommand(PNRoomCommandKind.SpawnWave, WaveIndex, Alive));
    }
}
