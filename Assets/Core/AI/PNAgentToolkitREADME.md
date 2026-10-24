# Agent toolkit — API và authoring

Toolkit là các object C# **tùy chọn**, không có Update riêng. Enemy C#/FSM hiện có
không tự tạo sensor, task, utility, goals hoặc nhóm. PNEnemySkill vẫn tick một selected
behavior và một main Action; Ability/cooldown giữ clock/lifecycle hiện có.
Toolkit gốc thuộc delivery Rules92. Follow-up Rules93 sửa receipt khi Action tick fault
và cho phép kế thừa contact Action để đăng ký compatible override; xem
Docs/EnemyToolkitFollowupReport.md để biết trạng thái kiểm chứng hiện hành.

## API thực tế

| Thành phần | API và giới hạn |
| --- | --- |
| Locomotion | `PNEnemyBehaviorContext.GoTo(goal, PNLocomotionOptions)`; overload float cũ giữ immediate và request.Options=null, không tạo options object |
| Options | Immutable StopDistance/SlowDistance/SpeedFraction; Acceleration/Braking/TurnLimit enabled flags; MaximumSpeed chỉ là trần dưới Stat speed |
| Geometry | PNAgentGeometry.CenterDistance/Distance/FootprintRadius; CenterXZ hoặc FootprintGapXZ; capsule Y upright |
| Facing | `Context.LookAt(point, Task/Skill)` trả exact lease; UpdatePoint/Dispose. PNEnemyMove.LookDirection là heading logic; không xoay sprite/camera |
| Actor handle | PNActorHandle.Capture(stat), TryResolve, SameActor, Position/FootprintRadius; pin load-context/world/membership, không chỉ EntityId |
| Adapter | IPNAgentContext; PNEnemyAgentContext(context, relationPredicate). Không buộc helpers dùng PNEnemyStat |
| Spatial query | agent.FindPoint(PNPointQuerySettings), query.Tick/Status/Point/Diagnostic/Dispose; score cao được ưu tiên, full path confirmation trước Success |
| Perception | PNAgentPerception(agent, settings).Tick(entityDelta), Records/NearestVisible; records có Unknown/Visible/Occluded/OutOfRange |
| Movement tasks | PNMovementTask + PNMovementTaskSettings; caller Tick, Cancel, Dispose; Running/Holding/Succeeded/Failed/Cancelled |
| Utility | PNUtilitySelector(candidates).Tick(delta) -> stableId/null; pure eligibility/score, hold/margin/cadence |
| Goals | PNDelegateGoal, PNMovementGoal, PNCompositeGoal(sequence/fallback), PNRetryGoal; 16 shared tick calls, depth8, static tree nodes256 |
| Replaceable helper | Own PNAgentOwnedSlot<T> một lần; Replace(factory) dọn object cũ trước tạo object mới, giữ reference để retry nếu Dispose lỗi |
| Cast receipt | Context.TryCast(id, direction, execution, out receipt, targets, pay, position); Started/Busy/Denied admission; Completed/Cancelled/Failed terminal receipt |
| Group | PNAgentGroupService(session), Join(agent, kind, encounter, eligible), membership Send/Drain/Reserve/FormationPoint/Dispose |
| Game composition | PNGameSession.AgentGroups lazy, LateUpdate tick active service bằng world.GameplayTime; không có service khi không dùng |
| Motion | IPNSkillMotionAction binding; PNMotionMotor + PNMove.AcquireMotionPhysics; outcome và deferred-cleanup contract |

Không dùng trực tiếp Load field của handle từ Game/mod; dùng SameActor để so lifetime.
Permission/Ready/world vẫn được kiểm tại lệnh thực thi; snapshot perception không cấp
quyền cast. Raw C# APIs là trusted author code, không phải sandbox chống mã độc.

## Mẫu giữ cự ly

```csharp
using UnityEngine;

public sealed class ExampleKeepRange : PNEnemyBehavior
{
    PNEnemyAgentContext agent;
    PNAgentPerception sensor;
    PNAgentOwnedSlot<PNMovementTask> slot;
    PNActorHandle selected;
    public override void Enter()
    {
        agent = new PNEnemyAgentContext(Context, actor => Context.Stat.Targets(actor));   // layers in my TargetLayers
        sensor = Context.Own(new PNAgentPerception(agent, new PNPerceptionSettings()));
        slot = Context.Own(new PNAgentOwnedSlot<PNMovementTask>());
    }
    public override void Tick(float delta)
    {
        sensor.Tick(delta);
        if (Context.Owner.IsExecutingEnemyAction) return;
        var seen = sensor.NearestVisible();
        if (seen == null) { slot.Value?.Cancel(); return; }
        if (selected == null || !selected.SameActor(seen.Actor) || slot.Value == null || slot.Value.IsTerminal)
        {
            selected = seen.Actor;
            slot.Replace(() => new PNMovementTask(agent,
                new PNMovementTaskSettings(PNMovementTaskKind.MaintainRange,
                    new PNLocomotionOptions(0, slowDistance: 1), minimumRange: 5, maximumRange: 7),
                agent.Self.Position, selected, seen));
        }
        else slot.Value.UpdateObservation(seen);
        slot.Value.Tick(delta);
    }
    public override void Exit(PNEnemyBehaviorExit reason)
    { slot?.Dispose(); sensor?.Dispose(); selected = null; }
}
```

Đây là ví dụ C# dùng API hiện có, không tự đăng ký behavior/đặt vào map.
Các wrapper Game sẵn có: PNToolkitMovementBehavior (10 task kinds),
PNToolkitSupportBehavior (ally injection + utility + heal/attack/follow),
PNToolkitPackBehavior (same-kind group, leader focus observations và attack reservation).
Chúng cần được thêm/chọn rõ ràng trên actor. Sentinel authored mẫu giữ melee/charge.

## Task settings

- Approach/Pursue có stop/resume; Loop=false là one-shot, Loop=true giữ band rồi đuổi lại.
- MaintainRange/Flee/CombatPosition hỗ trợ DistanceMetric CenterXZ/FootprintGapXZ.
  Ring query cộng radius thân đúng một lần; goal point trên ring dùng stop0.
- Pursue dùng observed velocity có prediction cap, không tự đọc vị trí thật khi khuất.
- Orbit dùng Radius/OrbitDegrees (segment <=45°) và OrbitAngularSpeed; tốc độ tuyến tính
  bị giới hạn bởi radius*angularSpeed và Stat, không tăng Stat để đạt angularSpeed.
- Patrol có danh sách waypoint; danh sách rỗng là wander trong radius bằng RNG riêng.
- Guard trở về home band; LeashRadius tùy chọn báo LeashExceeded, caller chọn chuyển Return.
- FollowOffset mặc định offset world XZ; OffsetInHeadingFrame=true cần caller truyền
  UpdateLeaderHeading, không suy hướng từ billboard hoặc velocity của một actor khác.
- Search dùng last-known snapshot; CombatPosition dùng ring + LOS, không là cover planner hoàn chỉnh.
- Mọi task có Timeout, RetryDelay, Retries hữu hạn. Holding chưa terminal; task timeout
  vẫn là policy đã cấu hình. Suspend/world pause/TimeStop không tiêu thụ task delta.

Chỉ một task có quyền điều khiển navigation tại một thời điểm. Muốn nhiều nguồn ra đề
nghị, chọn chúng trong behavior/utility trước rồi cấp một movement intent.

## Perception và query budget

Sensor khởi điểm range20, interval0.2 entity s, reaction0.15, memory3, 32 candidates/cycle,
8 LOS/cycle, memory64. Candidate cursor luân phiên; record chưa kiểm lại quá hạn là Unknown,
không khẳng định bị che. LastPosition/ObservedVelocity chỉ đổi khi thật sự thấy; velocity
dùng world.GameplayTime giữa các quan sát. Unknown/occluded không có ReadyToReact.
Không có audio sensor hoặc omniscient aggro tự động. Relation/FOV/LOS/anchors cần tuning.

Point query tối đa64 candidates (default16), 4 geometry candidates/Tick. Query độc lập
follower Seeker, một probe pending/actor load; dùng cùng4 path starts/frame/session.
FIFO giữa probe callers và giữ một lượt khi probe đợi, còn lượt cho follower. Backend
retirement đợi callback drain; cancel không giật pooled path/graph khỏi worker.
NoCandidate không tự nới tolerance, band hoặc đi thẳng xuyên tường.

## Nhóm, tín hiệu và ví dụ hỗ trợ

Game cấp kind + encounter key + eligibility; cùng CharacterId không tự join mọi actor
trong map. Membership scoped world/incarnation và load/selection; không đổi TargetLayers/layer,
OwnerId hay quyền player. Nếu policy quan hệ thay đổi, Game retire/rejoin membership.
Slots không đánh số lại khi member rời; leader dựa priority rồi stable actor ID.
Group record không giữ world activity lease để sống mãi khi không còn member.

Signals qua existing EveEventManager.Global, envelope typed/scoped, sequence/TTL;
callback chỉ enqueue. Inbox32, Drain mặc định8, drop oldest advisory khi tràn và tăng
DroppedSignals. Tín hiệu lifecycle thiết yếu không dùng queue có thể drop. Target/sender
retired hoặc TTL hết thì không execute sau thaw. Drain không tự cast; behavior xử lý.

Reservation là exact lease có TTL world time, không phải quyền thực thi. Group cap32
members,32 groups/world; mỗi group được tick tối đa một lần/frame. Không chứng minh
hiệu năng/capacity bởi các con số giới hạn này. Không có mạng hoặc persistence cho nhóm.

Support example nhận ally handle qua Configure trước selection. Pack example nhận
service/kind/encounter trước selection và chỉ group cùng layer/CharacterId theo Game policy.
Các ví dụ là actor thường; chưa có summon spawn/despawn/lệnh người chơi/lifetime.

## Actions và kết quả

Public aliases mới: base:trajectory_action, base:contact_motion_action, base:agent_heal_action.
Skill presets project_n:agent_curve/agent_hop/agent_orbit/agent_contact_motion/agent_dodge/
agent_backstep/agent_support. Tuning nằm trong PNAgentToolkitContent và registered payload.

Trajectory schemas riêng cho CurveXZ/Hop/OrbitSegment; không thêm field mới vào strict
PNMotionSettings cũ. Direction và optional position anchor snapshot lúc Start; orbit cần
anchor cách thân đúng radius. Ground13 required; hop cần nav profile phù hợp và không
ngầm cấp quyền vượt vực/tường. Curve/chord sweep có bounds; vượt budget trả failure.
Những lần cast đã commit có thể thất bại vì terrain/blocking; không hoàn lại cost/hit đã giao.

Contact sphere sweep/overlap theo displacement đã clip, hit-window và repeat interval.
Damage snapshot/crit/effects theo Skill contract (không đổi sang projectile per-hit crit).
Cancel trong callback hit chặn hit tiếp và giữ ownership đến scan safe point; lỗi cleanup
còn được scope giữ để retry. Physics lease phối hợp với PNMove freeze/thaw, không ghi đè
gravity/constraints từ một owner khác. Không có animation/VFX production mới.

Từ Rules93, lỗi owner/Action tick kết thúc receipt bằng Failed/ExecutionError trước
khi cancellation được gọi để dọn tài nguyên; không nhầm lỗi đó với Cancelled do gameplay.
PNAgentContactMotionAction có thể kế thừa để đăng ký selected override tương thích;
registry vẫn yêu cầu implementation kế thừa lớp gốc, không nới compatibility predicate.

Move và point probe phải tới đúng endpoint theo tolerance, không coi ABPath Complete
ở điểm gần nhất trên component khác là đường tới đích. Movement path starts có FIFO
giữa các request còn hợp lệ, cùng tổng budget4/frame với point probes hiện hành.
Physics query buffer thuộc owner tăng khi đầy, tự giữ capacity tới256 rồi conservative
ở giới hạn; không biến collider actor bị bỏ qua thành tường chỉ vì buffer32 đầy.
`PNAgentSpatial.HasSight(..., ref RaycastHit[] buffer, ...)` giữ lại buffer tăng cho caller;
overload truyền mảng cũ vẫn tương thích. Đây không phải capacity/RVO/flocking guarantee.

Enemy7 matrix hiện được chuyển đúng từ hàng Enemy của Physics2D sang Dynamics3D:
Enemy–UI bật, Enemy–Projectile bật, Enemy–ArenaWall tắt; Enemy–Enemy vẫn tắt, Ground vẫn bật.
Các cặp không liên quan Enemy giữ nguyên. Chỉ Sentinel mẫu/authoring body dùng Enemy7;
không recursive đổi layer renderer/hurtbox hoặc retrofit scene authored.

## Kiểm chứng và giới hạn

Đọc Docs/EnemyToolkitFollowupReport.md cho source Rules93 và evidence mới nhất;
Docs/EnemyBehaviorToolkitImplementationReport.md giữ evidence EB0–EB7 ban đầu.
Fixture opt-in PNEnemyMoveSkillPlayReview có stage toolkit1/toolkit2/toolkit345/toolkit6/
toolkit-all; Player nhận --pn-enemy-review=<output>, --pn-toolkit-stage=toolkit-all và
--pn-enemy-cache-path=<fixture caches>. Không chạy trên main authored scene.
Không RVO/flocking/fuzzy, bay navigation, Skill wire, arbitrary mid-cast save hoặc APK/device
acceptance. Agent helpers có thể được adapter summon tương lai dùng lại.

Follow-up fixtures nhận stage `followup` hoặc `crowd-32-0`/`crowd-64-1`/`crowd-64-2`
(population32/64, workload0..2), cache open/choke đã bake riêng. `--pn-followup-root`
trỏ tới root chứa Disconnected cache; `--pn-crowd-dynamic` là ca chức năng bổ sung có
target shift, Move-control hold2s để giữ hình học occlusion, rồi release/reacquire.
Mọi lease dọn ở finally. Không so số đo dynamic regression này với matrix F2.
F2 giữ20 raw Player runs và giới hạn đo ở report; không chứng minh mọi map/mod terrain,
solver CPU, probe completion timing hoặc FPS/capacity production. GC fixture còn cao.
SDK7 mới trong `.utmp/EnemyToolkitFollowup/20261023-014827/F4/SDK`; reference hashes
khớp assemblies main hiện tại, ba pack mới đã native load/execute/logical unload.
Không suy tương thích mọi DLL cũ. Rules92 worlds/peers không tương thích Rules93.
