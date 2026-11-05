using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// EN: Opt-in review of the roguelite run on the real flow, in Play Mode or a Player started from the Hall (Docs/RogueliteFramePlan.md F3-F5):
//       solo start -> lobby -> first room (the lobby is gone, the player is rebuilt in the map world) -> the start switch is the only open object, out of range it does nothing, a peer
//       may not use it, the host in range does -> 3 s countdown -> wave 1 on the navigation mesh -> wave 2 after its delay -> room cleared (reward, kills, gates opened with the kind
//       of room behind each) -> player hurt to half health -> a gate -> a new room: a NEW entity with half health, the old room unloaded, and in a shop or rest room the heal works.
//     The player is walked to each object (the review moves its body) and uses it through the real command `project_n:interact`, the way the list does.
//     Start it with the flag `-pn-run-flow-review` (a Player) or `PNRunFlowPlayReview.Begin()` (Play Mode). Results: Debug log lines starting "RunFlow", <dir>/result.txt and PNG frames
//     when `-pn-run-flow-output=<dir>` is given, and the static `Result` / `Finished`. Not production code.
// VI: Review tùy chọn cho run roguelite trên luồng thật, dùng lệnh tương tác thật (công tắc, cổng, đài/cửa hàng hồi máu) và chụp khung hình.
public sealed class PNRunFlowPlayReview : MonoBehaviour
{
    public static string Result { get; private set; } = "";
    public static bool Finished { get; private set; }
    // null = the room-to-room walk; "defeat" = the last player falls; "victory" = clear the run and take the last gate.
    public static string Scenario;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pn-run-flow-review") < 0) return;
        const string flag = "-pn-run-flow-scenario=";
        var named = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(flag, StringComparison.Ordinal));
        if (named != null) Scenario = named.Substring(flag.Length);
        Begin();
    }
    // `seed` replays a known run: seed 1 puts the shop behind the first gate, seed 35 the rest room.
    public static void Begin(ulong? seed = null)
    {
        if (FindAnyObjectByType<PNRunFlowPlayReview>() != null) return;
        Result = ""; Finished = false; PNGameFlow.DebugSeed = seed;
        var host = new GameObject("PNRunFlowPlayReview"); DontDestroyOnLoad(host); host.AddComponent<PNRunFlowPlayReview>();
    }

    private readonly List<string> lines = new List<string>();
    private bool failed;
    private int shot;
    private string output;
    private void Pass(string what) { lines.Add("PASS " + what); Debug.Log("RunFlow PASS " + what); }
    private void Fail(string what) { failed = true; lines.Add("FAIL " + what); Debug.LogError("RunFlow FAIL " + what); }
    private bool Check(bool value, string what) { if (value) Pass(what); else Fail(what); return value; }

    private IEnumerator Until(string what, Func<bool> condition, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > end) { Fail("timeout: " + what); yield break; } yield return null; }
    }
    private static IEnumerator Seconds(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }
    private IEnumerator Shot(string label)
    {
        yield return new WaitForEndOfFrame();
        var dir = output ?? Path.Combine(Application.temporaryCachePath, "RunFlow"); Directory.CreateDirectory(dir);
        var texture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, (shot++).ToString("00") + "_" + label + ".png"), texture.EncodeToPNG()); Destroy(texture);
        lines.Add("frame " + Path.Combine(dir, (shot - 1).ToString("00") + "_" + label + ".png"));
    }

    private static PNGameSession Game => FindAnyObjectByType<PNGameSession>();
    private static EveEntityLoader Player => Game.Session.Entities.Snapshot().First(x => x.IsReady && PNEntityRoles.IsPlayer(x));
    private static int Worlds(string definition) => Game.Session.Worlds.Snapshot().Count(x => x.DefinitionId.ToString() == definition && x.State != EveWorldState.Unloading && x.State != EveWorldState.Disposed);
    private static PNInteractable Item(PNRoomDirector room, string key)
        => Game.Sync.ObjectsOf(room.World.InstanceId, room.World.Incarnation).Select(x => (x.Value as PNWorldObject)?.GetComponent<PNInteractable>()).First(x => x != null && x.Key == key);

    // Put the player's body next to an object (the room's floor is flat, so a straight move is enough) and give the scene a moment to settle.
    private IEnumerator WalkTo(PNInteractable item, float side = 0f)
    {
        var player = Player; var body = player.GetComponent<Rigidbody>();
        var target = item.AnchorPosition + new Vector3(side, 0, -1f); target.y = player.transform.position.y;
        player.transform.position = target; if (body != null) { body.position = target; body.linearVelocity = Vector3.zero; }
        yield return Seconds(.4f);
    }

    private void Kill(PNRoomDirector room)
    {
        var source = PNCombatSource.Capture(Player.GetComponent<PNStat>());
        foreach (var enemy in room.Enemies.ToArray())
        {
            var stat = enemy.GetComponent<PNStat>();
            if (stat != null && !stat.IsDead) stat.ReceiveDamage(source.Snapshot.CreateDamage(1e12, false, 0, PNContentIds.True));
        }
    }

    private IEnumerator Start()
    {
        output = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("-pn-run-flow-output=", StringComparison.Ordinal))?.Substring("-pn-run-flow-output=".Length);
        // A review never touches the real saved run: it writes to its own folder, removed at the end.
        saveFolder = Path.Combine(Application.temporaryCachePath, "RunFlowSave_" + Guid.NewGuid().ToString("N"));
        saveStore = new PNRunSaveStore(saveFolder); PNRunSaveService.Use(saveStore);
        // Nor the real profile: a run that ends pays stella into the profile, so the review keeps its own (removed at the end).
        profileFolder = Path.Combine(Application.temporaryCachePath, "RunFlowProfile_" + Guid.NewGuid().ToString("N"));
        PNPlayerProfileService.Load(new PNProfileStore(profileFolder)); PNStellaService.ResetStatics();
        yield return Until("the Hall is up", () => EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy, 90);
        if (!failed && Scenario != "ui")
        {
            PNGameFlow.StartRoguelite(PNFlowMode.Offline);
            yield return Until("the run is playing", () => EveRegistrySession.IsReady && Game != null && Game.Runs.Phase == PNRunPhase.Playing && Game.Runs.Room != null && !PNGameFlow.Busy, 90);
        }
        if (!failed && Scenario == "defeat") yield return Defeat();
        else if (!failed && Scenario == "victory") yield return Victory();
        else if (!failed && (Scenario == "gamepad" || Scenario == "touch")) yield return Devices(Scenario == "gamepad");
        else if (!failed && Scenario == "save") yield return Saving();
        else if (!failed && Scenario == "retire") yield return Retiring();
        else if (!failed && Scenario == "ui") yield return MenusDrawn();
        else if (!failed) yield return FirstRoom();
        if (!failed && Scenario == null) yield return SecondRoom();
        Finish();
    }

    private IEnumerator FirstRoom()
    {
        var runs = Game.Runs; var room = runs.Room;
        Check(PNGameFlow.Phase == PNFlowPhase.Run, "the flow reports the Run phase in a map world");
        Check(room.Definition.Id.ToString() == "project_n:room_star_combat_a" && room.Flow.Phase == PNRoomPhase.Waiting && room.StartAvailable, "the first room is the only room of step 0 and waits for its switch");
        yield return Until("the lobby is gone", () => Worlds("project_n:roguelite_lobby") == 0, 20);
        Check(Worlds("project_n:map_star_test_01") == 1 && Player.LoadContext.Identity.World.DefinitionId.ToString() == "project_n:map_star_test_01", "one map world is loaded and the player stands in it");
        Check(Player.GetComponent<PNStat>().CharacterDefinitionId == "project_n:eve" && PNLocalWorldContext.Current.Actor == Player, "the player is the picked character and the local view follows it");

        // ---- the start switch is the only open object ----
        yield return new WaitForSeconds(.2f);
        var objects = Game.Sync.ObjectsOf(room.World.InstanceId, room.World.Incarnation).Select(x => (x.Value as PNWorldObject)?.GetComponent<PNInteractable>()).Where(x => x != null).ToArray();
        var open = objects.Where(x => x.Available).Select(x => x.Key).ToArray();
        Check(objects.Length == 6 && open.Length == 1 && open[0] == "start_switch", "of the six interactables of the map only the start switch is open");
        var start = Item(room, "start_switch");
        Check(start.Definition.Settings.Permission == PNInteractionPermission.Host && start.UsesLeft == 1, "it is a host-only, once-only switch");
        Game.Sync.Send(start.Target, PNInteractable.Command);
        Check(room.Flow.Phase == PNRoomPhase.Waiting, "used from the player's spawn (out of range) it does nothing");
        yield return WalkTo(start);
        Game.Sync.ReceiveCommand(1, start.Target, PNInteractable.Command, null);
        Check(room.Flow.Phase == PNRoomPhase.Waiting, "a command from another peer (not the host) is refused");
        var player = Player; var scan = new PNInteractionScanner(); scan.Refresh(Game, player);
        Check(scan.Entries.Count == 1 && scan.Entries[0].Item == start && scan.Entries[0].Usable && scan.Selected == 0, "next to it the scanner lists exactly the switch, usable and selected");
        yield return Shot("switch_list");
        Check(scan.UseSelected(Game) && room.Flow.Phase == PNRoomPhase.Countdown && Math.Abs(room.Flow.CountdownRemaining - 3) < .6, "the host uses it: the 3 s countdown starts");
        yield return new WaitForSeconds(.3f);
        Check(!start.Available && start.UsesLeft == 0, "and the switch is spent and closed");
        yield return Shot("countdown");
        float pressed = Time.realtimeSinceStartup;
        yield return Until("wave 1 spawned", () => room.Flow.Phase == PNRoomPhase.Combat && room.Enemies.Count == 2, 20);
        Check(!failed && Time.realtimeSinceStartup - pressed > 2f && Time.realtimeSinceStartup - pressed < 6f, "wave 1 comes after the countdown, not before");
        if (failed) yield break;
        var lease = PNWorldNavigation.Find(room.World);
        Check(lease != null && room.Enemies.All(e => lease.TryProject(e.transform.position, .5f, out _)), "both enemies stand on the navigation mesh");
        Check(room.Enemies.All(e => e.LoadContext.Identity.World == room.World && e.GetComponent<PNStat>().CharacterDefinitionId == "project_n:starweaver"), "they are Starweavers in the room's world");
        yield return Shot("combat");
        Kill(room);
        yield return Until("wave 2 spawned", () => room.Flow.WaveIndex == 1 && room.Enemies.Count == 3, 20);
        Check(!failed && room.Enemies[2].GetComponent<PNStat>().CharacterDefinitionId == "project_n:starkeeper" && runs.State.Stats.Kills == 2, "wave 2 (a Starkeeper) follows once wave 1 is dead, 2 kills counted");
        Kill(room);
        yield return Until("room cleared", () => room.Flow.Phase == PNRoomPhase.Cleared, 20);
        yield return new WaitForSeconds(.3f);
        var exits = runs.Exits();
        Check(!failed && runs.State.Shards == 30 && runs.State.Stats.RoomsClearedTotal == 1 && runs.State.Stats.Kills == 3, "clearing pays 30, counts the room and 3 kills");
        Check(exits.Count == 2 && exits[0] != exits[1], "two different gates were drawn");
        var gates = new[] { "gate_a", "gate_b", "gate_c" }.Select(k => Item(room, k)).ToArray();
        Check(gates[0].Available && gates[1].Available && !gates[2].Available && gates[0].LabelKey == PNRoomInteractions.KindKey(runs.KindOf(exits[0])) && gates[1].LabelKey == PNRoomInteractions.KindKey(runs.KindOf(exits[1])), "two gates open, each saying the kind of room behind it; the third stays closed");
        yield return WalkTo(gates[0]);
        scan.Refresh(Game, Player);
        Check(scan.Entries.Count == 1 && scan.Entries[0].Item == gates[0] && scan.Entries[0].Usable, "next to the first gate the list shows it");
        yield return Seconds(.4f);
        var kindIcon = Ui.HudRoot?.Q("interact-kind-icon");
        Check(kindIcon != null && kindIcon.resolvedStyle.backgroundImage.texture != null && kindIcon.resolvedStyle.width > 10, "the gate's row shows the icon of the kind of room behind it, not the star");
        yield return Shot("gate_list");
    }

    // ---- the saved run (Docs/RunSavePlan.md S4/S5) ----
    // Seed 1: first room (combat), clear it, hurt the player to half health, take a gate into the shop; the run is saved on entering each room; leave to the Hall (the save stays); continue it:
    // the same room, the same wallet and stats, the same health ratio (not full), the same gates behind the room (the random streams carry on); a lost run deletes the save.
    private string saveFolder, profileFolder;
    private PNRunSaveStore saveStore;

    // ---- the new menus, drawn (Docs/RunSavePlan.md S7): the map screen, the Continue / Sum up screen (playable and not), the summary of a summed-up run, the pause menu with Play again ----
    private PNRunSave SavedRun(string rulesVersion)
    {
        var hub = EveRegistrySession.Current; var run = hub.RogueliteRuns().Get(EveId.Parse("project_n:standard_run"));
        var planner = new PNRunPlanner(run, id => hub.RogueliteFloors().Get(id));
        var state = planner.Begin(Guid.NewGuid(), 7UL, new[] { new KeyValuePair<long, EveId>(0, EveId.Parse("project_n:eve")) }, owner => PNPlayerProfileService.PlayerId);
        state.Stats.AddKills(4); state.Stats.AddTime(95); state.Stats.AddCleared(PNRoomKind.Combat); state.AddShards(30, "room");
        return PNRunSave.Capture(state, rulesVersion, PNRunSaveService.ContentHash(), "roguelite", DateTime.UtcNow);
    }
    private IEnumerator MenusDrawn()
    {
        PNUiRuntime ui = null;
        yield return Until("the Hall UI is up", () => EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && (ui = PNGameBootstrap.Runtime?.GetComponent<PNUiRuntime>()) != null && ui.MenuFlow != null, 90);
        if (failed) yield break;
        yield return Seconds(1f);
        var flow = ui.MenuFlow; var controller = new PNMenuController(ui);

        // ---- the map screen (no save) ----
        Check(controller.OpenHall(), "the Hall menu opens");
        yield return Until("the mode ring", () => flow.Current is PNModeScreen && !flow.IsBusy, 10);
        ((PNModeScreen)flow.Current).Pick();
        yield return Until("the setup screen", () => flow.Current is PNSetupScreen && !flow.IsBusy, 10);
        ((PNSetupScreen)flow.Current).Choose(0);
        yield return Until("the map screen", () => flow.Current is PNRunMapScreen && !flow.IsBusy, 10);
        if (failed) yield break;
        Check(flow.Current.Items.Count == 1, "with one map the map screen still shows it");
        yield return Seconds(.8f); yield return Shot("map");
        flow.RequestBack(); yield return Seconds(1.2f); flow.RequestBack(); yield return Seconds(1.2f); flow.RequestBack(); yield return Seconds(1f);
        Check(!flow.IsOpen, "back closes the menu");

        // ---- a saved run that can be continued ----
        saveStore.Save(SavedRun(EveGameRuntime.CurrentRulesVersion));
        controller.OpenHall(); yield return Until("the mode ring", () => flow.Current is PNModeScreen && !flow.IsBusy, 10);
        ((PNModeScreen)flow.Current).Pick(); yield return Until("the setup screen", () => flow.Current is PNSetupScreen && !flow.IsBusy, 10);
        ((PNSetupScreen)flow.Current).Choose(0); yield return Until("the resume screen", () => flow.Current is PNResumeScreen && !flow.IsBusy, 10);
        if (failed) yield break;
        Check(((PNResumeScreen)flow.Current).CanContinue, "a save made with this content can be continued");
        yield return Seconds(.8f); yield return Shot("resume");
        flow.RequestBack(); yield return Seconds(1.2f); flow.RequestBack(); yield return Seconds(1.2f); flow.RequestBack(); yield return Seconds(1f);

        // ---- a saved run of another game version: Continue is off, the reason is told ----
        saveStore.Save(SavedRun("project_n:1"));
        controller.OpenHall(); yield return Until("the mode ring", () => flow.Current is PNModeScreen && !flow.IsBusy, 10);
        ((PNModeScreen)flow.Current).Pick(); yield return Until("the setup screen", () => flow.Current is PNSetupScreen && !flow.IsBusy, 10);
        ((PNSetupScreen)flow.Current).Choose(0); yield return Until("the resume screen", () => flow.Current is PNResumeScreen && !flow.IsBusy, 10);
        if (failed) yield break;
        Check(!((PNResumeScreen)flow.Current).CanContinue, "a save of another version cannot be continued");
        yield return Seconds(.8f); yield return Shot("resume_error");

        // ---- sum up: the end screen shows the run as stopped, then the button dismisses it ----
        ((PNResumeScreen)flow.Current).SumUp();
        yield return Until("the end screen", () => EndShown(), 10);
        Check(EndShown() && saveStore.Load().Status == PNRunSaveStatus.Missing, "summing up shows the end screen and frees the slot");
        yield return Seconds(.6f); yield return Shot("retired");
        PNRunSummary.Clear(); yield return Seconds(.4f);
        Check(!EndShown(), "the summary is dismissed");

        // ---- Play again in the pause menu, inside a run that has a save ----
        PNGameFlow.NextRun = null; PNGameFlow.StartRoguelite(PNFlowMode.Offline);
        yield return Until("the run is playing", () => EveRegistrySession.IsReady && Game != null && Game.Runs.Phase == PNRunPhase.Playing && Game.Runs.Room != null && !PNGameFlow.Busy, 90);
        if (failed) yield break;
        ui = PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>(); yield return Seconds(.6f);
        ui.Pause.Open(); yield return Seconds(1.4f);
        var restart = ui.HudRoot.Q<Button>("pause-restart");
        Check(restart != null && restart.resolvedStyle.display == DisplayStyle.Flex, "the pause menu offers Play again inside a run that is saved");
        yield return Shot("pause_restart");
        ui.Pause.Close(); yield return Seconds(1f);
    }
    private IEnumerator Saving()
    {
        var runs = Game.Runs; string me = PNPlayerProfileService.PlayerId;
        var first = PNRunSaveService.Peek();
        Check(first.Status == PNRunSaveStatus.Loaded && first.Save.Room == runs.Room.Definition.Id && first.Save.Floor == 0 && first.Save.Step == 0 && first.Save.Options.Count == 0 && first.Save.Shards == 0, "entering the first room saves the run at its start");
        Check(first.Save.Seats.Count == 1 && first.Save.Seats[0].PlayerId == me && first.Save.Seats[0].Health == 1 && !first.Save.Seats[0].Dead && first.Save.Seats[0].Character.ToString() == "project_n:eve", "one seat, this person, full health, the picked character");
        var current = first.Save;
        var stale = PNRunSave.Parse(current.ToJson().Replace(current.RulesVersion, "project_n:1"), out _);
        var gone = PNRunSave.Parse(current.ToJson().Replace("\"definition\": \"project_n:standard_run\"", "\"definition\": \"project_n:gone\""), out _);
        var nobody = PNRunSave.Parse(current.ToJson().Replace("\"character\": \"project_n:eve\"", "\"character\": \"project_n:nobody\""), out _);
        var hub = EveRegistrySession.Current;
        Check(PNRunSaveService.Check(current, hub) == PNRunSaveProblem.None && PNRunSaveService.Check(stale, hub) == PNRunSaveProblem.Version && PNRunSaveService.Check(gone, hub) == PNRunSaveProblem.Definition && PNRunSaveService.Check(nobody, hub) == PNRunSaveProblem.Character,
            "a save is playable with the content now loaded; another rules version, a missing map and a missing character are told apart");

        yield return ClearRoom(runs.Room);
        if (failed) yield break;
        var stat = Player.GetComponent<PNStat>(); var source = PNCombatSource.Capture(stat);
        stat.ReceiveDamage(source.Snapshot.CreateDamage(stat.GetMax(PNContentIds.Health) / 2, false, 0, PNContentIds.True));
        double ratio = stat.GetCurrent(PNContentIds.Health) / stat.GetMax(PNContentIds.Health);
        var chosen = runs.Exits()[0]; var oldWorld = runs.Room.World; var gate = Item(runs.Room, "gate_a");
        yield return WalkTo(gate); Game.Sync.Send(gate.Target, PNInteractable.Command);
        yield return Until("the next room is playing", () => runs.Phase == PNRunPhase.Playing && runs.Room != null && runs.Room.World != oldWorld && !PNGameFlow.Busy, 60);
        if (failed) yield break;
        yield return Seconds(.4f);
        var second = PNRunSaveService.Peek().Save;
        Check(second != null && second.RunId == first.Save.RunId && second.Step == 1 && second.Room == chosen && second.Options.Count == 0 && second.Shards == 30 && second.Kills == 3 && second.Cleared[PNRoomKind.Combat] == 1,
            "entering the second room saved it: the same run, one step on, the room behind the gate, the wallet and stats of the first room, no gates drawn yet");
        Check(Math.Abs(second.Seats[0].Health - ratio) < .02, "and the health ratio the person carried (" + ratio.ToString("0.00") + ")");
        var exits = runs.Room.Definition.HasCombat ? null : runs.Exits().ToArray();
        Check(exits != null, "this seed puts a room without combat behind the first gate (its gates are drawn at once)");
        if (failed) yield break;

        // What the run holds with the second room entered (a room without combat is cleared at once, so it is already counted).
        long shardsHeld = runs.State.Shards; long killsHeld = runs.State.Stats.Kills; int roomsHeld = runs.State.Stats.RoomsClearedTotal; int combatHeld = runs.State.Stats.RoomsCleared(PNRoomKind.Combat), otherHeld = roomsHeld - combatHeld;
        // ---- leave: the save stays ----
        PNGameFlow.ReturnToHall();
        yield return Until("the Hall is up", () => EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy, 60);
        if (failed) yield break;
        var kept = PNRunSaveService.Peek();
        Check(kept.Status == PNRunSaveStatus.Loaded && kept.Save.RunId == first.Save.RunId && kept.Save.Step == 1, "leaving to the Hall keeps the saved run");

        // ---- continue ----
        PNGameFlow.NextRun = PNRunRequest.Continue(kept.Save);
        PNGameFlow.StartRoguelite(PNFlowMode.Offline);
        yield return Until("the continued run is playing", () => EveRegistrySession.IsReady && Game != null && Game.Runs.Phase == PNRunPhase.Playing && Game.Runs.Room != null && !PNGameFlow.Busy, 90);
        if (failed) yield break;
        runs = Game.Runs; yield return Seconds(.6f);
        Check(runs.State.RunId == first.Save.RunId && runs.Room.Definition.Id == chosen && runs.State.Step == 1 && runs.State.Floor == 0, "the continued run is the same run, in the room it was saved at");
        Check(runs.State.Shards == shardsHeld && runs.State.Stats.Kills == killsHeld && runs.State.Stats.RoomsClearedTotal == roomsHeld && roomsHeld == 2 && runs.State.Stats.RoomsCleared(PNRoomKind.Combat) == combatHeld && otherHeld == 1, "with the wallet and stats the run held (30 shards, 3 kills, the combat room and this room, each counted once)");
        var back = Player.GetComponent<PNStat>();
        Check(Math.Abs(back.GetCurrent(PNContentIds.Health) / back.GetMax(PNContentIds.Health) - ratio) < .02 && back.CharacterDefinitionId == "project_n:eve", "the player has the saved health ratio, not the full health of the lobby body");
        Check(runs.Room.Flow.Phase == PNRoomPhase.Cleared && runs.Exits().SequenceEqual(exits), "the room is cleared again at once and draws the same gates as before: the random streams carried on");
        var again = PNRunSaveService.Peek().Save;
        Check(again != null && again.Step == 1 && again.Room == chosen && again.RunId == first.Save.RunId, "the continued run is saved again on entering its room");

        // ---- Play again (the pause menu's button): the same path as Continue, from inside the run ----
        Check(PNGameFlow.CanRestartFromSave(), "Play again is offered inside a run with a playable save");
        var before = runs;
        Check(PNGameFlow.RestartFromSave(out var why), "Play again starts the saved run again (" + why + ")");
        yield return Seconds(1.5f);
        yield return Until("the run is playing again", () => EveRegistrySession.IsReady && Game != null && !ReferenceEquals(Game.Runs, before) && Game.Runs.Phase == PNRunPhase.Playing && Game.Runs.Room != null && !PNGameFlow.Busy, 90);
        if (failed) yield break;
        runs = Game.Runs; yield return Seconds(.6f);
        Check(runs.State.RunId == first.Save.RunId && runs.Room.Definition.Id == chosen && runs.State.Step == 1 && runs.State.Shards == shardsHeld, "Play again is the same run, in the same room, with the same wallet");
        var replayed = Player.GetComponent<PNStat>();
        Check(Math.Abs(replayed.GetCurrent(PNContentIds.Health) / replayed.GetMax(PNContentIds.Health) - ratio) < .02, "and the same health ratio");

        // ---- on to the last room, then a lost run deletes the save ----
        var shopRoom = runs.Room; var shopWorld = shopRoom.World; var onward = Item(shopRoom, "gate_a");
        yield return WalkTo(onward); Game.Sync.Send(onward.Target, PNInteractable.Command);
        yield return Until("the last room is playing", () => runs.Phase == PNRunPhase.Playing && runs.Room != null && runs.Room.World != shopWorld && !PNGameFlow.Busy, 60);
        if (failed) yield break;
        yield return Seconds(.4f);
        var third = PNRunSaveService.Peek().Save;
        Check(third != null && third.Step == 2 && third.Room == runs.Room.Definition.Id && third.RunId == first.Save.RunId, "the continued run goes on and saves its next room");
        var hurt = Player.GetComponent<PNStat>();
        hurt.ReceiveDamage(PNCombatSource.Capture(hurt).Snapshot.CreateDamage(1e12, false, 0, PNContentIds.True));
        yield return Until("the run ended", () => runs.Phase == PNRunPhase.Ended, 12);
        Check(!failed && runs.State.Result == PNRunResult.Defeat && PNRunSaveService.Peek().Status == PNRunSaveStatus.Missing, "a lost run deletes the saved run");
        yield return LeaveByButton();
    }

    private IEnumerator SecondRoom()
    {
        var runs = Game.Runs; var before = Player; long oldId = before.LoadContext.Identity.Id.Value;
        var stat = before.GetComponent<PNStat>(); var source = PNCombatSource.Capture(stat);
        stat.ReceiveDamage(source.Snapshot.CreateDamage(stat.GetMax(PNContentIds.Health) / 2, false, 0, PNContentIds.True));
        double ratio = stat.GetCurrent(PNContentIds.Health) / stat.GetMax(PNContentIds.Health);
        var chosen = runs.Exits()[0]; var oldWorld = runs.Room.World; var gate = Item(runs.Room, "gate_a");
        Game.Sync.Send(gate.Target, PNInteractable.Command);
        yield return Until("the next room is playing", () => runs.Phase == PNRunPhase.Playing && runs.Room != null && runs.Room.World != oldWorld && !PNGameFlow.Busy, 60);
        if (failed) yield break;
        var after = Player; var afterStat = after.GetComponent<PNStat>();
        Check(runs.Room.Definition.Id == chosen && runs.State.Step == 1, "the first gate leads to the room that was drawn behind it");
        Check(after.LoadContext.Identity.Id.Value != oldId && !ReferenceEquals(after, before), "the player was rebuilt: a new entity");
        Check(Math.Abs(afterStat.GetCurrent(PNContentIds.Health) / afterStat.GetMax(PNContentIds.Health) - ratio) < .02, "and it kept its health ratio (" + ratio.ToString("0.00") + ")");
        Check(PNLocalWorldContext.Current.Actor == after && after.LoadContext.Identity.World == runs.Room.World, "the local view follows the new player in the new world");
        yield return Until("the old room is unloaded", () => Worlds("project_n:map_star_test_01") == 1, 20);
        Check(!failed, "only the new room's world is left");
        yield return new WaitForSeconds(.3f);
        var room = runs.Room; var kind = room.Definition.Kind;
        var openGates = Enumerable.Range(0, 3).Select(i => Item(room, new[] { "gate_a", "gate_b", "gate_c" }[i])).Count(g => g.Available);
        if (room.Definition.HasCombat) Check(openGates == 0, "a combat room's gates are closed until it is cleared");
        else Check(room.Flow.Phase == PNRoomPhase.Cleared && openGates == runs.Exits().Count, "a room without combat is cleared at once and its gates (" + openGates + ") are open");
        yield return Shot("second_room");
        if (kind == PNRoomKind.Rest) yield return Fountain(room);
        else if (kind == PNRoomKind.Shop) yield return Shop(room);
    }

    // A rest room opens its fountain at once; it heals half of the maximum health, once.
    private IEnumerator Fountain(PNRoomDirector room)
    {
        var fountain = Item(room, "fountain");
        Check(fountain.Available && room.Flow.Phase == PNRoomPhase.Cleared, "a rest room is cleared at once and its fountain is open");
        yield return WalkTo(fountain);
        var stat = Player.GetComponent<PNStat>(); double before = stat.GetCurrent(PNContentIds.Health), max = stat.GetMax(PNContentIds.Health);
        Check(Game.Sync.Send(fountain.Target, PNInteractable.Command) && Math.Abs(stat.GetCurrent(PNContentIds.Health) - Math.Min(max, before + max * .5)) < 2, "the fountain heals half of the maximum health");
        Check(!fountain.Available && fountain.UsesLeft == 0, "and is spent");
    }

    // A shop room opens its heal; it costs the team wallet (30 from the cleared room) and heals fully; with no shards left the entry is dimmed.
    private IEnumerator Shop(PNRoomDirector room)
    {
        var shop = Item(room, "shop_heal"); var runs = Game.Runs;
        Check(shop.Available && runs.State.Shards == 30, "a shop room opens its heal and the team holds 30");
        yield return WalkTo(shop);
        var stat = Player.GetComponent<PNStat>(); var scan = new PNInteractionScanner(); scan.Refresh(Game, Player);
        Check(scan.Entries.Count == 1 && scan.Entries[0].Usable, "with the shards, the entry is usable");
        Game.Sync.Send(shop.Target, PNInteractable.Command);
        Check(runs.State.Shards == 0 && Math.Abs(stat.GetCurrent(PNContentIds.Health) - stat.GetMax(PNContentIds.Health)) < 2, "paying 30 heals to full and empties the wallet");
        yield return new WaitForSeconds(.2f); scan.Refresh(Game, Player);
        Check(scan.Entries.Count == 1 && !scan.Entries[0].Usable, "with no shards left the entry is dimmed");
        yield return Shot("shop_dimmed");
    }

    // ---- end of run (phase E) ----
    private static VisualElement EndRoot() => FindObjectsByType<UIDocument>(FindObjectsInactive.Include).Select(d => d.rootVisualElement?.Q("run-end")).FirstOrDefault(x => x != null);
    private static bool EndShown() { var root = EndRoot(); return root != null && root.resolvedStyle.display == DisplayStyle.Flex; }

    private IEnumerator ClearRoom(PNRoomDirector room)
    {
        if (room.Flow.Phase == PNRoomPhase.Waiting) { var start = Item(room, "start_switch"); yield return WalkTo(start); Game.Sync.Send(start.Target, PNInteractable.Command); }
        float end = Time.realtimeSinceStartup + 90f;
        while (room.Flow.Phase != PNRoomPhase.Cleared && Time.realtimeSinceStartup < end) { Kill(room); yield return null; }
        if (room.Flow.Phase != PNRoomPhase.Cleared) Fail("timeout: room cleared");
        yield return Seconds(.3f);
    }

    private IEnumerator Defeat()
    {
        var runs = Game.Runs; var room = runs.Room;
        var start = Item(room, "start_switch"); yield return WalkTo(start); Game.Sync.Send(start.Target, PNInteractable.Command);
        yield return Until("combat", () => room.Flow.Phase == PNRoomPhase.Combat, 20);
        var stat = Player.GetComponent<PNStat>(); var source = PNCombatSource.Capture(stat);
        stat.ReceiveDamage(source.Snapshot.CreateDamage(1e12, false, 0, PNContentIds.True));
        float fell = Time.realtimeSinceStartup;
        yield return Until("the room sees every player dead", () => room.Flow.Phase == PNRoomPhase.Defeated, 5);
        Check(runs.Phase == PNRunPhase.Playing && runs.State.IsRunning && !EndShown(), "the last player fell: the run is not lost yet (the delay runs)");
        yield return Until("the run ended", () => runs.Phase == PNRunPhase.Ended, 8);
        Check(!failed && runs.State.Result == PNRunResult.Defeat && Time.realtimeSinceStartup - fell >= PNRunDirector.DefeatDelaySeconds - .05f, "after the delay the run is lost");
        yield return Seconds(.4f);
        Check(EndShown(), "the end screen is shown");
        yield return Shot("end_defeat");
        yield return LeaveByButton();
    }

    private IEnumerator Victory()
    {
        var runs = Game.Runs;
        for (int guard = 0; guard < 6 && !failed; guard++)
        {
            var room = runs.Room;
            yield return ClearRoom(room);
            if (failed) yield break;
            var gate = new[] { "gate_a", "gate_b", "gate_c" }.Select(k => Item(room, k)).FirstOrDefault(g => g.Available);
            if (gate == null) { Fail("no open gate after " + room.Definition.Id); yield break; }
            bool last = runs.IsLastRoom; var old = room.World;
            yield return WalkTo(gate); Game.Sync.Send(gate.Target, PNInteractable.Command);
            if (last) break;
            yield return Until("the next room", () => runs.Phase == PNRunPhase.Playing && runs.Room != null && runs.Room.World != old && !PNGameFlow.Busy, 60);
        }
        if (failed) yield break;
        Check(runs.Phase == PNRunPhase.Ended && runs.State.Result == PNRunResult.Victory, "the last gate wins the run");
        yield return Seconds(.4f);
        var s = runs.State.Stats;
        Check(EndShown() && s.RoomsClearedTotal == 3 && s.Kills > 0 && s.ShardsEarned >= 30 && s.Seconds > 0, "the end screen shows a summary (" + s.RoomsClearedTotal + " rooms, " + s.Kills + " kills, " + s.ShardsEarned + " shards)");
        yield return Shot("end_victory");
        // Stella (Docs/StellaPlan.md): this person was paid once for the run: one floor passed + a tenth of the shards earned + a roll of at most 3 per floor.
        var definition = runs.State.Definition; var rule = Game.Session.Registry.RogueliteRuns().Get(definition).Stella;
        int expectedBase = (int)Math.Floor(10.0 * 1 + 0.1 * s.ShardsEarned);
        int balance = PNPlayerProfileService.Stella;
        Check(rule != null && balance >= expectedBase && balance <= expectedBase + 3 && balance == PNStella.Compute(rule, 1, s.ShardsEarned, runs.State.RunId, PNPlayerProfileService.PlayerId), "the victory paid this person's stella once (" + balance + ", base " + expectedBase + ")");
        Check(PNPlayerProfileService.Current.StellaRun == runs.State.RunId.ToString("N"), "the profile remembers the run it paid");
        var endRows = EndRoot().Query<Label>(className: "pn-end-label").ToList().Select(x => x.text).ToList();
        Check(endRows.Contains(PNLocalization.Text("project_n:run.end.stella")), "the end screen has a stella row");
        yield return Seconds(.6f);
        Check(PNPlayerProfileService.Stella == balance && PNStellaService.Settle(PNRunStateSyncs.Current()) == balance, "the screen redrawing does not pay the same run twice");
        yield return LeaveByButton();
    }

    // "Sum up now" in the pause menu while a run is played (Docs/StellaPlan.md Q6): the first press asks, the second ends the run as retired; standing on the first floor it pays nothing.
    private IEnumerator Retiring()
    {
        var runs = Game.Runs; var ui = PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>(); yield return Seconds(.6f);
        Check(PNGameFlow.CanRetireRun(), "alone, while the run is played, Sum up now is possible");
        ui.Pause.Open(); yield return Seconds(1.4f);
        var retire = ui.HudRoot.Q<Button>("pause-retire");
        Check(retire != null && retire.resolvedStyle.display == DisplayStyle.Flex, "the pause menu offers Sum up now inside a run");
        yield return Shot("pause_retire");
        if (retire == null) yield break;
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = retire; retire.SendEvent(submit); }
        yield return Seconds(.4f);
        Check(runs.Phase == PNRunPhase.Playing && runs.State.IsRunning && retire.text == PNLocalization.Text("project_n:run.sumup.confirm"), "the first press only asks: the run goes on and the button says so");
        yield return Shot("pause_retire_confirm");
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = retire; retire.SendEvent(submit); }
        yield return Until("the run ended", () => runs.Phase == PNRunPhase.Ended, 8);
        Check(!failed && runs.State.Result == PNRunResult.Retired, "the second press ends the run as retired");
        Check(saveStore.Load().Status == PNRunSaveStatus.Missing, "summing up frees the saved run");
        yield return Seconds(.6f);
        Check(EndShown(), "the end screen is shown for a retired run");
        var labels = EndRoot().Query<Label>(className: "pn-end-label").ToList().Select(x => x.text).ToList();
        Check(PNPlayerProfileService.Stella == 0 && !labels.Contains(PNLocalization.Text("project_n:run.end.stella")), "standing on the first floor pays no stella and shows no stella row");
        Check(PNStellaService.Settle(PNRunStateSyncs.Current()) == 0, "settling the same run again still gives nothing");
        yield return Shot("end_retired_in_run");
        yield return LeaveByButton();
        Check(!PNGameFlow.CanRetireRun(), "back in the Hall there is nothing to sum up");
    }

    private IEnumerator LeaveByButton()
    {
        var button = EndRoot()?.Q<Button>();
        if (button == null) { Fail("the end screen has a button"); yield break; }
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        yield return Until("back in the Hall", () => EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy, 60);
        Check(!failed && Game.Runs.Phase == PNRunPhase.None && !EndShown(), "the button leads back to the Hall and the end screen is gone");
    }

    // ---- gamepad and touch on the interaction list and the end button ----
    // The real devices are virtual ones fed through the Input System (a Gamepad / a Touchscreen), so the list's InputActions and the UI input module see what a pad or a finger would send.
    private Gamepad pad;
    private Touchscreen screen;
    private InputSettings oldSettings, settings;
    private static PNUiRuntime Ui => FindAnyObjectByType<PNUiRuntime>();
    private static VisualElement ListRoot() => Ui.HudRoot?.Q("interaction-list");
    private static List<VisualElement> Rows() { var root = ListRoot(); return root == null || root.resolvedStyle.display != DisplayStyle.Flex ? new List<VisualElement>() : root.Query<VisualElement>(className: "pn-interact-row").ToList(); }
    private static int SelectedRow() => Rows().FindIndex(x => x.ClassListContains("pn-interact-row-selected"));
    private static string RowText(VisualElement row) => row.Q<Label>(className: "pn-interact-label")?.text ?? "";

    private void PrepareInput()
    {
        oldSettings = InputSystem.settings; settings = Instantiate(oldSettings);
        if (oldSettings.hideFlags == HideFlags.HideAndDontSave) oldSettings.hideFlags = HideFlags.DontSave;
        InputSystem.settings = settings; settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
    }

    private void CleanupInput()
    {
        if (pad != null) { InputSystem.RemoveDevice(pad); pad = null; }
        if (screen != null) { InputSystem.RemoveDevice(screen); screen = null; }
        if (settings != null) { if (oldSettings != null) InputSystem.settings = oldSettings; Destroy(settings); settings = null; }
        var ui = Ui; if (ui != null) { ui.ShowMobileControls = false; }
    }

    private IEnumerator Press(params GamepadButton[] buttons)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState(buttons)); yield return Seconds(.12f);
        InputSystem.QueueStateEvent(pad, new GamepadState()); yield return Seconds(.18f);
    }

    private Vector2 ScreenOf(VisualElement element)
    {
        var p = element.worldBound.center; float scale = Screen.width / Ui.HudRoot.resolvedStyle.width;
        return new Vector2(p.x * scale, Screen.height - p.y * scale);
    }

    private IEnumerator Tap(VisualElement element)
    {
        var at = ScreenOf(element);
        InputSystem.QueueStateEvent(screen, new TouchState { touchId = 7, phase = UnityEngine.InputSystem.TouchPhase.Began, position = at }); yield return Seconds(.12f);
        InputSystem.QueueStateEvent(screen, new TouchState { touchId = 7, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = at }); yield return Seconds(.25f);
    }

    private IEnumerator WalkToPoint(Vector3 target)
    {
        var player = Player; var body = player.GetComponent<Rigidbody>(); target.y = player.transform.position.y;
        player.transform.position = target; if (body != null) { body.position = target; body.linearVelocity = Vector3.zero; }
        yield return Seconds(.5f);
    }

    private IEnumerator Devices(bool gamepad)
    {
        string kind = gamepad ? "gamepad" : "touch";
        var runs = Game.Runs; var room = runs.Room;
        PrepareInput();
        if (gamepad) pad = InputSystem.AddDevice<Gamepad>("PNRunReviewPad");
        else { Ui.ShowMobileControls = true; screen = InputSystem.AddDevice<Touchscreen>("PNRunReviewTouch"); }
        yield return Seconds(.5f);

        // ---- the start switch from the list ----
        var start = Item(room, "start_switch"); yield return WalkTo(start); yield return Seconds(.3f);
        var rows = Rows();
        Check(rows.Count == 1 && RowText(rows[0]).Length > 0, kind + ": next to the switch the list shows one row with a label");
        var keyLabel = rows.Count > 0 ? rows[0].Q<Label>(className: "pn-interact-key")?.text : null;
        lines.Add("info " + kind + ": key label on the row = " + (keyLabel ?? "(none)"));
        yield return Shot(kind + "_list_start");
        if (gamepad) yield return Press(GamepadButton.RightStick); else if (rows.Count > 0) yield return Tap(rows[0]);
        Check(room.Flow.Phase == PNRoomPhase.Countdown, kind + ": " + (gamepad ? "the pressed right stick" : "a tap on the row") + " starts the countdown");
        if (failed) yield break;
        yield return ClearRoom(room);
        if (failed) yield break;

        // ---- two gates: move the selection, choose one ----
        var a = Item(room, "gate_a"); var b = Item(room, "gate_b");
        // The authored gates stand far apart, so the list would show one at a time: this disposable review moves the second one beside the first so the list has two rows to move through.
        b.transform.position = a.transform.position + new Vector3(.9f, 0, 0);
        yield return WalkTo(a, .45f);
        yield return Seconds(.3f);
        rows = Rows();
        Check(rows.Count == 2, kind + ": between the two open gates the list shows two rows (" + rows.Count + ")");
        if (rows.Count != 2) { yield return Shot(kind + "_gates_fail"); yield break; }
        int first = SelectedRow(); string firstText = RowText(rows[first]);
        yield return Shot(kind + "_list_gates");
        var gateKey = rows[first].Q<Label>(className: "pn-interact-key")?.text;
        if (gamepad) Check(gateKey == "R3", "gamepad: once the pad was used the selected row names its button (R3), not the keyboard's key (" + (gateKey ?? "none") + ")");
        else Check(gateKey == null, "touch: no key label on a row (the row itself is the button)");
        int target = 1 - first;
        if (gamepad)
        {
            yield return Press(GamepadButton.DpadDown); int afterDown = SelectedRow();
            yield return Press(GamepadButton.DpadLeft); int afterLeft = SelectedRow();
            Check(afterDown == (first + 1) % 2 || afterDown == Mathf.Min(first + 1, 1), kind + ": D-pad down moves the selection (" + first + " -> " + afterDown + ")");
            Check(afterLeft == first, kind + ": D-pad left moves it back (" + afterDown + " -> " + afterLeft + ")");
            if (afterDown == first) { Fail(kind + ": no selection change to select the other row"); yield break; }
            yield return Press(GamepadButton.DpadDown);
        }
        var old = room.World; var chosenWorldBefore = old;
        if (gamepad) yield return Press(GamepadButton.RightStick); else { rows = Rows(); yield return Tap(rows[target]); }
        yield return Until("the next room (" + kind + ")", () => runs.Phase == PNRunPhase.Playing && runs.Room != null && runs.Room.World != chosenWorldBefore && !PNGameFlow.Busy, 60);
        Check(!failed && runs.State.Step == 1, kind + ": the chosen gate led to the next room");
        if (failed) yield break;
        yield return Seconds(.5f);

        // ---- the end screen and its button ----
        var stat = Player.GetComponent<PNStat>(); var source = PNCombatSource.Capture(stat);
        if (runs.Room.Flow.Phase == PNRoomPhase.Waiting && runs.Room.Definition.HasCombat) { var s2 = Item(runs.Room, "start_switch"); yield return WalkTo(s2); Game.Sync.Send(s2.Target, PNInteractable.Command); yield return Until("combat", () => runs.Room.Flow.Phase == PNRoomPhase.Combat, 20); }
        stat.ReceiveDamage(source.Snapshot.CreateDamage(1e12, false, 0, PNContentIds.True));
        yield return Until("the run ended", () => runs.Phase == PNRunPhase.Ended, 15);
        yield return Seconds(.6f);
        Check(!failed && EndShown(), kind + ": the end screen is shown");
        if (failed) yield break;
        var button = EndRoot()?.Q<Button>();
        Check(button != null, kind + ": it has its button");
        if (button == null) yield break;
        if (gamepad)
        {
            Check(button.focusController?.focusedElement == button, "gamepad: the button has the focus when the screen appears");
            yield return Shot("gamepad_end");
            yield return Press(GamepadButton.South);
        }
        else { yield return Shot("touch_end"); yield return Tap(button); }
        yield return Until("back in the Hall (" + kind + ")", () => EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy, 60);
        Check(!failed && Game.Runs.Phase == PNRunPhase.None && !EndShown(), kind + ": the end button led back to the Hall");
    }

    private void Finish()
    {
        CleanupInput();
        PNRunSaveService.ResetStatics(); PNPlayerProfileService.ResetStatics(); PNStellaService.ResetStatics();
        try { if (profileFolder != null && Directory.Exists(profileFolder)) Directory.Delete(profileFolder, true); } catch (IOException) { }
        try { if (saveFolder != null && Directory.Exists(saveFolder)) Directory.Delete(saveFolder, true); } catch (IOException) { }
        Result = (failed ? "FAIL" : "PASS") + " " + lines.Count(x => x.StartsWith("PASS")) + " passed" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        Finished = true; Debug.Log("RunFlow " + (failed ? "FAILED" : "PASSED") + " (" + lines.Count + " lines)");
        if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "result.txt"), Result); }
        if (!Application.isEditor) Application.Quit(failed ? 1 : 0);
        Destroy(gameObject);
    }
}
