
## Current: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

Buff registration requires explicit ImplementationId; Derive preserves it. Buff and
Area reference schemas use validateReferences/PNContentReferenceContext, including
dependency/alias/staged batch checks; property-name inference was removed. Nested
Action/Operation schemas may delegate their registered reference callbacks.
Stat resource max callbacks run only when max actually changes. Buff stat-source
keys use checked instance+sequence, retained through behavior changes/resume.
Automatic Buff manager clock/runtime failures retire only its collection and cleanup;
caller argument errors and per-behavior callback errors retain separate handling.

Buff gameplay is authoritative. Host/solo and client UI/VFX use a common replica
presentation source; UI Numbers and VFX clock data are separate. Elemental VFX is
presenter-owned/grouped, not acquired by gameplay behavior. No production transport,
Move/Skill/HP/Area replication is implied. See Assets/Core/Entity/Shared/Buff/PNBuffCoopREADME.md for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

# Registered areas

RulesVersion54 / Mod API2. Areas are session-owned execution instances. A manager
dispatches a selected registry definition; individual implementations own geometry,
input schema, clocks, admission and gameplay behavior. The manager does not require
every area to have a sphere, duration, Buff or entity owner.

## Registration and overrides

`PNAreaDefinition` is registered at content startup in the named `areas` registry.
Payload is cloned; validation runs before publishing the sealed content session.
The selected definition constructs an inactive pool template containing its concrete
`PNAreaInstance` component. Manager reuses clones through `EveEasyPooling`; it never
constructs a replacement implementation directly. Factories belong to registration,
not Skill/input/entity initialization. Base aliases support compatible selected
implementations. Pools are partitioned by selected definition and scene.

JSON can derive a registered factory/schema:

```json
{"extensions":{"project_n":{"areas":[{
  "id":"addon:slow_field",
  "factoryId":"base:timescale_area",
  "data":{"buffId":"base:timescale_reduction_area","pollPeriod":0.05,
          "fadeIn":0.1,"fadeOut":0.3}
}]}}}
```

The pack must own `addon` and declare `project_n` as a dependency. References to
Buffs in the same extension batch are validated before commit. `factoryId` references
an already code-registered area factory. Unknown fields/nonfinite inputs are rejected.
Area metadata, schemas and create-operation contracts are available to standalone
checks; actual component/physics/rendering execution remains native Unity-only.
The standalone operation validates payload/context but does not spawn a region.

## Create, inspect and end

In an authoritative Ready session, use the runtime's `PNAreaManager`:

```csharp
var manager = PNAreaManager.Attach(runtime);
var input = JObject.Parse(@"{
  'radius':3,'reduction':0.5,'minimumReduction':0.2,
  'duration':15,'dependent':false,'layerMask':-1,'layerMode':'Include'
}");
var receipt = manager.Create(new PNAreaCreateRequest(
    EveId.Parse("base:timescale_area"), input, PNBuffSource.Capture(caster),
    caster.gameObject.scene, position, owner: caster));
if (receipt.Status == PNAreaStatus.Created) {
    var handle = receipt.Handle;
    // handle.IsAlive; handle.End() ends gameplay immediately.
}
```

`manager.Get(handle)` and `manager.Snapshot()` expose immutable ID/profile,
position, running state and end reason, including instances retiring their VFX.
Requests during manager mutations may be Pending; inspect final receipt rather
than treating Pending as success. Invalid initialization returns Failed with Error;
inactive/non-authoritative sessions reject creation. Handle belongs to one manager
and monotonically numbered use, never the reusable GameObject. Old handles cannot
end a later use. Definition/input data is separate from mutable instance state.

Skill Action `base:area_action` executes registered Operation `base:create_area`:

```json
{"operationId":"base:create_area","data":{
  "areaId":"base:timescale_area","followCaster":false,
  "input":{"radius":3,"reduction":0.5,"minimumReduction":0.2,
           "duration":15,"dependent":true,"layerMask":-1,"layerMode":"Include"}
}}
```

The invocation must supply Position. `PNCreateAreaOperation.Result` is the receipt.
Disposing/canceling the granting Operation does not cancel a manager-owned area.
System callers may provide stable `PNBuffSource` attribution without fabricating a
Skill context. Client gameplay creation is rejected; area replication is not implemented.

## Timescale area

`base:timescale_area` uses root non-trigger body collider intersection with a 3D
sphere, deduplicated by entity identity in the instance's physics scene. LayerMask
uses root entity layer, with explicit Include or Exclude. All listed input fields
are required; `duration:null` means permanent and requires `dependent:true` with a
live owner. Radius is positive; reduction/minimumReduction are nonnegative finite
floats, may exceed 1. Duration is positive finite Game seconds.

Owner, source attribution and follow target are separate. Dependent instances end
on owner death/disable/unload/destroy; reenabling does not restore them. Independent
timed instances survive source loss. A lost follow entity leaves its last valid
position. Scene unload ends instances in that scene; session shutdown ends all areas.

Poll period is registered `.05` Game seconds. Admission first runs after activation.
Long frames advance lifetime fully but query current geometry once rather than
inventing historical contacts. Fast traversal between samples may be missed; this
is a persistent field, not a guaranteed swept-contact trap.

Each area-target pair maintains one exact Buff grant. Alive or Pending grants are
not repeatedly Applied. Immunity leaves waiting membership; deleted Buffs reapply
at a later poll. Exit/invalid layer/ending revokes only its own grant via normal
bypass cleanup. Pending grants are revoked after their mutation resolves. Multiple
regions (including same caster) remain independent and add modifiers normally.

Gameplay ends before VFX retirement. Fade-in `.1` and fade-out `.3` use Game clock;
pool return waits for fade. Disable, scene/session teardown and runtime faults skip
retirement. External OnDisable cleans gameplay immediately and defers pool return
until sceneUnloaded/next manager update, avoiding storage destruction races.
No global timeScale, Control lock or forced Animator/Rigidbody speed.

## Pool ownership and implementation

Implement `Initialize`, `Advance`, `Cleanup`; optional `AdvanceRetirement` and
`RetirementDuration`. `Initialize` receives cloned request input after inactive Get.
Only manager returns objects to their pool. Do not call pool Release recursively
from OnDisable/pool hooks. Clear targets, callback subscriptions, timers and VFX in
cleanup/reset; handle generation must gate late callbacks. `OnPoolRelease` overrides
must call base. Teardown also runs OnDestroy cleanup because pool Dispose does not
invoke release hooks. An implementation fault ends that instance and is reported.

## Rendering

`PNTimeScaleAreaRendering` injects a URP RenderGraph pass at runtime for Base Game
cameras with UniversalAdditionalCameraData. No renderer asset/scene changes. It uses
scene depth and world-space ray/sphere intersections, then maximum coverage across
same-style spheres. Gameplay does not merge. Purple filter, desaturation, chromatic
offset and soft boundary preserve the old TimeStop direction inside the volume.
Depthless transparent surfaces inherit the opaque background's mask; this is not
per-transparent-object volumetric compositing. XR/overlay/custom renderer paths
require their own verification. No extra gameplay objects are created per target
for screen VFX. Screen-pass materials/buffers/subscriptions retire after the last visual grant; pooled dust materials retire when the pool destroys their instance.

Checks: `PNAreaEditorChecks.Run()` in stopped Edit Mode and
`PNAreaPlayReview.Run()` in Ready unpaused Play. Evidence `.utmp/AreaReview`.


## Historical floor depth / legacy-feel visuals (RulesVersion56; superseded below)

The registered timescale definition now includes optional `visual` payload. Defaults
are authoritative in PNTimeScaleAreaVisualSettings.DefaultPayload and explicitly
registered: particleLifetime2.5, speed1, size.1, rate10, maximum20; colorFilter
[1.1607844,0,1.6549022], saturation-30, chromaticAberration.3, vignetteIntensity.4,
vignetteSmoothness.5 and purple vignette/dust colors. When present, every visual
field is required and validated; older derived payloads omit the object and share
these same defaults. Selected profiles retain their own validated visual data.

Chromatic uses URP's radial cubic channel displacement. Vignette is a soft regional
falloff rather than only a narrow rim; coincident/intersecting regions take maximum
coverage and minimum interior darkening. ParticleSystem children/materials are retained
by the region's existing pool, cleared on return, disposed with the pool. Simulation
is manual Game delta exactly once; parent activation starts it safely, pause holds
particles, source entity scale/death does not change independent-region visual time.
Duplicate occupancy in overlapping spheres belongs to the earlier active visual.
Dust is depth-tested/soft-intersected; no real particle shadows or LensDistortion
were present in the old effect, and none are added automatically.

ErModeGroundVisual preserves the visible SpriteRenderer/shared material and builds
an owned depth-only mesh from the generated opaque grid sprite vertices/triangles.
The proxy follows the floor transform, with a1mm local-back offset to avoid z-fighting;
it writes opaque Z and URP DepthOnly R encoding, without writing visible color.
No collider, renderer asset, authored scene save or general sprite material change.
Refresh from OnValidate is deferred to avoid destroying/creating meshes inside
validation callbacks. Disable/rebuild retires mesh/material/object. This depth proxy
is for the component's fully opaque generated floor; it is not a transparency-depth
solution for every sprite. Area RenderGraph explicitly reads the camera depth handle
and uses the current camera inverse GPU view/projection matrix.

Checks: PNTimeScaleVisualEditorChecks.Run uses the actual floor component and current
camera pose, with original-vs-depth A/B, completely buried sphere, visible dust,
intersections/camera-inside/fade/pool reuse. PNTimeScaleVisualPlayReview.Run verifies
actual Update, pause, source zero scale/unload and pool teardown.
Evidence `.utmp/TimeScaleVisualReview`; Player/CoreCLR/XR remain separate checks.


Retained-resource destruction hook: `PNAreaInstance.DisposePooledResources()` must
be idempotent. Manager caches component CLR references at Pool.Created and invokes
this hook from Pool.Destroyed as well as normal OnDestroy. In native Edit fixtures,
MonoBehaviour OnDestroy alone is insufficient; the native GameObject may already be
gone when the pool notification runs. Dispose retained material/buffer references
without dereferencing destroyed native objects. Gameplay Stop/Cleanup remains separate
from idle-pool resource destruction. Native teardown asserts dust material count0.


## Private floor depth and readable streak visuals (RulesVersion57)

Supersedes RulesVersion56 global floor-Z proxy. PNVfxFloorDepth registers owned floor
geometry with a custom VFXFloorDepth-only shader pass; no UniversalForward/DepthOnly
pass writes into main scene Z. Per Base Game camera, a private R32 depth + depth
attachment is rasterized once after opaques and before transparent particles. Area
post and streak shader sample nearest(scene depth,private floor depth). The main
SpriteRenderer/billboard materials and positions stay unchanged, including portrait
parts below ground. Existing opaque wall/foreground depth remains authoritative.
No ZTest Always on character sprites, collider/Visual edits or renderer-asset changes.
Transient textures are RenderGraph-owned; registration retires on floor disable.

Default visual: tintStrength.25 (blend rather than full multiplication), saturation-10,
chromatic.12, vignette.15/smoothness.5. Cubic chromatic and weak moving noise are
localized to sphere boundary; same-style overlaps suppress interior darkening/rims.
New optional fields tintStrength,edgeDistortion(.0008),particleAspect(3),particleOpacity(.3)
use authoritative DefaultPayload values when omitted by older profiles.

Dust now uses Hovl Glow1 texture as narrow billboard streaks: size.025 with variation,
aspect3, randomized angle, speed.05, lifetime3, rate6, maximum20; soft lifetime fade.
PNVfx/TimeScaleTextures resource asset references original Glow1 and Noise1 from Pack,
without copying/editing textures or their GUIDs. Game simulation/pooling unchanged.
Samples from imported hit/buff prefabs are references, not automatically instantiated
into gameplay. Main tint preserves most original RGB, particle brightness remains low.

Native review reconstructs Dummy's physical-rest root with its existing billboard
pose/material, verifies below-floor sprite geometry remains visible, and compares
buried region with private depth on/off. Readable tint preserves green detail; opaque
foreground regression still passes. Evidence `.utmp/TimeScaleReadableReview`.


## Stronger time boundary (RulesVersion58)

Default tintStrength is now .4 and saturation -20; particle settings and texture
references remain as in57. Boundary is a soft purple band with registered optional
rimIntensity.18, rimWidth.18 (normalized radius), rimPulse.12. EdgeDistortion.004
refracts all RGB channels together along the projected sphere normal, with slow
Game-time noise/pulse. Chromatic separation is now focused on the boundary rather
than the interior; center remains comparatively clean. Pulse uses region Game visual
time (.55 radians/sec), not global/unscaled shader time. Equal-strength overlapping
regions suppress internal rims/refraction via minimum boundary factor. Private floor
depth and untouched billboard rendering remain. Native wide captures isolate animated
boundary by temporarily hiding fixture particles; no production particle changes.
Evidence `.utmp/TimeScaleBoundaryReview`.

## Sharp floor contact and creation impulse (RulesVersion59)

Supersedes58 boundary defaults: edgeDistortion.006, rimIntensity.12, rimWidth.1,
rimPulse.12. The floor intersection uses the union of sphere signed distances,
with a narrow contact line rather than a broad thickness fade. Visible volume
coverage uses the finite camera-to-depth ray segment with pixel antialiasing;
fully buried regions remain hidden by private floor depth. Derivatives are taken
outside the dynamic sphere loop to support the native shader compiler.

Optional visual fields use the registered DefaultPayload when omitted:
contactWidth.015 world units (range.001.. .2), contactIntensity.35 (0..1),
birthDuration.22 Game seconds (.01..1.5), birthIntensity.35 (0..1).
The creation impulse expands once from the center, bends the sampled scene and
fades away. Game delta0 freezes it; pooled reuse resets visual age. Seven float4
vectors per region carry these values. TintStrength remains.4/saturation-20,
with center tint scaled to65% and full tint at the shell. Particles unchanged.
Native captures isolate floor contact, creation movement/pause and shell breathing.
Evidence `.utmp/TimeScaleContactReview`.

## Slowly twisting purple volume (RulesVersion60)

Supersedes59's contact outline and creation impulse: both are removed from rendering.
Private floor depth still clips volume coverage with only pixel antialiasing. Historical
contactWidth/contactIntensity/birthIntensity fields remain accepted for derived-profile
compatibility but no longer produce a line or wave. Defaults contactIntensity0 and
birthIntensity0. BirthDuration now controls opening of the volume (default.35 Game s).

VisualRadius opens from0 to the requested Radius with smoothstep; retirement contracts
from the current visual radius to0 over registered fadeOut. Gameplay Radius/polling/
Buff grants are unchanged. Interrupted opening contracts from its current size, without
jumping to full radius. Particle local volume scales with VisualRadius/Radius; original
texture, size, speed, emission and lifetime settings remain unchanged. Game delta0
freezes visual size and procedural motion; pool initialization resets both.

Default tintStrength.58, edgeDistortion.009. Optional twistStrength.018 (UV displacement,
range0.. .05), twistSpeed.45 (phase radians per Game second,0..2) come from the same
authoritative DefaultPayload. Tangential travelling curved bands plus radial noise bend
the sampled scene throughout the volume, stronger near its shell. Purple center tint
uses78% of tintStrength, shell100%. Sparse moving shell highlights replace the uniform
bright rim; same-strength overlap still suppresses interior rims and stacked tint.
Native frames compare motion with particles hidden, opening/closing radius, frozen
Game delta, particle scale, buried VFX and full resting portrait. Evidence
`.utmp/TimeScaleTwistReview`; Player/CoreCLR/XR remain unverified.

### P1 S3: play teardown ownership

Game binds this manager once per play and registers Shutdown as required session cleanup
before mod EndPlay. Shutdown retains failed gameplay Cleanup entries and pools instead
of evicting them. Failed pooled-resource disposal remains owned for retry as well; a
successful gameplay cleanup is not repeated just because resource disposal retries.
New creation is closed during teardown. Fix the cause and call session RetryCleanup;
new plays remain blocked while required cleanup is pending. Cleanup implementations
must tolerate partial initialization/retry; resource disposal stays idempotent.
