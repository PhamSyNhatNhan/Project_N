# Move replication (RulesVersion73)

EveEngine.Movement contains pure protocol models and a bounded binary codec. PNMoveConnections binds them to
published Unity actors. Enrollment/transport remain explicit; adding PNMove to a scene does not create a network.
Host gameplay authority, Stat writes, Skill admission and damage stay authoritative. Client locomotion prediction
uses a separate capability. It never grants CanMutate. No production transport, Skill cast prediction, anti-cheat,
forced-motion mechanic, gameplay rollback or arbitrary new movement algorithm is supplied by this slice.

## Integration

After trusted compatibility/admission, construct ONE PNMoveHostWorld for the host session. Track published PNMove
actors with remoteOwned=true only for an admitted remote-owned player, false for host-driven actors. Create a
PNMoveHostConnection for each admitted peer. At a drained boundary after gameplay advancement, call world.Advance
with host monotonic seconds, then connection.Advance and Send. Dispatch authenticated upstream bytes to world.Receive.
Do not feed pose bytes through actor JSON/base64 or HandleCommand. Actors still use the existing lifecycle adapter.

On the client, AdmitHost(hostSession,compatibility,localOwnerId) must have completed. Construct PNMoveClientConnection
for that trusted host, Bind each exact published PNMove after actor Ready, and Unbind it at retirement. Feed bytes only
from that host connection into Receive. Call AdvanceObservers with an estimated HOST monotonic time (transport owns
clock mapping), and SendReports to the upstream transport. A replacement actor uses its actual EntityId/Generation;
never substitute a connection incarnation for entity generation. Disable/unload invalidates pending body writes and
execution tokens. Same-generation owner rebind needs a fresh matching requested baseline; observer rebind retains its
sequence/epoch floor and waits for a later full state. Dispose connections/world at session teardown.

Owner BootstrapRequest is retried until a matching full mirror/correction applies. The request ID is session-monotonic,
echoed by the host; the correction also supplies a report sequence floor. Repeated requests do not allocate a new epoch.
The owner stays kinematic until the compatible baseline applies at the next fixed boundary. Pre-Ready/unbound messages
are dropped, not queued without a bound. Newer mirrors are retained when an older correction arrives. Physics settings
owned by PNMove freeze are restored locally; network data never supplies arbitrary Rigidbody constraints/gravity flags.

A newer life revision fences pending older-life corrections. Dead owners hold their physics body, including in the air;
revive waits for its compatible new-life correction before releasing that hold. This does not freeze the entity clock or
raise client gameplay Died. Host Move disable/re-enable retains the model fences and emits one current baseline on resume;
it does not wait forever for a client component rebind that may never happen.

Send returns true only when transport accepts OWNERSHIP of a batch. A false return preserves an identical offered batch;
latest slots outside that batch may be replaced. Transport must authenticate the connection, bound accepted buffers and
honor its reliability contract; these classes are not a socket implementation. Periodic complete notices and bootstrap
retries additionally recover the deterministic loss fixture. World/connection enrollment is bounded at256 actors,
history64 states, codec8704 bytes, <=64 permission IDs, <=128 strict UTF8 bytes per ID. These are Move carrier limits,
not a change to gameplay/actor/Buff caps. Exceeding a limit is explicit; do not silently stop enrolling visible actors.

## State and ordering

Wire v1 kinds: PoseReport1, Mirror2, Correction3, PoseState4, BaselineRequest5. Fixed little-endian numbers; Guid is
.NET ToByteArray order. Identity includes host Guid + signed Int64 positive entity/generation. Coordinates are metres
(absolute <=1e6); physical linear velocity and separate unscaled recovery velocity/angular velocity <=1e5; finite
numbers only. Current facing is +/-X and grounded/rising/falling is a locomotion view, not a list of producers. A new
orientation or extension schema needs explicit versioning. uint counters use half-range ordering, zero reserved.

Host accepts only the authenticated owner and current session/entity/generation, newer report sequence, current epoch
and correction ID, and no future mirror revision. Current hard constraints beat the owner's old mirror. Freeze holds
pose and zero physical velocity; a walk lock preserves vertical physics and exempts only an actual permitted host motion
execution. No speed/distance anti-cheat is inferred. A starved remote-owned host body holds the last accepted state; it
does not integrate old velocity indefinitely. The initial remote dead-body policy is hold until a later life/baseline
transition. Host-driven actors retain existing gameplay physics. Stat publisher alone owns the shared lifeRevision.

Owner reports represent the previous COMPLETED physics step and intervening Update producers, AFTER any queued correction
and BEFORE this fixed step's locomotion/physics. Corrections apply before locomotion via PNMove's explicit hook. Pose,
physical velocity, facing, locomotion view and unscaled freeze recovery are applied coherently. A newer mirror controls
current hard constraints; no rollback to an older mirror. Simulation corrections snap; no owner visual-offset blend is
implemented. Observer position samples interpolate with100ms delay and hold at starvation; epoch changes cut history.
Observer rendering never extrapolates from physical velocity, which can be zero for position-driven motion.

## Producer boundary

No new Move registry. Existing Action/Operation factories and selected overrides remain. PNMotionAction on PNMove requires
BindMovementOwner BEFORE Start. All existing owners bind exact-instance callbacks: auxiliary PNSkillEffects, primary Eve
moveset and shared player Dash. An execution token owns exclusive PLANAR displacement; ordinary vertical physics continues.
The motion's lease suppresses walking; final motion displacement retains its capsule sweep. A competing exclusive motion
is rejected. New additive/vertical producers need a declared composition/collision implementation, not another mode enum.

Before invalidation calls the owner, the old token is fenced and removed. Callback failure cannot restore its write right;
fallback Dispose releases Action resources. Re-entry during the transition rejects a replacement, then clears the gate in
finally so a later execution can start. Teleport, death, rejected branch and authority corrections cancel through the
owner. Freeze pauses; ordinary constraint correction continues from corrected current pose if permission remains legal.
This initial motion cancels on permission revocation. Host PNMotionAction still advances authoritative Skill timing for
a remote owner, but never writes that body's pose. Skill owns costs, Dash lifecycle and refunds; Move never rolls them back.

RejectBranch/start-pose anchors exist only as reserved model APIs and tests. No cast/INTENT/anchor adapter is connected.
New enemy trajectories can publish the same result state, but still need their own registered execution/data, permissions,
clock, collision and lifecycle implementation. Orbit is an example, not an implemented feature.

## Failure and evidence

Malformed upstream packets return Malformed and retain LastProtocolError; wrong identity/owner/stale/fenced packets cannot
write a body. Client malformed packets return false and retain LastError; later complete notices may recover. A host
sampling failure is isolated per actor in SamplingFailure(identity); it retains the last accepted state/mirror and refuses
reports while current constraints cannot be read. It does not skip gameplay ticks or fabricate a new life observer.
An owner apply failure does not acknowledge the correction. Teardown clears subscribers, queued work and owned settings.
Stale-input disconnect deadlines are session policy; not invented here.

Evidence and limits: .utmp/Step6/Move/Report.md, Execution.md, NativeOut, IPC*, smoke/golden/mutation artifacts.
Standalone substitutes do not establish physics. Clone-only PNMoveReplicationEditorChecks manually steps an EMPTY default
physics scene and MUST NOT run on an authored scene. Play review uses a disposable test scene. Loopback Player fixture
uses known pre-created actors and tests the actual Move adapters, not production actor admission or two-machine gameplay.
