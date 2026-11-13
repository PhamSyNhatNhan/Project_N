using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // EN: Two Player processes for a 1024 x 1024 cell grid world over the real network (Docs/SandboxPlan.md "Dong nen chung"): how many chunks and bytes the first stream costs, how long the client takes to become
    //     ready, what a single edit and a 64-chunk blast cost, and whether the chunks the client holds equal the host's (checksum per chunk). Only with `--pn-grid-large=<folder> --pn-grid-role=<host|c1> --pn-auto-start=hall`;
    //     the same flag registers the check world `project_n:check_large_grid` (see PNGameplayContentModule). Results: `<role>.txt` in the folder.
    // VI: Hai Player cho world luoi 1024x1024 qua mang that: chi phi luong dau, thoi gian client san sang, mot sua va mot vu no 64 manh, va cac manh client giu co bang cua host khong.
    public sealed class PNGridLargePlayChecks : MonoBehaviour
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
        private void Say(string text) { log.Add(text); Debug.Log("GridLarge " + role + " " + text); }
        private void Verdict(string name, bool ok, string detail = null) { if (ok) passes++; else fails++; Say((ok ? "PASS " : "FAIL ") + name + (detail != null ? "  [" + detail + "]" : "")); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Early()
        {
            if (Arg("--pn-grid-large=") == null) return;
            Application.logMessageReceived += (condition, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) lock (errors) errors.Add(condition); };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Arg("--pn-grid-large=") == null) return;
            var go = new GameObject("PNGridLargePlayChecks"); DontDestroyOnLoad(go); go.AddComponent<PNGridLargePlayChecks>();
        }

        private IEnumerator Start()
        {
            folder = Arg("--pn-grid-large="); role = Arg("--pn-grid-role=") ?? "c1"; Directory.CreateDirectory(folder); Application.runInBackground = true;
            Mark(role + "-started");
            PNPlayerProfileService.Load(new PNProfileStore(Path.Combine(folder, "profile_" + role)));
            var run = role == "host" ? Host() : Client();
            while (true)
            {
                bool more; try { more = run.MoveNext(); } catch (Exception error) { Say("FAIL exception " + error); fails++; break; }
                if (!more) break; yield return run.Current;
            }
            lock (errors) Verdict("no Error or Exception entries in the whole log", errors.Count == 0, errors.Count + " first=" + string.Join(" || ", errors.Take(3).Select(e => e.Length > 160 ? e.Substring(0, 160) : e)));
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

        private IEnumerator StreamIdle(NetworkConnectionToClient peer, EveWorldContext grid)
        {
            long last = -1; float since = Time.realtimeSinceStartup;
            for (float until = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < until;)
            {
                var s = Manager.GridChunkStats(peer.connectionId, grid.InstanceId);
                if (s.HasValue) { if (s.Value.Chunks != last || s.Value.Held != 0) { last = s.Value.Chunks; since = Time.realtimeSinceStartup; } else if (Time.realtimeSinceStartup - since > 1.5f) yield break; }
                yield return null;
            }
        }

        private static string Sums(PNGridWorldData data)
        {
            var parts = new string[data.ChunkCount];
            for (int i = 0; i < parts.Length; i++) parts[i] = PNGridChunkCopy.Checksum(data, i).ToString();
            return string.Join(" ", parts);
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

            EveWorldContext grid = null; float t0 = Time.realtimeSinceStartup;
            try { grid = Manager.Worlds.Create(EveId.Parse("project_n:check_large_grid")); gridId = grid.InstanceId; }
            catch (Exception error) { Verdict("the host creates the large world", false, error.Message); yield break; }
            yield return Wait("grid world ready", () => grid.State == EveWorldState.Ready || grid.State == EveWorldState.Failed || grid.State == EveWorldState.Disposed, 300);
            if (grid.State != EveWorldState.Ready) { Verdict("the large world became ready", false, grid.State + " " + (grid.Failure ?? grid.FirstFailure)); yield break; }
            float made = Time.realtimeSinceStartup - t0;
            var build = Manager.Worlds.Get(grid); var data = build.GridWorld; var controller = PNGridWorldController.Find(grid);
            Verdict("the host's 1024 x 1024 world is ready", grid.State == EveWorldState.Ready && data != null && data.Width == 1024 && data.Depth == 1024 && controller != null, "state=" + grid.State + " made in " + made.ToString("0.0") + " s, " + (data != null ? data.ChunkCount : 0) + " chunks");
            if (data == null || controller == null) yield break;
            Mark("grid-world", gridId.ToString());

            var peer = NetworkServer.connections.Values.FirstOrDefault(c => c.authenticationData is PNPeer p && p.Owner > 0 && !(c is LocalConnectionToClient));
            if (peer == null) { Verdict("a client is connected", false); yield break; }
            float t1 = Time.realtimeSinceStartup; Manager.SubscribeWorld(peer, grid);
            yield return Wait("c1 ready", () => Has("c1-grid-ready"), 300); float ready = Time.realtimeSinceStartup - t1;
            yield return StreamIdle(peer, grid); float idle = Time.realtimeSinceStartup - t1;
            var s0 = Manager.GridChunkStats(peer.connectionId, gridId); var k0 = Manager.GridChunkKinds(peer.connectionId, gridId);
            Verdict("the first stream is a ring, not the world", s0.HasValue && s0.Value.InitialDone && s0.Value.Chunks > 0 && s0.Value.Chunks < data.ChunkCount, s0.HasValue ? s0.Value.Chunks + " of " + data.ChunkCount + " chunks, " + s0.Value.Bytes + " bytes, ready after " + ready.ToString("0.0") + " s, idle after " + idle.ToString("0.0") + " s" : "no stats");
            Mark("sums-1", s0.Value.Chunks + "|" + Sums(data));
            yield return Wait("c1 verified 1", () => Has("c1-verified-1"), 120);
            Verdict("the client holds chunks equal to the host's (first stream)", Has("c1-verified-1") && Read("c1-verified-1").StartsWith("ok"), Has("c1-verified-1") ? Read("c1-verified-1") : "missing");

            // single edits near the world's middle (where the client's focus falls with no actor yet)
            var editor = controller.Editor; int toggled = 0;
            for (int i = 0; i < 20; i++)
            {
                int ex = 512 + (i % 10) * 3, ez = 512 + (i / 10) * 3;
                if (editor.Submit(new PNGridEditBatch().AddSolid(ex, ez, !data.IsSolid(ex, ez))).Accepted) toggled++;
                controller.Pump(); yield return null; yield return null;
            }
            yield return StreamIdle(peer, grid);
            var s1 = Manager.GridChunkStats(peer.connectionId, gridId); var k1 = Manager.GridChunkKinds(peer.connectionId, gridId);
            long editBytes = s1.Value.Bytes - s0.Value.Bytes;
            Verdict("single edits are deltas and cheap", k1.Value.Deltas - k0.Value.Deltas >= 1 && k1.Value.Fulls == k0.Value.Fulls && editBytes <= toggled * 64, toggled + " edits: " + (k1.Value.Deltas - k0.Value.Deltas) + " deltas, " + editBytes + " bytes");
            Mark("sums-2", s1.Value.Chunks + "|" + Sums(data));
            yield return Wait("c1 verified 2", () => Has("c1-verified-2"), 120);
            Verdict("the client equals the host after the single edits", Has("c1-verified-2") && Read("c1-verified-2").StartsWith("ok"), Has("c1-verified-2") ? Read("c1-verified-2") : "missing");

            // a blast over a block of 8 x 8 chunks (256 x 256 cells)
            var batches = PNGridEditBatch.SolidRegionBatches(new PNGridRegion(384, 384, 639, 639), true);
            var submit = editor.SubmitGroup(batches);
            for (int i = 0; i < 400 && editor.QueuedCells > 0; i++) { controller.Pump(); yield return null; }
            yield return StreamIdle(peer, grid);
            var s2 = Manager.GridChunkStats(peer.connectionId, gridId); var k2 = Manager.GridChunkKinds(peer.connectionId, gridId);
            Verdict("a 64-chunk blast is accepted and sent", submit.Accepted, "chunks " + (s2.Value.Chunks - s1.Value.Chunks) + ", bytes " + (s2.Value.Bytes - s1.Value.Bytes) + ", deltas " + (k2.Value.Deltas - k1.Value.Deltas) + ", wholes " + (k2.Value.Fulls - k1.Value.Fulls));
            Mark("sums-3", s2.Value.Chunks + "|" + Sums(data));
            yield return Wait("c1 verified 3", () => Has("c1-verified-3"), 120);
            Verdict("the client equals the host after the blast", Has("c1-verified-3") && Read("c1-verified-3").StartsWith("ok"), Has("c1-verified-3") ? Read("c1-verified-3") : "missing");
            Say("BANDWIDTH first=" + s0.Value.Bytes + " B in " + idle.ToString("0.0") + " s; edits=" + editBytes + " B; blast=" + (s2.Value.Bytes - s1.Value.Bytes) + " B; total=" + s2.Value.Bytes + " B");
            Mark("host-done");
            yield return Wait("client done", () => Has("c1-done"), 60);
        }

        // ------------------------------------------------------------------------------------------------ client
        private IEnumerator Client()
        {
            yield return Wait("hall", () => PNGameFlow.Phase == PNFlowPhase.Hall && !PNGameFlow.Busy && EveRegistrySession.IsReady, 120);
            yield return Wait("host lobby", () => Has("host-lobby"), 180);
            PNGameFlow.StartRoguelite(PNFlowMode.Join, "127.0.0.1");
            bool inLobby = false; yield return Wait("lobby", () => PNGameFlow.Phase == PNFlowPhase.Lobby && !PNGameFlow.Busy && EveRegistrySession.IsReady && Manager != null && Manager.ClientInputReady, 120, v => inLobby = v);
            Verdict("c1 joins the lobby", inLobby); if (!inLobby) yield break;
            Mark("c1-lobby");
            yield return Wait("grid world offered", () => Has("grid-world"), 300);
            gridId = Guid.Parse(Read("grid-world"));
            float t0 = Time.realtimeSinceStartup;
            bool ready = false; yield return Wait("grid replica ready", () => Manager.ClientWorldReady(gridId), 300, v => ready = v);
            Verdict("the large replica becomes ready", ready, (Time.realtimeSinceStartup - t0).ToString("0.0") + " s after the offer");
            if (!ready) yield break;
            Mark("c1-grid-ready");
            yield return Check("sums-1", "c1-verified-1", true);
            yield return Check("sums-2", "c1-verified-2", false);
            yield return Check("sums-3", "c1-verified-3", false);
            Mark("c1-done");
        }

        private IEnumerator Check(string sumsFile, string verifiedMark, bool first)
        {
            yield return Wait("host sums " + sumsFile, () => Has(sumsFile), 300);
            var split = Read(sumsFile).Split('|'); long sent = long.Parse(split[0]); var host = split[1].Split(' ');
            var world = EveRegistrySession.Context.Worlds.Snapshot().First(x => x.InstanceId == gridId); var data = Manager.Worlds.Get(world).GridWorld; var inbox = Manager.ClientGridInbox(gridId);
            yield return Wait("chunks in", () => inbox.ChunksReceived >= sent && inbox.QueuedCount == 0, 120);
            yield return new WaitForSeconds(1f);
            int match = 0; for (int i = 0; i < host.Length; i++) if (PNGridChunkCopy.Checksum(data, i).ToString() == host[i]) match++;
            // a chunk the client never received is default cells and would not equal the host's noise; in the first stream every sent chunk is a distinct chunk
            bool ok = match > 0 && match < host.Length && (!first || match >= sent);
            Verdict(verifiedMark + ": chunks equal to the host's", ok, match + " of " + host.Length + " equal, " + inbox.ChunksReceived + " received, host sent " + sent);
            Mark(verifiedMark, (ok ? "ok" : "bad") + " " + match + " of " + host.Length);
        }
    }
}
