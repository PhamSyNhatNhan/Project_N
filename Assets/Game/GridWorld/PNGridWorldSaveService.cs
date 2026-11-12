#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Decoded saves waiting for the world that will be built from them (Docs/SandboxPlan.md G7). A world is created from a descriptor; the descriptor of a restored world carries a one-time token (`restore`) and
//     the grid provider takes the save out of here while it loads. Nothing else reads it.
// VI: Bản lưu đã đọc, chờ world sẽ dựng từ nó. Descriptor của world khôi phục mang một token dùng một lần; provider lưới lấy bản lưu ra khỏi đây lúc tải.
public static class PNGridRestoreStaging
{
    private static readonly Dictionary<string, PNGridWorldSnapshot> waiting = new Dictionary<string, PNGridWorldSnapshot>();
    public static int Waiting => waiting.Count;
    public static string Put(PNGridWorldSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (waiting.Count >= 8) throw new InvalidOperationException("Too many saves are waiting for a world.");
        var token = Guid.NewGuid().ToString("N"); waiting[token] = snapshot; return token;
    }
    public static PNGridWorldSnapshot Take(string token)
    {
        if (token == null || !waiting.TryGetValue(token, out var value)) return null;
        waiting.Remove(token); return value;
    }
    public static void Discard(string token) { if (token != null) waiting.Remove(token); }
}

// What a read of a saved grid world gives back.
public sealed class PNGridSaveRead
{
    public PNGridWorldSnapshot Snapshot { get; }
    public PNGridWorldDescriptor Descriptor { get; }
    public EveId Definition { get; }
    public string ContentHash { get; }
    public long Revision { get; }
    public bool Recovered { get; }
    public int EnvelopeBytes { get; }
    internal PNGridSaveRead(PNGridWorldSnapshot snapshot, PNGridWorldDescriptor descriptor, EveId definition, string hash, long revision, bool recovered, int bytes)
    { Snapshot = snapshot; Descriptor = descriptor; Definition = definition; ContentHash = hash; Revision = revision; Recovered = recovered; EnvelopeBytes = bytes; }
}

// EN: Reading a saved grid world and building a world from it. The file is read and checked on a worker; the world is created like any other from the descriptor the save kept, with the restore token added, and
//     the provider fills it from the save instead of from its map. A save of another game version (rules, content) or of a world whose definition changed is refused before anything is built.
// VI: Đọc một world lưới đã lưu và dựng world từ nó. Tệp được đọc và kiểm trên worker; world được tạo như thường từ descriptor bản lưu giữ, thêm token khôi phục.
public static class PNGridWorldSaves
{
    public static Task<PNGridSaveRead> ReadAsync(PNWorldFileStore store, string key, EveContentCompatibility compatibility, int maximumBytes = PNWorldSaveEnvelope.MaximumBytes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (compatibility == null) throw new ArgumentNullException(nameof(compatibility));
        return Task.Run(() =>
        {
            var read = store.Read(key, maximumBytes);
            read.Envelope.ValidateCompatibility(compatibility);
            var doc = read.Envelope.Document; var descriptor = PNGridWorldDescriptor.Parse(doc["descriptor"]);
            var snapshot = PNGridWorldSnapshot.FromState(doc["state"], descriptor);
            return new PNGridSaveRead(snapshot, descriptor, EveId.Parse(read.Envelope.Definition), read.Envelope.ContentHash, read.Revision, read.Recovered, read.Envelope.Bytes);
        });
    }

    // Creates the world from a read save, STAGED: it loads over frames like any world, and the caller publishes it (PNWorldRuntime.PublishStaged) once it is ready. The definition must be the one the save was made in, with the same content hash.
    public static EveWorldContext Restore(PNWorldRuntime worlds, EveSessionContext session, PNGridSaveRead read)
    {
        if (worlds == null || session == null || read == null) throw new ArgumentNullException(worlds == null ? nameof(worlds) : session == null ? nameof(session) : nameof(read));
        var definition = session.Registry.Get<PNWorldDefinition>().Get(read.Definition);
        if (definition.ContentHash != read.ContentHash) throw new InvalidOperationException("The saved world's definition changed since it was saved.");
        var token = PNGridRestoreStaging.Put(read.Snapshot);
        try { return worlds.CreateStaged(read.Definition, Guid.NewGuid(), 1, read.Descriptor.WithRestore(token).ToJson()); }
        catch { PNGridRestoreStaging.Discard(token); throw; }
    }
}

// EN: The save service of a grid world (Docs/SandboxPlan.md G7), one per world that has the feature `project_n:world_save`, on the authority only. It keeps nothing but versions: which chunk version it last wrote, per
//     chunk, and the blob it wrote for it. A save copies the chunks whose version moved (a few per frame inside a time budget, default 2 ms; a copy is a memory copy of about 3 KB), checks afterwards that none of them
//     changed while it was copying (it copies those again, up to three rounds), takes the placed things and where the living players stand, and hands the copies to a worker thread, which encodes them, builds the
//     snapshot and writes it through the world file store (immutable data only; the write is atomic and keeps the two newest revisions). Ordinary enemies and the players' bodies are not saved. Saving is off until
//     `Configure` gives it a folder and a key (a mode does that); then it saves every `Interval` seconds when something changed, on `SaveNow`, and when the world goes away (`Flush`, blocking).
// VI: Dịch vụ lưu của world lưới, mỗi world một cái (feature `project_n:world_save`), chỉ ở authority. Chỉ giữ phiên bản mảnh đã ghi và blob đã ghi. Mỗi lần lưu chép các mảnh đã đổi (vài mảnh mỗi frame trong ngân sách thời gian),
//     kiểm lại không mảnh nào đổi trong lúc chép, lấy vật đặt và chỗ đứng người chơi, rồi đưa bản chép cho worker mã hóa và ghi qua kho tệp world. Quái thường và thân người chơi không được lưu. Tắt cho đến khi `Configure`.
public sealed class PNGridWorldSaveService : MonoBehaviour
{
    private enum Phase { Idle, Capturing, Writing }

    private sealed class Job
    {
        public PNGridChunkCopy[] Copies; public PNGridSavedPlacement[] Placed; public PNGridSavedPlayer[] Players; public double Time;
        public PNGridWorldDescriptor Descriptor; public IReadOnlyList<string> Palette; public byte[][] Blobs;
        public PNWorldFileStore Store; public string Key; public Guid SaveId; public string DefinitionId, ContentHash; public EveContentCompatibility Compatibility;
        public long Revision; public int Bytes; public int PlacedSignature, PlayersSignature;
    }

    private static readonly Dictionary<EveWorldContext, PNGridWorldSaveService> services = new Dictionary<EveWorldContext, PNGridWorldSaveService>();
    private readonly List<PNGridChunkCopy> copies = new List<PNGridChunkCopy>();
    private readonly List<int> pending = new List<int>();
    private readonly Dictionary<string, Vector3> players = new Dictionary<string, Vector3>();
    private int[] savedVersion; private byte[][] blobs;
    private Phase phase; private int cursor, rounds; private float sinceSave; private bool disposed;
    private Task<Job> task; private Job job;
    private int savedPlacedSignature, savedPlayersSignature;

    public EveWorldContext World { get; private set; }
    public PNGridWorldController Controller { get; private set; }
    public PNGridWorldData Data => Controller.Data;
    public PNGridWorldDescriptor Descriptor { get; private set; }
    public PNWorldFileStore Store { get; private set; }
    public string Key { get; private set; }
    public Guid SaveWorldId { get; private set; }
    // Seconds between automatic saves (only when something changed) and the time one frame may spend copying chunks.
    public float Interval { get; set; } = 300f;
    public float BudgetMilliseconds { get; set; } = 2f;
    public bool FlushOnDispose { get; set; } = true;
    public bool Busy => phase != Phase.Idle;
    public int Saves { get; private set; }
    public long LastRevision { get; private set; }
    public int LastBytes { get; private set; }
    public int LastChunksCopied { get; private set; }
    public int LastRounds { get; private set; }
    public double LongestSliceMilliseconds { get; private set; }
    // The longest a whole frame of this service took on the main thread while a save ran (copying, handing over to the worker, taking the result back).
    public double LongestUpdateMilliseconds { get; private set; }
    // Diagnostics of the last save: the slowest single chunk copy, and how many gen-0 collections the runtime did between the start of the save and its end.
    public double LongestChunkCopyMilliseconds { get; private set; }
    public int CollectionsDuringSave { get; private set; }
    private int collectionsAtStart;
    public string LastError { get; private set; }
    // Where the players stood in the save this world was restored from (and since): by player key.
    public IReadOnlyDictionary<string, Vector3> Players => players;
    public event Action<PNGridWorldSaveService> Saved;

    public static PNGridWorldSaveService Find(EveWorldContext world) => world != null && services.TryGetValue(world, out var value) ? value : null;

    // `restoredFrom` is the save the world was filled from (null for a world made by its map): the chunk versions and blobs it left are the baseline, so the first save only writes what changed since.
    public static PNGridWorldSaveService Create(EveWorldContext world, UnityEngine.SceneManagement.Scene scene, PNGridWorldController controller, PNGridWorldDescriptor descriptor, PNGridWorldSnapshot restoredFrom)
    {
        if (world == null || controller == null || descriptor == null) throw new ArgumentNullException(world == null ? nameof(world) : controller == null ? nameof(controller) : nameof(descriptor));
        if (services.ContainsKey(world)) throw new InvalidOperationException("This world already has a save service.");
        var root = new GameObject("PN Grid Save Service"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var service = root.AddComponent<PNGridWorldSaveService>();
        service.World = world; service.Controller = controller; service.Descriptor = descriptor.WithoutRestore(); service.SaveWorldId = Guid.NewGuid();
        int count = controller.Data.ChunkCount;
        service.savedVersion = new int[count]; service.blobs = new byte[count][];
        if (restoredFrom != null)
        {
            for (int i = 0; i < count; i++) { service.savedVersion[i] = controller.Data.ChunkVersionAt(i); service.blobs[i] = restoredFrom.Blobs[i]; }
            foreach (var player in restoredFrom.Players) service.players[player.Key] = new Vector3(player.X, player.Y, player.Z);
            service.savedPlacedSignature = service.PlacedSignature(); service.savedPlayersSignature = service.PlayersSignature();
        }
        else for (int i = 0; i < count; i++) service.savedVersion[i] = -1;
        services[world] = service; root.SetActive(true);
        return service;
    }

    // Turns saving on: the store (a folder of the caller's choosing) and the key the world is saved under. A mode calls this; a fixture calls it with a temporary folder.
    public void Configure(PNWorldFileStore store, string key)
    {
        PNWorldFileStore.ValidateKey(key);
        if (Busy) throw new InvalidOperationException("A save is running.");
        Store = store ?? throw new ArgumentNullException(nameof(store)); Key = key; sinceSave = 0f;
    }

    private void Update()
    {
        if (disposed || World == null || !World.CanExecute || !World.Session.HasAuthority) return;
        sinceSave += Time.unscaledDeltaTime;
        var clock = phase != Phase.Idle ? Stopwatch.StartNew() : null;
        Pump(BudgetMilliseconds);
        if (clock != null) LongestUpdateMilliseconds = Math.Max(LongestUpdateMilliseconds, clock.Elapsed.TotalMilliseconds);
        if (phase == Phase.Idle && Store != null && sinceSave >= Interval && IsDirty()) SaveNow();
    }

    // True when something a save keeps has changed since the last save.
    public bool IsDirty()
    {
        for (int i = 0; i < savedVersion.Length; i++) if (Data.ChunkVersionAt(i) != savedVersion[i] || blobs[i] == null) return true;
        return PlacedSignature() != savedPlacedSignature || PlayersSignature() != savedPlayersSignature;
    }

    // Starts a save. False when saving is not configured, one is already running, or nothing changed.
    public bool SaveNow()
    {
        if (disposed || Store == null || phase != Phase.Idle || !IsDirty()) return false;
        pending.Clear(); copies.Clear();
        for (int i = 0; i < savedVersion.Length; i++) if (Data.ChunkVersionAt(i) != savedVersion[i] || blobs[i] == null) pending.Add(i);
        cursor = 0; rounds = 0; phase = Phase.Capturing; LongestSliceMilliseconds = 0; LongestUpdateMilliseconds = 0; LongestChunkCopyMilliseconds = 0; collectionsAtStart = GC.CollectionCount(0); CollectionsDuringSave = 0; LastError = null;
        return true;
    }

    // One step of the running save: copies chunks for at most `milliseconds` (at least one), and when all are copied and still unchanged, starts the worker; when the worker is done, finishes. Public so a test can drive it.
    public void Pump(float milliseconds)
    {
        if (phase == Phase.Capturing) Capture(milliseconds);
        else if (phase == Phase.Writing && task.IsCompleted) Finish();
    }

    private void Capture(float milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (cursor < pending.Count)
        {
            int index = pending[cursor++]; long began = Stopwatch.GetTimestamp();
            copies.Add(PNGridChunkCopy.Capture(Data, index % Data.ChunksX, index / Data.ChunksX));
            LongestChunkCopyMilliseconds = Math.Max(LongestChunkCopyMilliseconds, (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
            if (clock.Elapsed.TotalMilliseconds >= milliseconds) break;
        }
        LongestSliceMilliseconds = Math.Max(LongestSliceMilliseconds, clock.Elapsed.TotalMilliseconds);
        if (cursor < pending.Count) return;
        // A chunk that changed while the others were being copied is copied again, so the file never holds half of an edit that spans chunks (after three rounds the chunk is written as it is and stays dirty).
        var changed = copies.Where(c => Data.ChunkVersionAt(c.Index) != c.Version).ToList();
        if (changed.Count > 0 && rounds < 3)
        {
            rounds++; pending.Clear(); pending.AddRange(changed.Select(c => c.Index)); copies.RemoveAll(c => changed.Contains(c)); cursor = 0; return;
        }
        Launch();
    }

    private Job Prepare()
    {
        var placed = Controller.Placements.All.Select(x => new PNGridSavedPlacement { Id = x.Id, Definition = x.Definition.Id.ToString(), X = x.X, Z = x.Z, Direction = x.Direction, Health = x.Health }).ToArray();
        CapturePlayers();
        var kept = players.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new PNGridSavedPlayer { Key = x.Key, X = x.Value.x, Y = x.Value.y, Z = x.Value.z }).ToArray();
        var definition = World.Session.Registry.Get<PNWorldDefinition>().Get(World.DefinitionId);
        return new Job
        {
            Copies = copies.ToArray(), Placed = placed, Players = kept, Time = Math.Max(0, World.GameplayTime), Descriptor = Descriptor, Palette = Data.Palette, Blobs = blobs,
            Store = Store, Key = Key, SaveId = SaveWorldId, DefinitionId = World.DefinitionId.ToString(), ContentHash = definition.ContentHash, Compatibility = World.Session.Compatibility,
            PlacedSignature = PlacedSignature(), PlayersSignature = PlayersSignature()
        };
    }

    private void Launch()
    {
        job = Prepare(); LastChunksCopied = job.Copies.Length; LastRounds = rounds;
        phase = Phase.Writing; var captured = job;
        task = Task.Run(() => Write(captured));
    }

    // Runs on the worker: only the immutable job and the blob cache (which nobody else touches while a save runs).
    private static Job Write(Job work)
    {
        foreach (var copy in work.Copies) { work.Blobs[copy.Index] = copy.Encode(); copy.Release(); }
        var snapshot = new PNGridWorldSnapshot(work.Descriptor, work.Palette, work.Blobs, work.Placed, work.Players);
        var envelope = PNWorldSaveEnvelope.Create(work.SaveId, work.DefinitionId, work.ContentHash, work.Compatibility, work.Descriptor.ToJson(), new JObject(), snapshot.ToState(work.Time));
        work.Revision = work.Store.Write(work.Key, envelope); work.Bytes = envelope.Bytes;
        return work;
    }

    private void Finish()
    {
        var finished = job; var running = task; job = null; task = null; phase = Phase.Idle; copies.Clear(); pending.Clear(); sinceSave = 0f;
        if (running.IsFaulted || running.IsCanceled)
        {
            var error = running.Exception?.GetBaseException(); LastError = error != null ? error.Message : "The save was cancelled.";
            UnityEngine.Debug.LogWarning("Grid world save failed: " + LastError); return;
        }
        CollectionsDuringSave = GC.CollectionCount(0) - collectionsAtStart;
        foreach (var copy in finished.Copies) savedVersion[copy.Index] = copy.Version;
        savedPlacedSignature = finished.PlacedSignature; savedPlayersSignature = finished.PlayersSignature;
        Saves++; LastRevision = finished.Revision; LastBytes = finished.Bytes;
        Saved?.Invoke(this);
    }

    // Saves now and waits for the file (blocking): for leaving the world or the game. False when saving is off or the save failed.
    public bool Flush()
    {
        if (disposed && !flushing) return false;
        if (Store == null) return false;
        if (phase == Phase.Writing) { try { task.Wait(); } catch (AggregateException) { } Finish(); }
        else if (phase == Phase.Capturing) { phase = Phase.Idle; copies.Clear(); pending.Clear(); }
        if (!IsDirty()) return LastError == null;
        if (!SaveNow()) return false;
        while (phase == Phase.Capturing) Capture(float.MaxValue);
        try { task.Wait(); } catch (AggregateException) { }
        Finish();
        return LastError == null;
    }
    private bool flushing;

    private void CapturePlayers()
    {
        foreach (var loader in World.Session.Entities.Snapshot(World))
        {
            if (loader == null || !loader.IsReady || !PNEntityRoles.IsPlayer(loader)) continue;
            var stat = loader.GetComponent<PNStat>(); if (stat != null && stat.IsDead) continue;
            var key = "owner:" + loader.LoadContext.Identity.OwnerId;
            if (!players.ContainsKey(key) && players.Count >= PNGridSaveLimits.MaxPlayers) continue;
            players[key] = loader.transform.position;
        }
    }

    private int PlacedSignature()
    {
        unchecked
        {
            int hash = 17;
            foreach (var x in Controller.Placements.All) hash = hash * 31 + (x.Id * 7 + x.X * 13 + x.Z * 17 + x.Direction * 19 + x.Health * 23);
            return hash * 31 + Controller.Placements.Count;
        }
    }

    // Where the living players stand now, to the nearest half metre; a player who has left keeps the last place it was saved at.
    private int PlayersSignature()
    {
        unchecked
        {
            var now = new Dictionary<string, Vector3>(players);
            foreach (var loader in World.Session.Entities.Snapshot(World))
            {
                if (loader == null || !loader.IsReady || !PNEntityRoles.IsPlayer(loader)) continue;
                var stat = loader.GetComponent<PNStat>(); if (stat != null && stat.IsDead) continue;
                now["owner:" + loader.LoadContext.Identity.OwnerId] = loader.transform.position;
            }
            int hash = 29;
            foreach (var pair in now.OrderBy(x => x.Key, StringComparer.Ordinal)) hash = hash * 31 + pair.Key.GetHashCode() * 3 + Mathf.RoundToInt(pair.Value.x * 2) * 7 + Mathf.RoundToInt(pair.Value.y * 2) * 11 + Mathf.RoundToInt(pair.Value.z * 2) * 13;
            return hash;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (FlushOnDispose && Store != null) { flushing = true; try { Flush(); } catch (Exception error) { UnityEngine.Debug.LogWarning("Grid world final save failed: " + error.Message); } flushing = false; }
        else if (phase == Phase.Writing) { try { task.Wait(); } catch (AggregateException) { } }
        disposed = true;
        if (World != null) services.Remove(World);
        if (this != null) DestroyImmediate(gameObject);
    }

    private void OnDestroy() { if (!disposed) { disposed = true; if (World != null) services.Remove(World); } }
}
#endif
