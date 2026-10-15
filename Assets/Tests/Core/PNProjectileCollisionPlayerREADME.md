# Standalone collision benchmark / Thử collision trong Player

EN: This is an opt-in synthetic experiment, not production Projectile. Current
RulesVersion65 / Mod API4 are unchanged. Read AGENTS.md and latest HANDOFF first.
VI: Đây là thử nghiệm tường minh, không phải Projectile sản phẩm. RulesVersion65 /
Mod API4 giữ nguyên. Đọc AGENTS.md và HANDOFF mới nhất trước khi chạy.

## Fair comparison / Phạm vi so sánh

- Actual trigger callbacks (`OnTriggerEnter` + `OnTriggerStay`) on a sphere trigger
  and kinematic Rigidbody. Bullet layer31 interacts only with target30/wall29;
  bullet/bullet and irrelevant layer pairs are disabled in this standalone process.
- Overlap: no projectile Collider/Rigidbody, native nonalloc sphere queries.
- Sweep: no projectile Collider/Rigidbody, sphere casts with initial overlap and
  stationary fallback. Their query cost is included, not omitted from the result.
- Each method is sampled at1/2/4 steps per1/60-second tick. Do not equate cheaper
  one-step collision with equally accurate four-step collision.
- Shared per-object16-piece homing/sine motion plan, radius, target motion, lifetime,
  pooling and stop-clock rule. Collision does not retire on hit so differences in
  detection cannot change subsequent trajectories or active population.
- Counts300/500/1000; player90% homing +10% sine and boss sine. Some projectiles are
  deliberately fast/small, targets move and have two sphere subcolliders. A thin
  box wall exercises environment hits. No production HP/combat/AI/network costs.
- CPU-only and identical two-quad glow VFX, balanced/reversed repeats. Explicit
  offscreen render requests run once per tick; a pixel check proves visible glow
  before measured VFX cases and validates every captured frame.30 warmup +
  120 measured frames per case;216 cases total. Model time, placement, physics and
  query times are separate. Frame time is whole empty-player synthetic throughput.

VI: Trigger là callback Unity thật, không phải Collider rồi vẫn gọi overlap như
bài trước. Mỗi backend dùng cùng đường bay/hitbox; không xóa đạn theo hit để tránh
việc bỏ sót làm thay đổi tải. Chi phí tính quỹ đạo chung được đo riêng và có trong
CPU Tick; không lấy tổng Tick làm riêng chi phí collision.

## Reference / Quỹ đạo đối chiếu

Reference follows the16 motion pieces with relative projectile/target movement.
It tests continuous line/sphere distance for target subcolliders and planar
segment/rectangle distance for the wall, including corners/endpoints. Self-checks
run in Player before measurements. Reference computation is outside timing.

Coverage is per target/projectile-life, aggregated across warmup and timed ticks.
Missed and false-positive contacts are reported against this reference. Hit timing,
ordering, target HP, immunity and rehit rules are not measured. The16-piece model
is a bounded approximation, not analytic continuous accuracy for arbitrary curves.
Native sweep sees target endpoint poses and chords; moving-target crossings and
curve chord errors can remain even at higher sample rates.

VI: Tỷ lệ bỏ sót tính trên cặp target/lần sống của đạn, không phải tỷ lệ frame hay
damage. Reference không tính trong timer. Không kết luận một thuật toán đúng tuyệt
đối khi số bỏ sót bằng0 ở workload cụ thể.

## Build and run / Build và chạy

Use idle Edit Mode and a clean authored scene. Invoke
`PNProjectileCollisionPlayerBuild.Queue()` via Unity MCP. It creates/saves only a
new unique empty fixture scene and a shader-dependency material, builds only that
scene with BuildOptions.None for StandaloneWindows64, then removes its owned
temporary assets. EditorBuildSettings.scenes is never assigned, original scene is
not saved/reloaded, and Game/Simulator/Play Maximized are not modified. Backend on
the tested project is Mono2x. Require successful BuildResult before launching.

Output `.utmp/ProjectileCollisionPlayer/Build/CollisionBenchmark.exe` accepts:

```text
-screen-fullscreen 0 -screen-width 1280 -screen-height 720
-pn-projectile-collision-benchmark -pn-output <absolute results directory>
-logFile <absolute Player log path>
```

Run with a hidden window, without `-batchmode` or `-nographics`. A graphics device
alone is not rendering evidence: initial automatic/batch hidden-camera trials
produced black captures and are excluded from rendered results. Explicit render
submission/pixel validation is required. VFX CPU submission has a separate timer.
The review-directory `RunPlayer.ps1` starts a nonblocking background process and
records PID/arguments. `Result.txt` must say PASS after all216 cases; partial CSVs
alone are not success. Player quits after cleanup. Existing results should be
archived, not overwritten accidentally. The runtime entry point is inert without
the explicit benchmark flag, including ordinary game/editor startup.

VI: Build thường, không Development Build/profiler. Cửa sổ chạy ẩn; không dùng
nographics vì vẫn đo render. Không đổi cấu hình project để chạy thử. Nếu dừng sớm
thì CSV chưa đầy đủ không được coi là PASS.

## Artifacts / Bằng chứng

- BuildResult / EditorBuildAudit: success, authored scene and build-list preservation.
- Environment.json: confirms editorFalse/developmentFalse, CPU/GPU and graphics API.
- Summary.csv and individual case CSVs: staged CPU p50/p95/p99, frame and step GC.
- OracleChecks, expected/detected/missed/false_positive, query/callback/reuse counts.
- Eighteen1000-projectile VFX captures, Progress/Result/Cleanup and Player.log.
- Review analyzer pools both repeats and verifies actual callbacks/reference parity.

Empty-player FPS is not full-game/mobile FPS. The prior grid-index comparison and
this collision-algorithm comparison answer different questions. Projectile-volume
query/grid maintenance, production stats/Buffs/effects and homing target acquisition
remain separate costs. Do not claim grid universally wins or that trigger is cheap
enough without considering the measured accuracy requirement and target platform.
