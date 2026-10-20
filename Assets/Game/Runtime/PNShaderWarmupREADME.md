# Shader warm-up (compile GPU programs under the transition cover)

Docs: `Docs/ShaderWarmupPlan.md` (plan and decisions). Code: `Core/Rendering/PNShaderWarmupDefinition.cs` (registered data), `Game/Runtime/PNShaderWarmup.cs`
(`PNShaderWarmupContent` registration + `PNShaderWarmup` runtime), `PNTransitionController` / `PNTransitionMachine` (the hold). Presentation only: no gameplay rule or
network message depends on it, no RulesVersion bump.

## Why
A shader's GPU programs are compiled by the driver the first time it is drawn. On DX12 (the Windows default since 2026-10-21, DX11 fallback) and Vulkan that is also when the
pipeline state is built, so the first Glass shatter cost ~60 ms and the first Eve volley a similar hitch. The game compiles them once, behind the dark Join / Transfer screen.

## Rule for authors
A shader that a game system, a skill VFX, a Buff visual or a mod draws **must be registered**:
```csharp
var registry = hub.GetOrCreate<PNShaderWarmupDefinition>();
var id = EveId.Parse("project_n:shader_myeffect");
registry.Register(new PNShaderWarmupDefinition(id, "Project_N/VFX/MyEffect"));            // optional second argument: keywords to warm
registry.RegisterBase(EveId.Parse("base:shader_myeffect"), id, "project_n");              // game content only; mods register under their own namespace
```
Game shaders are listed in `PNShaderWarmupContent.Names`. Every pass type the shader has is warmed (untagged = `Normal`, `SRPDefaultUnlit`, `UniversalForward`/custom
light modes = `ScriptableRenderPipeline`); the types it lacks are skipped silently. A shader that is missing or unsupported logs one warning and never blocks the session.
Keep a one-time CPU preparation (materials, meshes, textures built in code) out of the shader list: `PNShaderWarmup.Pump` calls `PNLoricaShatter.Warm()` once; add a similar
call there for another system that builds assets lazily.

## When it runs
- `PNTransitionController.Bind` calls `PNShaderWarmup.Begin(session)`: once per **content generation** (`EveContentContext.GenerationId`), so later sessions with the same
  content skip it, and a content reload (for example after a mod is toggled) starts over. A future mod screen calls `Begin` again after reloading content.
- Every controller `Update`: `AddWorld(local.World)` (once per world instance; scans every `Renderer` material of the world's physics scene with its enabled keywords, so the
  real variants of URP shaders and of shaders that came in a mod's scene bundle are warmed) and `Pump()` (4 variants per frame, so the cover keeps animating).
- While work is pending the controller reports `PNTransitionPhase.Loading` with `PNShaderWarmup.Fraction`, `ConditionsMet()` is false and the machine is **held**
  (`PNTransitionMachine.Update(..., holding: true)`): the cycle neither hides nor expires at `MaximumSeconds`. There is deliberately no time limit: the work is done once.
- No controller (`--pn-legacy-camera`), no screen, or no local actor: nothing blocks; without a controller nothing pumps (the review harness pumps explicitly).

## Phase B: recorded graphics states (DX12, Vulkan, Metal)
Shader variants are not the whole cost on DX12: each draw also needs a pipeline state object (PSO). `GraphicsStateCollection` records the PSOs a real run used and compiles
them ahead of time. DX11 has no PSOs, so there the recording is skipped (the variant warm-up above still applies).
- **File**: `Assets/StreamingAssets/GraphicsStates/<RuntimePlatform>_<GraphicsDeviceType>.graphicsstate` (now `WindowsPlayer_Direct3D12`, 29 states from three recordings). `PNShaderWarmup.Begin`
  loads it (skipped with a log line when missing, unreadable, or recorded for another platform/API) and `Pump` calls `WarmUpProgressively(8)` per frame after the variants;
  `IsDone`/`Fraction` include it. Android needs a real file path (StreamingAssets is inside the APK), so Android recording/loading is **not done**.
- **Record**: tracing works only in **Development** Players (a release build records nothing: variants=0). Start such a Player with `-pn-gsc-record=<file>` (optional
  `-pn-gsc-record-seconds=<n>` quits and saves after n seconds); play the content you want covered (Hall, a run, Eve skills and Dash, enemies, lorica break, buffs, popups),
  quit. `PNGraphicsStateRecorder` writes the file on quit. Record with the real game camera (HDR/MSAA decide the render target formats and so the PSOs): a synthetic review camera only
  approximates it.
- **Install**: `Unity.exe -batchmode -quit -projectPath <project> -executeMethod PNGraphicsStateTools.Install -pn-gsc-inputs=a.graphicsstate;b.graphicsstate -pn-gsc-output=<file>`
  (default output is the StreamingAssets path above; all inputs must share platform and API). Re-record when shaders, the URP asset or the Unity version change.
- **Result so far**: first lorica break on DX12 stays at 17-22 ms with the recording installed (same as variants only): the remaining cost is creating the particle systems, and this
  machine's driver cache hides cold PSO compilation, so the benefit of the recording itself is not measured here.

## Checks
- Smoke `PNShaderWarmupTests`: definition validation, the twelve game registrations, `holding` in the machine, `PNTransitionTests` unchanged.
- Native `PNShaderWarmupEditorChecks` (14, includes the installed recording and the merge tool): shaders exist, spread over frames, once per generation, new generation restarts, scene scan of a real URP Lit material with keywords, no duplicate queue.
- Player (Windows, DX12, GTX 1650 Ti): `Review.exe ... -pn-lorica-visual-review -pn-lorica-shatter-stress [-pn-lorica-shatter-warm]` (first break worst frame 60-65 ms without, 17-20 ms with) and the Flow Player (`CameraFlowBuild`) whose log shows `Shader warm-up done: ... 11 frames, 0.267 s of work` and `Transition hide ... shader warm-up done=True`.
- Not measured: a cold driver cache (not cleared by decision), mod bundle shaders (only fixtures), Android/Vulkan, low-end or integrated GPUs on DX12.
