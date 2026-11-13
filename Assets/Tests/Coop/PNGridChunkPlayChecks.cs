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

            // ---- G9c part 2: the placed things reach the client as a list (a marker that keeps the way open, a fountain with an interactable), their health follows, a broken or removed one goes
            {
                var placements = controller.Placements;
                bool PlaceFree(EveId def, out PNPlacedObject made)
                {
                    made = null;
                    for (int z = 8; z < data.Depth - 8; z += 3) for (int x = 8; x < data.Width - 8; x += 3)
                    {
                        if (data.IsSolid(x, z) || placements.At(x, z) != null || Math.Abs(x - px) <= 2 && Math.Abs(z - pz) <= 2) continue;
                        if (PNGridCommands.ApplyPlace(registry, controller, new Vector3((x + .5f) * cell, 0, (z + .5f) * cell), new JObject { ["def"] = def.ToString(), ["x"] = x, ["z"] = z, ["dir"] = 0 })) { made = placements.At(x, z); return made != null; }
                    }
                    return false;
                }
                bool madeMarker = PlaceFree(PNPlaceableIds.Marker, out var marker), madeFountain = PlaceFree(PNPlaceableIds.Fountain, out var fountain);
                Verdict("PL: the host places a marker and a fountain", madeMarker && madeFountain, madeMarker + " " + madeFountain);
                if (madeMarker && madeFountain)
                {
                    Mark("pl-added", marker.Id + " " + marker.X + " " + marker.Z + " " + fountain.Id + " " + fountain.X + " " + fountain.Z + " " + fountain.EffectiveKey + " " + marker.Health);
                    yield return Wait("c1 has the placed things", () => Has("c1-pl-added"), 90);
                    Verdict("PL: the client has the marker and the fountain (list, key, world object)", Has("c1-pl-added") && Read("c1-pl-added") == "ok", Has("c1-pl-added") ? Read("c1-pl-added") : "missing");
                    placements.Damage(marker.Id, 2);
                    Mark("pl-health", marker.Id + " " + marker.Health);
                    yield return Wait("c1 has the health", () => Has("c1-pl-health"), 90);
                    Verdict("PL: the client sees the marker's health go down", Has("c1-pl-health") && Read("c1-pl-health") == "ok", Has("c1-pl-health") ? Read("c1-pl-health") : "missing");
                    placements.Break(marker.Id);
                    Mark("pl-gone", marker.Id.ToString());
                    yield return Wait("c1 lost the marker", () => Has("c1-pl-gone"), 90);
                    Verdict("PL: the client sees the broken marker gone", Has("c1-pl-gone") && Read("c1-pl-gone") == "ok", Has("c1-pl-gone") ? Read("c1-pl-gone") : "missing");
                    bool madeSecond = PlaceFree(PNPlaceableIds.Fountain, out var second);
                    Verdict("PL: the host places a second fountain", madeSecond);
                    if (madeSecond)
                    {
                        Mark("pl-added2", second.Id + " " + second.X + " " + second.Z + " " + second.EffectiveKey);
                        yield return Wait("c1 has the second fountain", () => Has("c1-pl-added2"), 90);
                        Verdict("PL: the client has the second fountain", Has("c1-pl-added2") && Read("c1-pl-added2") == "ok", Has("c1-pl-added2") ? Read("c1-pl-added2") : "missing");
                        placements.Remove(second.Id);
                        Mark("pl-gone2", second.Id + " " + second.EffectiveKey);
                        yield return Wait("c1 lost the second fountain", () => Has("c1-pl-gone2"), 90);
                        Verdict("PL: the client sees the removed fountain gone with its world object", Has("c1-pl-gone2") && Read("c1-pl-gone2") == "ok", Has("c1-pl-gone2") ? Read("c1-pl-gone2") : "missing");
                    }
                    // the first fountain stays for the clients that come later
                    Mark("pl-final", fountain.Id + " " + fountain.X + " " + fountain.Z + " " + fountain.EffectiveKey);
                }
            }

            // ---- G8b: single edits are deltas, a blast and a build arrive whole and in step, a wrong copy asks for the chunk again
            var editor = controller.Editor;
            var k0 = Manager.GridChunkKinds(first.connectionId, gridId); var s0 = Manager.GridChunkStats(first.connectionId, gridId);
            int toggled = 0;
            for (int i = 0; i < 20; i++)
            {
                int ex = 10 + (i % 10) * 3, ez = 6 + (i / 10) * 3;
                if (Math.Abs(ex - px) <= 1 && Math.Abs(ez - pz) <= 1) continue;
                if (editor.Submit(new PNGridEditBatch().AddSolid(ex, ez, !data.IsSolid(ex, ez))).Accepted) toggled++;
                controller.Pump(); yield return null; yield return null;
            }
            yield return StreamIdle(first, grid);
            var k1 = Manager.GridChunkKinds(first.connectionId, gridId); var s1 = Manager.GridChunkStats(first.connectionId, gridId);
            int deltas = k1.Value.Deltas - k0.Value.Deltas, fulls = k1.Value.Fulls - k0.Value.Fulls; long bytes = s1.Value.Bytes - s0.Value.Bytes;
            Verdict("single block edits go out as deltas, not whole chunks", deltas >= 1 && fulls == 0 && bytes <= toggled * 64, toggled + " edits: " + deltas + " deltas, " + fulls + " whole, " + bytes + " bytes");
            PublishHash("hash-4", data, first, grid);
            yield return Wait("c1 verified the single edits", () => Has("c1-verified-4"), 90);
            Verdict("the client has the single edits (cells and checksums)", Has("c1-verified-4") && Read("c1-verified-4").StartsWith("ok"), Has("c1-verified-4") ? Read("c1-verified-4") : "missing");

            // a structure built through a group of batches, on the side away from the placed block, then blasted away again
            bool right = px < data.Width / 2; var area = right ? new PNGridRegion(data.Width / 2 + 4, 8, data.Width - 6, data.Depth - 8) : new PNGridRegion(6, 8, data.Width / 2 - 4, data.Depth - 8);
            var group = editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(area, true));
            Verdict("a structure is accepted as a group of batches", group.Accepted && editor.Settling, group.Accepted ? group.Queued + " cells" : group.Reason);
            for (int i = 0; i < 400 && editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(first, grid);
            Verdict("the group has been applied and the updates are no longer held back", editor.QueuedCells == 0 && !editor.Settling && !data.Settling);
            PublishHash("hash-5", data, first, grid);
            yield return Wait("c1 verified the structure", () => Has("c1-verified-5"), 90);
            Verdict("the client has the structure (cells, colliders, no resync)", Has("c1-verified-5") && Read("c1-verified-5").StartsWith("ok"), Has("c1-verified-5") ? Read("c1-verified-5") : "missing");
            editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(area, false));
            for (int i = 0; i < 400 && editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(first, grid);
            PublishHash("hash-6", data, first, grid);
            yield return Wait("c1 verified the blast", () => Has("c1-verified-6"), 90);
            Verdict("the client has the cleared area (cells, colliders, no resync)", Has("c1-verified-6") && Read("c1-verified-6").StartsWith("ok"), Has("c1-verified-6") ? Read("c1-verified-6") : "missing");

            // a client whose copy is wrong asks for the chunk again and ends equal to the host
            Mark("ask-corrupt");
            yield return Wait("c1 corrupted a cell", () => Has("c1-corrupted"), 60);
            editor.Submit(new PNGridEditBatch().AddSolid(5, 5, !data.IsSolid(5, 5))); controller.Pump(); yield return null;
            yield return StreamIdle(first, grid); yield return new WaitForSeconds(2f); yield return StreamIdle(first, grid);
            PublishHash("hash-7", data, first, grid);
            yield return Wait("c1 verified the resync", () => Has("c1-verified-7"), 90);
            Verdict("a client with a wrong copy asked for the chunk and got it whole", Has("c1-verified-7") && Read("c1-verified-7").StartsWith("ok"), Has("c1-verified-7") ? Read("c1-verified-7") : "missing");
            Mark("bulk-done");

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

            // ---- G8b-2: a client's OWN body in the grid world. c1's player is transferred into the world (slot main, spawn player); walls then grow on it, and the client frees its own body.
            var game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
            long ownerId = ((PNPeer)again.authenticationData).Owner;
            EveEntityLoader body = null;
            yield return Wait("c1's player exists", () => (body = EveRegistrySession.Context.Entities.Snapshot().FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == ownerId)) != null, 60);
            Verdict("the host has c1's player", body != null); if (body == null) yield break;
            // The world offers a default entry; the player in the lobby is Eve from the lobby's own spawn point, so for a plain (non-replacement) transfer the entry names that character and prototype, as a mode would.
            var entry = build.Slots[0].Roots[0].GetComponentsInChildren<PNPlayerSpawnPoint>(true).Single(x => x.SpawnEntry == PNGridWorldProvider.PlayerSpawnEntry);
            var prototype = body.GetComponent<PNFactoryEntity>()?.Prototype;
            Verdict("the lobby player has a prototype to spawn from", prototype != null); if (prototype == null) yield break;
            entry.Character = body.LoadContext.Identity.CharacterId.IsValid ? body.LoadContext.Identity.CharacterId.ToString() : body.GetComponent<PNStat>().CharacterId.ToString(); entry.Prefab = prototype;
            PNWorldTransferReceipt receipt = null;
            try { receipt = game.Transfers.Request(new PNWorldTransferRequest(Guid.NewGuid(), new[] { body }, grid, "main", PNGridWorldProvider.PlayerSpawnEntry)); }
            catch (Exception error) { Verdict("c1's player is transferred into the grid world", false, error.Message); yield break; }
            yield return Wait("transfer finished", () => receipt.IsTerminal && !receipt.CleanupPending, 120);
            Verdict("c1's player is transferred into the grid world", receipt.State == PNWorldTransferState.Completed, receipt.State + " " + receipt.Error);
            if (receipt.State != PNWorldTransferState.Completed) yield break;
            body = EveRegistrySession.Context.Entities.Snapshot(grid).FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == ownerId);
            Verdict("it is a body of the grid world on the host", body != null); if (body == null) yield break;
            Mark("in-grid");
            yield return Wait("c1 sees its body", () => Has("c1-in-grid"), 120);
            Verdict("the client has its own body in its copy of the grid world", Has("c1-in-grid"), Has("c1-in-grid") ? Read("c1-in-grid") : "missing"); if (!Has("c1-in-grid")) yield break;
            int pushedBefore = controller.PushedOut; float cellSize = data.CellSize;
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // A: standing still when walls grow on it
            var at = body.transform.position; int bx = Mathf.FloorToInt(at.x / cellSize), bz = Mathf.FloorToInt(at.z / cellSize);
            var groupA = controller.Editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(new PNGridRegion(bx - 5, bz - 5, bx + 5, bz + 5), true));
            Verdict("A: walls are built on the client's body", groupA.Accepted, groupA.Reason);
            for (int i = 0; i < 400 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(again, grid);
            PublishHash("hash-A", data, again, grid);
            yield return Wait("c1 freed itself (A)", () => Has("c1-A"), 90);
            Verdict("A: the client freed its own body", Has("c1-A") && Read("c1-A").StartsWith("ok"), Has("c1-A") ? Read("c1-A") : "missing");
            yield return new WaitForSeconds(1.5f);
            var hostAt = body.transform.position;
            Verdict("A: the host did not move the client's body itself", controller.PushedOut == pushedBefore, "pushed " + controller.PushedOut);
            Verdict("A: the host sees the body on free cells", PNGridBodies.FootprintFree(data, hostAt.x, hostAt.z), hostAt.ToString());
            if (Has("c1-A")) { var a = Read("c1-A").Split(' '); float cx = float.Parse(a[1], inv), cz = float.Parse(a[2], inv); Verdict("A: host and client agree where the body is", Vector2.Distance(new Vector2(cx, cz), new Vector2(hostAt.x, hostAt.z)) < 1f, "client " + cx + "," + cz + " host " + hostAt.x + "," + hostAt.z); }

            // B: walking, a wall grows ahead of it while it walks
            controller.Editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(new PNGridRegion(bx - 5, bz - 5, bx + 5, bz + 5), false));
            for (int i = 0; i < 400 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(again, grid);
            PublishHash("hash-B0", data, again, grid);
            yield return Wait("c1 verified the cleared area", () => Has("c1-verified-B0"), 90);
            Mark("walk-go");
            yield return Wait("c1 walks", () => Has("c1-walking"), 60);
            Verdict("B: the client walks", Has("c1-walking"), Has("c1-walking") ? Read("c1-walking") : "missing");
            if (Has("c1-walking"))
            {
                var w = Read("c1-walking").Split(' '); float tx = float.Parse(w[0], inv), tz = float.Parse(w[1], inv);
                int tcx = Mathf.FloorToInt(tx / cellSize), tcz = Mathf.FloorToInt(tz / cellSize);
                controller.Editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(new PNGridRegion(tcx - 4, tcz - 4, tcx + 4, tcz + 4), true));
                for (int i = 0; i < 400 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
                yield return StreamIdle(again, grid);
                PublishHash("hash-B", data, again, grid);
                yield return Wait("c1 settled (B)", () => Has("c1-B"), 90);
                Verdict("B: the client ended on free cells and could walk on", Has("c1-B") && Read("c1-B").StartsWith("ok"), Has("c1-B") ? Read("c1-B") : "missing");
                yield return new WaitForSeconds(1.5f);
                hostAt = body.transform.position;
                Verdict("B: the host sees the body on free cells", PNGridBodies.FootprintFree(data, hostAt.x, hostAt.z), hostAt.ToString());
                Verdict("B: the host did not move the client's body itself", controller.PushedOut == pushedBefore, "pushed " + controller.PushedOut);
            }

            // P: place and break commands sent by the CLIENT through the sync layer (the host decides: reach from the host's copy of its body, free cells, nothing standing there)
            Mark("place-go");
            yield return Wait("c1 sent its place commands", () => Has("c1-placed-sent"), 90);
            if (Has("c1-placed-sent"))
            {
                var pc = Read("c1-placed-sent").Split(' '); int qx = int.Parse(pc[0]), qz = int.Parse(pc[1]), ox = int.Parse(pc[2]), oz = int.Parse(pc[3]);
                yield return Wait("host applied the place command", () => controller.Placements.At(qx, qz) != null, 30);
                Verdict("P: the client's place command put a wall on the host", controller.Placements.At(qx, qz) != null && data.IsSolid(qx, qz), qx + "," + qz);
                yield return new WaitForSeconds(1f);
                Verdict("P: a wall over the client's own body was refused", controller.Placements.At(ox, oz) == null && !data.IsSolid(ox, oz), ox + "," + oz);
                yield return StreamIdle(again, grid);
                PublishHash("hash-P", data, again, grid);
                yield return Wait("c1 verified the wall", () => Has("c1-verified-P"), 90);
                Verdict("P: the client has the wall (cells, no resync)", Has("c1-verified-P") && Read("c1-verified-P").StartsWith("ok"), Has("c1-verified-P") ? Read("c1-verified-P") : "missing");
                Mark("break-go");
                yield return Wait("c1 sent its break commands", () => Has("c1-breaks-sent"), 60);
                yield return Wait("host broke it", () => controller.Placements.At(qx, qz) == null, 30);
                Verdict("P: the client's break commands broke the wall on the host", controller.Placements.At(qx, qz) == null && !data.IsSolid(qx, qz));
                yield return StreamIdle(again, grid);
                PublishHash("hash-P2", data, again, grid);
                yield return Wait("c1 verified the broken wall", () => Has("c1-verified-P2"), 90);
                Verdict("P: the client sees the wall gone (cells, no resync)", Has("c1-verified-P2") && Read("c1-verified-P2").StartsWith("ok"), Has("c1-verified-P2") ? Read("c1-verified-P2") : "missing");
            }
            else Verdict("P: the client sent its place commands", false);

            // C: the host takes the client body's Walk right away (control lease), then a wall grows on it. Observed, not asserted while locked: the host rejects a walk-locked body's new pose.
            var lease = body.GetComponent<PNControlManager>().State.Acquire("grid-test-lock", new[] { PNControlIds.Move });
            yield return new WaitForSeconds(1f);
            Mark("lock-go");
            {
                var lat = body.transform.position; int lx = Mathf.FloorToInt(lat.x / cellSize), lz = Mathf.FloorToInt(lat.z / cellSize);
                controller.Editor.SubmitGroup(PNGridEditBatch.SolidRegionBatches(new PNGridRegion(lx - 5, lz - 5, lx + 5, lz + 5), true));
                for (int i = 0; i < 400 && controller.Editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
                yield return StreamIdle(again, grid);
                PublishHash("hash-C", data, again, grid);
                yield return Wait("c1 reports the locked case", () => Has("c1-C"), 90);
                yield return new WaitForSeconds(2f);
                var hl = body.transform.position;
                Say("C (locked): host sees the body at " + hl + ", free=" + PNGridBodies.FootprintFree(data, hl.x, hl.z) + "; client says " + (Has("c1-C") ? Read("c1-C") : "missing"));
                lease.Dispose();
                Mark("unlock-go");
                yield return Wait("c1 reports after unlock", () => Has("c1-C2"), 90);
                yield return new WaitForSeconds(1.5f);
                hl = body.transform.position; bool hostFree = PNGridBodies.FootprintFree(data, hl.x, hl.z);
                Verdict("C: after the lock ended the body stands on free cells on the host", hostFree, hl.ToString());
                Verdict("C: after the lock ended the client's body is free too", Has("c1-C2") && Read("c1-C2").StartsWith("ok"), Has("c1-C2") ? Read("c1-C2") : "missing");
            }
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
            if (role == "late") { yield return Wait("bulk edits done", () => Has("bulk-done"), 600); if (!Has("bulk-done")) yield break; }
            else yield return Wait("host lobby", () => Has("host-lobby"), 180);
            yield return JoinLobby(role + "-lobby");
            yield return Wait("grid world offered", () => Has("grid-world"), 120);
            gridId = Guid.Parse(Read("grid-world"));
            if (role == "late") { yield return Receive(role, "hash-late", "late-grid-ready", "late-verified", null); yield return VerifyPlacement("pl-final", "late-pl-final", PlacementFinal); }
            else
            {
                yield return Receive(role, "hash-1", "c1-grid-ready", "c1-verified-1", null);
                yield return VerifyEdit("hash-2", "c1-verified-2", true);
                yield return VerifyEdit("hash-3", "c1-verified-3", true);
                yield return VerifyPlacement("pl-added", "c1-pl-added", PlacementAdded);
                yield return VerifyPlacement("pl-health", "c1-pl-health", PlacementHealth);
                yield return VerifyPlacement("pl-gone", "c1-pl-gone", PlacementGone);
                yield return VerifyPlacement("pl-added2", "c1-pl-added2", PlacementFountain);
                yield return VerifyPlacement("pl-gone2", "c1-pl-gone2", PlacementFountainGone);
                yield return VerifyCells("hash-4", "c1-verified-4", false);
                yield return VerifyCells("hash-5", "c1-verified-5", false);
                yield return VerifyCells("hash-6", "c1-verified-6", false);
                yield return Wait("ask corrupt", () => Has("ask-corrupt"), 120);
                { var wrong = Manager.Worlds.Get(Replica()).GridWorld; wrong.SetSolid(12, 12, !wrong.IsSolid(12, 12), false); Mark("c1-corrupted"); }
                yield return VerifyCells("hash-7", "c1-verified-7", true);
                yield return Wait("go rejoin", () => Has("go-rejoin"), 400);
                // leave the room and join it again
                PNGameFlow.ReturnToHall();
                yield return Wait("Hall again", () => PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && EveRegistrySession.IsReady, 90);
                Mark("c1-left");
                yield return new WaitForSeconds(2f);
                yield return JoinLobby("c1-lobby-2");
                yield return Receive(role, "hash-rejoin", "c1-grid-ready-2", "c1-verified-rejoin", null);
                yield return VerifyPlacement("pl-final", "c1-pl-final-2", PlacementFinal);
            }
            if (role == "c1") yield return OwnBody();
            if (role == "c1") yield return Wait("host done", () => Has("host-done"), 120);
        }

        // ---- G9c part 2, the client's side: the list of placed things in its copy of the world. Each check returns "ok" or what is wrong; the wait polls it while the stream catches up.
        private IEnumerator VerifyPlacement(string hostMark, string okMark, Func<string[], PNGridWorldController, string> check)
        {
            yield return Wait(hostMark, () => Has(hostMark), 180);
            string verdict = "no mark from the host";
            if (Has(hostMark))
            {
                var parts = Read(hostMark).Split(' '); verdict = "no controller";
                yield return Wait("placement " + hostMark, () => { var c = PNGridWorldController.Find(Replica()); if (c == null) return false; verdict = check(parts, c); return verdict == "ok"; }, 30);
            }
            Verdict(role + ": " + hostMark + " is in the client's list", verdict == "ok", verdict);
            Mark(okMark, verdict);
        }
        private static string Fountain(PNGridWorldController c, int id, int x, int z, string key)
        {
            var placed = c.Placements.Get(id);
            if (placed == null) return "fountain " + id + " is not in the list";
            if (placed.Definition.Id != PNPlaceableIds.Fountain || placed.X != x || placed.Z != z) return "fountain " + id + " is " + placed.Definition.Id + " at " + placed.X + "," + placed.Z;
            if (placed.EffectiveKey != key || c.Placements.ByKey(key) != placed) return "key is " + placed.EffectiveKey + ", expected " + key;
            var interactable = c.InteractableOf(id);
            if (interactable == null) return "no interactable for " + id;
            var worldObject = interactable.GetComponent<PNWorldObject>();
            return worldObject == null || !worldObject.IsBound || worldObject.Key != key ? "its world object is not bound" : "ok";
        }
        private static string PlacementAdded(string[] p, PNGridWorldController c)
        {
            int markerId = int.Parse(p[0]), mx = int.Parse(p[1]), mz = int.Parse(p[2]), fountainId = int.Parse(p[3]), fx = int.Parse(p[4]), fz = int.Parse(p[5]), health = int.Parse(p[7]);
            var marker = c.Placements.Get(markerId);
            if (marker == null) return "marker " + markerId + " is not in the list";
            if (marker.Definition.Id != PNPlaceableIds.Marker || marker.X != mx || marker.Z != mz || marker.Health != health) return "marker is " + marker.Definition.Id + " at " + marker.X + "," + marker.Z + " health " + marker.Health;
            if (c.Placements.At(mx, mz) != marker) return "the marker does not hold its cell";
            return Fountain(c, fountainId, fx, fz, p[6]);
        }
        private static string PlacementHealth(string[] p, PNGridWorldController c)
        {
            var marker = c.Placements.Get(int.Parse(p[0]));
            return marker == null ? "marker is gone" : marker.Health == int.Parse(p[1]) ? "ok" : "health is " + marker.Health + ", expected " + p[1];
        }
        private static string PlacementGone(string[] p, PNGridWorldController c) => c.Placements.Get(int.Parse(p[0])) == null ? "ok" : "marker " + p[0] + " is still in the list";
        private static string PlacementFountain(string[] p, PNGridWorldController c) => Fountain(c, int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), p[3]);
        private static string PlacementFountainGone(string[] p, PNGridWorldController c)
        {
            int id = int.Parse(p[0]);
            if (c.Placements.Get(id) != null) return "fountain " + id + " is still in the list";
            if (c.Placements.ByKey(p[1]) != null || c.InteractableOf(id) != null) return "its key or interactable is still there";
            return "ok";
        }
        private static string PlacementFinal(string[] p, PNGridWorldController c) => Fountain(c, int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), p[3]);

        private PNMove OwnMove()
        {
            var world = Replica(); if (world == null || !EveRegistrySession.IsReady || !EveRegistrySession.Context.LocalOwnerId.HasValue) return null;
            long me = EveRegistrySession.Context.LocalOwnerId.Value;
            var entity = EveRegistrySession.Context.Entities.Snapshot(world).FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == me);
            return entity == null ? null : entity.GetComponent<PNMove>();
        }

        // c1 side of G8b-2: the player arrives in the grid world, walls grow on it (standing, then walking), it frees itself and can still walk.
        private IEnumerator OwnBody()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // the entry names the lobby player's character and prototype (as on the host) so the transfer keeps the same character
            {
                long me = EveRegistrySession.Context.LocalOwnerId ?? -1;
                var lobbyPlayer = EveRegistrySession.Context.Entities.Snapshot().FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == me);
                var replica = Replica(); var spawnEntry = replica == null ? null : Manager.Worlds.Get(replica).Slots[0].Roots[0].GetComponentsInChildren<PNPlayerSpawnPoint>(true).FirstOrDefault(x => x.SpawnEntry == PNGridWorldProvider.PlayerSpawnEntry);
                var proto = lobbyPlayer == null ? null : lobbyPlayer.GetComponent<PNFactoryEntity>()?.Prototype;
                Verdict("c1: the replica has the player entry and the lobby player is known", spawnEntry != null && proto != null, "entry=" + (spawnEntry != null) + " proto=" + (proto != null));
                if (spawnEntry != null && proto != null) { spawnEntry.Character = lobbyPlayer.LoadContext.Identity.CharacterId.IsValid ? lobbyPlayer.LoadContext.Identity.CharacterId.ToString() : lobbyPlayer.GetComponent<PNStat>().CharacterId.ToString(); spawnEntry.Prefab = proto; }
            }
            yield return Wait("in-grid", () => Has("in-grid"), 300);
            PNMove move = null; yield return Wait("own body in the replica", () => (move = OwnMove()) != null && move.NetworkUsable, 120);
            if (move == null) { Verdict("c1: the own body exists in the grid replica", false); yield break; }
            var data = Manager.Worlds.Get(Replica()).GridWorld;
            Mark("c1-in-grid", move.transform.position.ToString());
            // A
            yield return VerifyCells("hash-A", "c1-verified-A", false);
            bool free = false; yield return Wait("body on free cells (A)", () => { var p = move.transform.position; return free = PNGridBodies.FootprintFree(data, p.x, p.z); }, 15);
            Verdict("c1: after walls grew on it the own body stands on free cells", free, move.transform.position.ToString());
            Mark("c1-A", (free ? "ok " : "bad ") + move.transform.position.x.ToString(inv) + " " + move.transform.position.z.ToString(inv));
            // B
            yield return VerifyCells("hash-B0", "c1-verified-B0", false);
            yield return Wait("walk go", () => Has("walk-go"), 120);
            var start = move.transform.position; Vector2 target = default; bool found = false;
            foreach (var dir in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                var candidate = new Vector2(start.x, start.z) + dir * 5f;
                if (PNGridBodies.FootprintFree(data, candidate.x, candidate.y) && PNGridBodies.FootprintFree(data, start.x + dir.x * 2.5f, start.z + dir.y * 2.5f)) { target = candidate; found = true; break; }
            }
            Verdict("c1: a free place to walk to exists", found); if (!found) { Mark("c1-walking", start.x.ToString(inv) + " " + start.z.ToString(inv)); yield break; }
            move.MoveToPoint(target, 2.5f, 8f);
            Mark("c1-walking", target.x.ToString(inv) + " " + target.y.ToString(inv));
            yield return Wait("hash B", () => Has("hash-B"), 120);
            yield return VerifyCells("hash-B", "c1-verified-B", false);
            yield return new WaitForSeconds(1.5f);
            var p2 = move.transform.position; bool free2 = PNGridBodies.FootprintFree(data, p2.x, p2.z);
            var before = move.transform.position;
            try { move.MoveToPoint(new Vector2(start.x, start.z), 2.5f, 4f); } catch (Exception error) { Say("walk back threw " + error.Message); }
            yield return new WaitForSeconds(2f);
            float moved = Vector2.Distance(new Vector2(before.x, before.z), new Vector2(move.transform.position.x, move.transform.position.z));
            var p3 = move.transform.position; bool free3 = PNGridBodies.FootprintFree(data, p3.x, p3.z);
            bool okB = free2 && free3 && moved > .5f;
            Verdict("c1: after a wall grew ahead of it while it walked the own body is on free cells and can walk on", okB, "free " + free2 + "/" + free3 + " moved " + moved.ToString("F2") + " pushedOut=" + UnityEngine.Object.FindObjectsByType<PNGridChunkReceiver>().Sum(x => x.PushedOut));
            Mark("c1-B", (okB ? "ok " : "bad ") + p3.x.ToString(inv) + " " + p3.z.ToString(inv));

            // P: place a wall three cells away, try to place one on itself, then break the first with several hits
            yield return Wait("place go", () => Has("place-go"), 120);
            {
                var game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
                var cmdTarget = PNSyncTarget.ForWorldObject(gridId, Replica().Incarnation, "main", PNGridCommands.ObjectKey);
                var at = move.transform.position; int bx = Mathf.FloorToInt(at.x / data.CellSize), bz = Mathf.FloorToInt(at.z / data.CellSize);
                int px = bx - 3, pz = bz;
                if (data.IsSolid(px, pz)) px = bx + 3;
                game.Sync.Send(cmdTarget, PNGridCommands.Place, new Newtonsoft.Json.Linq.JObject { ["def"] = PNPlaceableIds.Vanish.ToString(), ["x"] = px, ["z"] = pz, ["dir"] = 0 });
                game.Sync.Send(cmdTarget, PNGridCommands.Place, new Newtonsoft.Json.Linq.JObject { ["def"] = PNPlaceableIds.Vanish.ToString(), ["x"] = bx, ["z"] = bz, ["dir"] = 0 });
                Mark("c1-placed-sent", px + " " + pz + " " + bx + " " + bz);
                yield return VerifyCells("hash-P", "c1-verified-P", false);
                var replicaWorld = Replica(); bool wall = false;
                yield return Wait("wall collider", () => wall = WallAt(replicaWorld, data, px, pz), 20);
                Verdict("c1: the wall it placed stands as a wall on its copy", wall, px + "," + pz);
                yield return Wait("break go", () => Has("break-go"), 120);
                for (int i = 0; i < 10; i++)
                {
                    game.Sync.Send(cmdTarget, PNGridCommands.Break, new Newtonsoft.Json.Linq.JObject { ["x"] = px, ["z"] = pz });
                    yield return new WaitForSeconds(.15f);
                }
                Mark("c1-breaks-sent");
                yield return VerifyCells("hash-P2", "c1-verified-P2", false);
            }
            // C: walk right removed by the host, wall grown on the body
            yield return Wait("lock go", () => Has("lock-go"), 120);
            yield return VerifyCells("hash-C", "c1-verified-C", false);
            yield return new WaitForSeconds(2f);
            { var q = move.transform.position; bool f = PNGridBodies.FootprintFree(data, q.x, q.z); Mark("c1-C", (f ? "free " : "stuck ") + q.x.ToString(inv) + " " + q.z.ToString(inv)); }
            yield return Wait("unlock go", () => Has("unlock-go"), 120);
            yield return new WaitForSeconds(3f);
            { var q = move.transform.position; bool f = PNGridBodies.FootprintFree(data, q.x, q.z); Mark("c1-C2", (f ? "ok " : "bad ") + q.x.ToString(inv) + " " + q.z.ToString(inv)); }
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

        // The cells equal the host's and the colliders are built; nothing was asked for again (or, after a wrong copy was made on purpose, it was).
        private IEnumerator VerifyCells(string hashFile, string verifiedMark, bool expectResync)
        {
            yield return Wait("host hash " + hashFile, () => Has(hashFile), 120);
            var parts = Read(hashFile).Split(' '); ulong hash = ulong.Parse(parts[0]); long sent = long.Parse(parts[1]);
            var world = Replica(); var data = Manager.Worlds.Get(world).GridWorld; var inbox = Manager.ClientGridInbox(gridId);
            yield return Wait("chunks in", () => inbox.ChunksReceived >= sent && inbox.QueuedCount == 0, 60);
            bool same = false; yield return Wait("cells equal", () => same = Fingerprint(data) == hash, 20);
            var host = UnityEngine.Object.FindObjectsByType<PNGridWorldHost>().FirstOrDefault(h => h.Data == data);
            bool built = false; yield return Wait("meshes built", () => built = host != null && host.PendingChunks == 0, 20);
            bool resyncOk = expectResync ? inbox.ResyncsRequested >= 1 : inbox.ResyncsRequested == 0;
            Verdict("c1: after " + hashFile + " the cells equal the host's and the meshes are built" + (expectResync ? " (a wrong copy was asked for again)" : " (no resync)"), same && built && resyncOk, "same=" + same + " built=" + built + " deltas=" + inbox.DeltasApplied + " resyncs=" + inbox.ResyncsRequested);
            Mark(verifiedMark, (same && built && resyncOk ? "ok" : "bad") + " deltas=" + inbox.DeltasApplied + " resyncs=" + inbox.ResyncsRequested);
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
