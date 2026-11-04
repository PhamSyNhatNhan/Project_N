# State and command layer / Lớp đồng bộ trạng thái và lệnh

EN: One neutral way for gameplay to publish a small block of state from the authority (host or offline) and for a client to send a command back, for anything that is not
a Stat, Buff or Move. Design, decisions and the steps still to do: `Docs/NetworkStatePlan.md`. Core and Game never see Mirror: `IPNSyncTransport` is the seam, `Assets/Coop`
carries it (step N3, done 2026-11-04: `Assets/Coop/PNSyncTransport.cs`, two Reliable messages, NetworkVersion 13 since the Entity scope, N4 done 2026-11-04). Offline and a host with no client use the service as it is.

VI: Một cách trung lập để gameplay công bố khối trạng thái nhỏ từ authority và để client gửi lệnh lên, cho mọi thứ không phải Stat/Buff/Move.

## Parts

| Type | Where | Role |
|---|---|---|
| `PNSyncTarget` | Core | What a block or command addresses: `WorldObject` (world instance + incarnation + content slot + key), `Session` (the whole play), `Entity` (one life of an entity: id + generation; N4). |
| `PNSyncStateDefinition` | Core | Registered block: id, schema, byte bound (<= 4096), allowed target kinds. Registry `hub.SyncStates()`. |
| `PNSyncCommandDefinition` | Core | Registered command: id, byte bound (<= 512), allowed target kinds, handler class + factory (no reflection). Registry `hub.SyncCommands()`. |
| `IPNSyncStateSource` / `IPNSyncStateSink` | Core | Writer (authority only, one per target+block) and reader (every machine, the authority too, so a view reads the same way everywhere). |
| `IPNSyncCommandHandler` | Core | Runs on the authority: validates and acts, marks blocks dirty if it changes state. |
| `PNSyncService` | Core | The per-play service: objects, writers, readers, revisions, `Flush`, `Send`, `ReceiveState`, `ReceiveCommand`, `Snapshot`, `RemoveWorld`, `RemoveEntity`. Pure C#. Reached as `PNGameSession.Sync`. |
| `PNWorldObject` | Core | Component on an authored object of a world scene: its key. **Not an entity.** |
| `PNSyncWorldBinder` | Game | Registers the `PNWorldObject`s of every content slot when a world is ready, forgets them when it unloads. |
| `PNSyncEntity` | Game | Component on an entity (an `EveIEntityLoadStep`): registers the entity's Entity target when it commits and, on unload, tells `IPNSyncEntityListener`s and calls `RemoveEntity`. Add it only to entities with a block or command to sync. |

## Rules

- A block holds the latest value, not events. Revisions per (target, block) start at 1; a value equal to the last one keeps its revision. A late reader is given the latest value at once.
- `MarkDirty` then `Flush` (once a frame, from `PNGameSession.Update`): capture, bound check, next revision, readers on this machine, transport. Failures of one block do not stop the others.
- Payloads are plain JSON: finite numbers, at most 16 levels, within the definition's bound. Keys: `a-z 0-9 _ . -`, at most 64. Slot ids at most 128. At most 8 blocks per target.
- Two blocks have no order between them (nor with Stat/Buff/Move): values that must arrive together go in one block.
- A command to a target that is gone is dropped (stale, not hostile); a malformed one throws (protocol error).
- A world that repeats or misspells a `PNWorldObject` key (or repeats a `PNRoomSpawnGroup` key) in one content slot fails to load (`PNWorldRuntime.Validate`).

## Add a block or a command

1. Register the definition in a content module (`hub.SyncStates().Register(new PNSyncStateDefinition(id, schema, bytes, targets))`; commands need a handler class and `() => new Handler()`).
2. The owner component registers a source (authority) and/or sink with `PNGameSession.Sync.RegisterSource/RegisterSink(target, ...)` and disposes the lease when it goes away; it calls `MarkDirty` after a change.
3. A client acts with `Sync.Send(target, commandId, payload)`; the same call on the authority runs the handler directly.

## Checks

Smoke `Tests/EveEngine/PNSyncTests.cs`; native `PNSyncWorldChecks.Run()` (worlds, objects, refusals) and `PNSyncWorldChecks.RunMap()` (the sample map). Coop/wire checks come with N3.

## Entity scope (N4)

- An Entity target is `(entity id, generation)`, the generation being the loader's `LoadContext.Generation`: a pooled or rebuilt entity is a new target, so state of an old life cannot reach the new one.
- `RemoveEntity(entity, generation)` forgets objects, blocks, readers and writers of every generation up to it and retires them for good: `RegisterObject/Source/Sink` throw, `MarkDirty`/`Send`/`ReceiveCommand`/`ReceiveState` ignore it (stale, not hostile).
- Wire (`PNSyncStateMessage`/`PNSyncCommandMessage` carry `Entity` and `Generation`; the world comes from the host's binding, never from the sender): the host sends an entity's state only to a peer that observes its shell, a first image when the peer starts to see it, and drops waiting messages of a life the peer lost. A client accepts the state only for a world it has loaded and keeps it in its service even before the entity appears, so the entity reads it when it binds. A client command is run only for a life the sender observes. A client world closing forgets the lives that world delivered.
- Late join into a run is still refused by design, so "late joiner gets the state before the entity shows" is covered by the first image on observation, not by a real late client.

## Gamepad and touch (list and end button)

Checked in a Player with virtual Input System devices (2026-11-04): a Gamepad (right-stick press uses the entry, D-pad moves the selection, South presses the focused end button) and a Touchscreen (a tap on a row, a tap on the end button). The key label on the selected row follows the device in use (`R3` after a pad is touched).
