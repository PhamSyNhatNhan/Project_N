using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // EN: Two (three) real Player processes for the chunk channel of a grid world (Docs/SandboxPlan.md, step G8). Active only with `--pn-grid-test=<folder> --pn-grid-role=<host|c1|late> --pn-auto-start=hall`.
    //     The host opens a roguelite lobby, creates a grid world beside the lobby world and subscribes each client to it (a second replica, so the lobby keeps running). Then: the client's world must be ready only
    //     after the first set of chunks, its cells must equal the host's, the host's placed and broken things must reach it (with a collider you can hit), a client that joins later must receive the world as it is
    //     now, and a client that leaves and returns must receive it again. The processes talk through small files in the folder. Build the Player from this project with the scenes of the build settings.
    // VI: Hai (ba) Player thật cho kênh mảnh của world lưới (G8). Chỉ chạy với `--pn-grid-test=<thư mục> --pn-grid-role=<host|c1|late> --pn-auto-start=hall`.
    public sealed class PNGridChunkPlayChecks : MonoBehaviour
    {
        private string folder, role;
        private int passes, fails;
        private readonly List<string> log = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private Guid gridId;
        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        private bool Has(string name) => File.Exists(Path.Combine(folder, name));
        private string Read(string name) => File.ReadAllText(Path.Combine(folder, name));
        private void Mark(string name, string body = "ok") { var path = Path.Combine(folder, name); File.WriteAllText(path + ".pending", body); File.Move(path + ".pending", path); }
        private PNNetworkManager Manager => NetworkManager.singleton as PNNetworkManager;
        private void Say(string text) { log.Add(text); Debug.Log("GridChunk " + role + " " + text); }
        private void Verdict(string name, bool ok, string detail = null) { if (ok) passes++; else fails++; Say((ok ? "PASS " : "FAIL ") + name + (detail != null ? "  [" + detail + "]" : "")); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Early()
        {
            if (Arg("--pn-grid-test=") == null) return;
            Application.logMessageReceived += (condition, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) lock (errors) errors.Add(type + ": " + condition); };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Arg("--pn-grid-test=") == null) return;
            var go = new GameObject("PNGridChunkPlayChecks"); DontDestroyOnLoad(go); go.AddComponent<PNGridChunkPlayChecks>();
        }

        private IEnumerator Start()
        {
            folder = Arg("--pn-grid-test="); role = Arg("--pn-grid-role=") ?? "c1"; Directory.CreateDirectory(folder); Application.runInBackground = true;
            Mark(role + "-started");
            // Each process keeps its own profile in the test folder, so nothing of the real one is read or written.
            PNPlayerProfileService.Load(new PNProfileStore(Path.Combine(folder, "profile_" + role)));
            var run = role == "host" ? Host() : Client();
            while (true)
            {
                bool more; try { more = run.MoveNext(); } catch (Exception error) { Say("FAIL exception " + error); fails++; break; }
                if (!more) break; yield return run.Current;
            }
            lock (errors) Verdict("no Error or Exception entries in the whole log", errors.Count == 0, errors.Count + " first=" + string.Join(" || ", errors.Take(3).Select(e => e.Length > 240 ? e.Substring(0, 240) : e)));
            Say((fails == 0 ? "RESULT PASS " : "RESULT FAIL ") + passes + " passed, " + fails + " failed");
            File.WriteAllText(Path.Combine(folder, role + ".txt"), (fails == 0 ? "PASS " : "FAIL ") + passes + "\n" + string.Join("\n", log));
            Application.Quit(fails == 0 ? 0 : 1);
        }

        private IEnumerator Wait(string what, Func<bool> condition, float seconds, Action<bool> result = null)
        {
            float until = Time.realtimeSinceStartup + seconds; bool ok = false;
            while (Time.realtimeSinceStartup < until) { bool now = false; try { now = condition(); } catch (Exception error) { Say("wait " + what + " threw " + error.Message); } if (now) { ok = true; break; } yield return null; }
            if (!ok) Say("TIMEOUT waiting for " + what + (Manager != null ? " (" + Manager.LastFailure + ")" : ""));
            result?.Invoke(ok);
        }

        // A fingerprint of every cell of the world (solid, floor height, ground type).
        private static ulong Fingerprint(PNGridWorldData data)
        {
            ulong hash = 14695981039346656037UL;
            for (int z = 0; z < data.Depth; z++) for (int x = 0; x < data.Width; x++)
            {
                hash = (hash ^ (data.IsSolid(x, z) ? 1UL : 0UL)) * 1099511628211UL;
                hash = (hash ^ (ulong)(ushort)data.HeightRaw(x, z)) * 1099511628211UL;
                hash = (hash ^ data.GroundAt(x, z)) * 1099511628211UL;
            }
            return hash;
        }

        private static bool WallAt(EveWorldContext world, PNGridWorldData data, int x, int z)
        {
            var scene = world.PhysicsOwner.GetPhysicsScene(); float cell = data.CellSize;
            // From above, a wall is hit about 2 m up; open ground about 5 m down.
            return scene.Raycast(new Vector3((x + .5f) * cell, 5f, (z + .5f) * cell), Vector3.down, out var hit, 10f) && hit.distance < 4f;
        }

        // ------------------------------------------------------------------------------------------------ host
        private IEnumerator Host()
        {
            yield return Wait("hall", () => PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && EveRegistrySession.IsReady, 120);
            PNGameFlow.StartRoguelite(PNFlowMode.Host);
            bool lobby = false; yield return Wait("lobby", () => PNGameFlow.Phase == PNFlowPhase.Lobby && !PNGameFlow.Busy && EveRegistrySession.IsReady && Manager != null && Manager.IsPlaying, 120, v => lobby = v);
            Verdict("the host opens the lobby", lobby); if (!lobby) yield break;
            Mark("host-lobby");
            yield return Wait("client in the lobby", () => Has("c1-lobby"), 180); if (!Has("c1-lobby")) yield break;

            EveWorldContext grid = null;
            try { grid = Manager.Worlds.Create(PNGridIds.SandboxTestMaze); gridId = grid.InstanceId; }
            catch (Exception error) { Verdict("the host creates the grid world", false, error.Message); yield break; }
            yield return Wait("grid world ready", () => grid.State == EveWorldState.Ready, 60);
            var build = Manager.Worlds.Get(grid); var data = build.GridWorld; var controller = PNGridWorldController.Find(grid);
            Verdict("the host's grid world is ready with its cells, descriptor and controller", grid.State == EveWorldState.Ready && data != null && build.GridDescriptor != null && controller != null, "state=" + grid.State);
            if (data == null || controller == null) yield break;
            Mark("grid-world", gridId.ToString());

            var subscribed = new HashSet<int>();
            NetworkConnectionToClient Next() => NetworkServer.connections.Values.FirstOrDefault(c => c.authenticationData is PNPeer p && p.Owner > 0 && !subscribed.Contains(c.connectionId) && !(c is LocalConnectionToClient));

            // ---- c1 joins the grid world
            var first = Next();
            if (first == null) { Verdict("a client is connected", false); yield break; }
            subscribed.Add(first.connectionId); Manager.SubscribeWorld(first, grid);
            yield return Wait("c1 ready", () => Has("c1-grid-ready"), 120); Verdict("the client's grid world became ready", Has("c1-grid-ready"));
            yield return StreamIdle(first, grid);
            PublishHash("hash-1", data, first, grid);
            yield return Wait("c1 verified the first stream", () => Has("c1-verified-1"), 90);
            var stats = Manager.GridChunkStats(first.connectionId, gridId);
            Verdict("the host streamed chunks and ended the first set", stats.HasValue && stats.Value.Chunks >= 6 && stats.Value.InitialDone, stats.HasValue ? stats.Value.Chunks + " chunks, " + stats.Value.Bytes + " bytes" : "no stats");

            // ---- a placed thing and a broken thing reach the client
            int px = -1, pz = -1;
            for (int z = 20; z < data.Depth - 2 && px < 0; z++) for (int x = 20; x < data.Width - 2; x++) if (!data.IsSolid(x, z) && controller.Placements.At(x, z) == null) { px = x; pz = z; break; }
            var registry = EveRegistrySession.Context.Registry; float cell = data.CellSize;
            var place = new JObject { ["def"] = PNPlaceableIds.Vanish.ToString(), ["x"] = px, ["z"] = pz, ["dir"] = 0 };
            bool placed = PNGridCommands.ApplyPlace(registry, controller, new Vector3((px + .5f) * cell, 0, (pz + .5f) * cell), place);
            Verdict("the host places a block", placed, "cell " + px + "," + pz);
            Mark("edit-place", px + " " + pz);
            for (int i = 0; i < 20 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(first, grid);
            PublishHash("hash-2", data, first, grid, true);
            yield return Wait("c1 saw the block", () => Has("c1-verified-2"), 90);
            Verdict("the client saw the block and could hit it", Has("c1-verified-2") && Read("c1-verified-2") == "ok", Has("c1-verified-2") ? Read("c1-verified-2") : "missing");

            int hits = 0; while (controller.Placements.At(px, pz) != null && hits < 30) { PNGridCommands.ApplyBreak(controller, new Vector3((px + .5f) * cell, 0, (pz + .5f) * cell), new JObject { ["x"] = px, ["z"] = pz }); hits++; }
            for (int i = 0; i < 20 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            Verdict("the host breaks it again", controller.Placements.At(px, pz) == null, hits + " hits");
            // leave one block standing for the late client
            PNGridCommands.ApplyPlace(registry, controller, new Vector3((px + .5f) * cell, 0, (pz + .5f) * cell), place);
            for (int i = 0; i < 20 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(first, grid);
            PublishHash("hash-3", data, first, grid, true);
            yield return Wait("c1 saw the break and the new block", () => Has("c1-verified-3"), 90);
            Verdict("the client saw the block go and a new one stand", Has("c1-verified-3") && Read("c1-verified-3") == "ok", Has("c1-verified-3") ? Read("c1-verified-3") : "missing");
            Mark("edits-done", px + " " + pz);

            // ---- a client that joins later
            yield return Wait("late client in the lobby", () => Has("late-lobby"), 180); if (!Has("late-lobby")) yield break;
            var late = Next();
            if (late == null) { Verdict("the late client is connected", false); yield break; }
            subscribed.Add(late.connectionId); Manager.SubscribeWorld(late, grid);
            yield return Wait("late ready", () => Has("late-grid-ready"), 120); Verdict("the late client's grid world became ready", Has("late-grid-ready"));
            yield return StreamIdle(late, grid);
            PublishHash("hash-late", data, late, grid, true);
            yield return Wait("late verified", () => Has("late-verified"), 90);
            Verdict("a client that joined later received the world as it is now", Has("late-verified") && Read("late-verified") == "ok", Has("late-verified") ? Read("late-verified") : "missing");

            // ---- c1 leaves and returns
            Mark("go-rejoin");
            yield return Wait("c1 left", () => Has("c1-left"), 90);
            yield return Wait("c1 back in the lobby", () => Has("c1-lobby-2"), 180); if (!Has("c1-lobby-2")) yield break;
            var again = Next();
            if (again == null) { Verdict("the returning client is connected", false); yield break; }
            subscribed.Add(again.connectionId); Manager.SubscribeWorld(again, grid);
            yield return Wait("c1 ready again", () => Has("c1-grid-ready-2"), 120); Verdict("the returning client's grid world became ready", Has("c1-grid-ready-2"));
            yield return StreamIdle(again, grid);
            PublishHash("hash-rejoin", data, again, grid, true);
            yield return Wait("c1 verified again", () => Has("c1-verified-rejoin"), 90);
            Verdict("a client that left and returned received the world again", Has("c1-verified-rejoin") && Read("c1-verified-rejoin") == "ok", Has("c1-verified-rejoin") ? Read("c1-verified-rejoin") : "missing");
            Mark("host-done");
            yield return Wait("clients finished", () => Has("c1.txt") && Has("late.txt"), 60);
            Verdict("both clients finished clean", Has("c1.txt") && Has("late.txt") && Read("c1.txt").StartsWith("PASS") && Read("late.txt").StartsWith("PASS"));
        }

        // The stream to this peer has nothing left to send for a moment.
        private IEnumerator StreamIdle(NetworkConnectionToClient peer, EveWorldContext grid)
        {
            long last = -1; float since = Time.realtimeSinceStartup;
            for (float until = Time.realtimeSinceStartup + 30; Time.realtimeSinceStartup < until;)
            {
                var s = Manager.GridChunkStats(peer.connectionId, grid.InstanceId);
                if (s.HasValue) { if (s.Value.Chunks != last || s.Value.Held != 0) { last = s.Value.Chunks; since = Time.realtimeSinceStartup; } else if (Time.realtimeSinceStartup - since > 1.5f) yield break; }
                yield return null;
            }
        }

        private void PublishHash(string name, PNGridWorldData data, NetworkConnectionToClient peer, EveWorldContext grid, bool withThing = false)
        {
            var s = Manager.GridChunkStats(peer.connectionId, grid.InstanceId);
            var controller = PNGridWorldController.Find(grid);
            var thing = controller.Placements.All.FirstOrDefault();
            Mark(name, Fingerprint(data) + " " + (s.HasValue ? s.Value.Chunks : -1) + " " + (thing != null ? thing.X + "," + thing.Z : "none"));
        }

        // ------------------------------------------------------------------------------------------------ clients
        private IEnumerator Client()
        {
            yield return Wait("hall", () => PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && EveRegistrySession.IsReady, 120);
            if (role == "late") { yield return Wait("edits done", () => Has("edits-done"), 400); if (!Has("edits-done")) yield break; }
            else yield return Wait("host lobby", () => Has("host-lobby"), 180);
            yield return JoinLobby(role + "-lobby");
            yield return Wait("grid world offered", () => Has("grid-world"), 120);
            gridId = Guid.Parse(Read("grid-world"));
            if (role == "late") { yield return Receive(role, "hash-late", "late-grid-ready", "late-verified", null); }
            else
            {
                yield return Receive(role, "hash-1", "c1-grid-ready", "c1-verified-1", null);
                yield return VerifyEdit("hash-2", "c1-verified-2", true);
                yield return VerifyEdit("hash-3", "c1-verified-3", true);
                yield return Wait("go rejoin", () => Has("go-rejoin"), 400);
                // leave the room and join it again
                PNGameFlow.ReturnToHall();
                yield return Wait("Hall again", () => PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && EveRegistrySession.IsReady, 90);
                Mark("c1-left");
                yield return new WaitForSeconds(2f);
                yield return JoinLobby("c1-lobby-2");
                yield return Receive(role, "hash-rejoin", "c1-grid-ready-2", "c1-verified-rejoin", null);
            }
            if (role == "c1") yield return Wait("host done", () => Has("host-done"), 120);
        }

        private IEnumerator JoinLobby(string mark)
        {
            PNGameFlow.StartRoguelite(PNFlowMode.Join, "127.0.0.1");
            bool inLobby = false; yield return Wait("lobby", () => PNGameFlow.Phase == PNFlowPhase.Lobby && !PNGameFlow.Busy && EveRegistrySession.IsReady && Manager != null && Manager.ClientInputReady, 120, v => inLobby = v);
            Verdict(role + " joins the lobby (" + mark + ")", inLobby); if (!inLobby) yield break;
            Mark(mark);
        }

        private EveWorldContext Replica() => EveRegistrySession.IsReady ? EveRegistrySession.Context.Worlds.Snapshot().FirstOrDefault(x => x.InstanceId == gridId) : null;

        // The grid world arrives: it is not ready before the first set of chunks, then everything the host sent is in the cells.
        private IEnumerator Receive(string who, string hashFile, string readyMark, string verifiedMark, object unused)
        {
            bool ready = false; yield return Wait("grid replica ready", () => Manager.ClientWorldReady(gridId), 120, v => ready = v);
            Verdict(who + ": the grid replica becomes ready", ready); if (!ready) yield break;
            var inbox = Manager.ClientGridInbox(gridId);
            Verdict(who + ": it was held back until the first set of chunks had come", inbox != null && inbox.InitialComplete && inbox.ChunksReceived > 0, inbox == null ? "no inbox" : inbox.ChunksReceived + " chunks");
            Mark(readyMark);
            yield return Wait("host hash", () => Has(hashFile), 120);
            var parts = Read(hashFile).Split(' '); ulong hash = ulong.Parse(parts[0]); long sent = long.Parse(parts[1]);
            var world = Replica(); var data = Manager.Worlds.Get(world).GridWorld;
            yield return Wait("all chunks in", () => Manager.ClientGridInbox(gridId) != null && Manager.ClientGridInbox(gridId).ChunksReceived >= sent && Manager.ClientGridInbox(gridId).QueuedCount == 0, 60);
            bool same = false; yield return Wait("cells equal", () => same = Fingerprint(data) == hash, 20);
            Verdict(who + ": the replica's cells equal the host's (" + hashFile + ")", same, "received " + Manager.ClientGridInbox(gridId)?.ChunksReceived + " of " + sent);
            var host = world.PhysicsOwner.GetPhysicsScene().IsValid() ? UnityEngine.Object.FindObjectsByType<PNGridWorldHost>().FirstOrDefault(h => h.Data == data) : null;
            bool built = false; yield return Wait("meshes built", () => built = host != null && host.PendingChunks == 0, 20);
            Verdict(who + ": its chunk meshes and colliders are built", built);
            var things = parts[2];
            if (things != "none") { var xz = things.Split(','); Verdict(who + ": the block stands as a wall you can hit", WallAt(world, data, int.Parse(xz[0]), int.Parse(xz[1])), things); }
            Mark(verifiedMark, same && built ? "ok" : "bad");
        }

        private IEnumerator VerifyEdit(string hashFile, string verifiedMark, bool unused)
        {
            yield return Wait("host hash " + hashFile, () => Has(hashFile), 120);
            var parts = Read(hashFile).Split(' '); ulong hash = ulong.Parse(parts[0]); long sent = long.Parse(parts[1]);
            var world = Replica(); var data = Manager.Worlds.Get(world).GridWorld;
            yield return Wait("chunks in", () => Manager.ClientGridInbox(gridId).ChunksReceived >= sent && Manager.ClientGridInbox(gridId).QueuedCount == 0, 60);
            bool same = false; yield return Wait("cells equal", () => same = Fingerprint(data) == hash, 20);
            var host = UnityEngine.Object.FindObjectsByType<PNGridWorldHost>().FirstOrDefault(h => h.Data == data);
            bool built = false; yield return Wait("meshes built", () => built = host != null && host.PendingChunks == 0, 20);
            // the cell of the thing: with one standing there is a wall, without it open ground
            var edit = Read("edit-place").Split(' '); int x = int.Parse(edit[0]), z = int.Parse(edit[1]);
            bool standing = parts[2] != "none"; bool wall = WallAt(world, data, x, z);
            Verdict("c1: after " + hashFile + " the cells equal the host's and the collider agrees (thing " + (standing ? "stands" : "gone") + ")", same && built && wall == standing, "same=" + same + " built=" + built + " wall=" + wall);
            Mark(verifiedMark, same && built && wall == standing ? "ok" : "bad");
        }
    }
}
