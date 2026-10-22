# Enemy Move and Skill

Rules92 adds the optional [agent toolkit](../../AI/PNAgentToolkitREADME.md):
locomotion options, exact facing leases, spatial/perception/memory, movement tasks,
utility/goals, scoped groups/signals and registered trajectory/contact Actions.
The legacy float GoTo overload remains immediate with no optional sensor/group runtime.
See the toolkit API guide for ownership, tick budgets and actual authoring examples.

Enemy locomotion is grounded XZ movement through PNMove/EveMove3D and its Rigidbody,
Control, TimeScale and world gates. PNEnemyMove never writes a pose directly. Ordinary
TryJump is disabled; temporary special movement uses a registered Action.

## Ownership

- PNEnemySkill owns one selected prefab PNEnemyBehavior and one main invocation.
  The behavior has no autonomous Update/FixedUpdate/coroutine. The owner supplies
  entity-scaled Tick; the Ability manager and clock retain their own single tick paths.
- Use PNEnemyBehavior for direct C# logic, PNEnemyStateMachineBehavior for optional
  local states (AddState/ChangeState). Transitions are deferred and bounded to16 per drain.
- Configure unique local BehaviorId values before loading, and choose one with
  ConfigureInitialBehavior. The owner must be the component's nearest PNEnemySkill,
  including while the prefab is inactive. Foreign/duplicate/missing IDs fail preparation.
- An empty PNEnemySkill preserves the original passive extension point/dependencies.
  A behavior-enabled enemy requires PNStat, PNAbilityManager, PNEnemyMove and Control.
  Its authoritative world must have prepared navigation before publication.
- BuiltInSkillIds is the union of behavior SkillIds. Content is registered in the game
  content module, not during behavior entry. Existing player PNSkillBehavior/bindings
  are independent; this framework does not require input slots for AI.

## Behavior lifecycle

Attach is internal. Override Enter, Tick(delta), Exit(reason). Enter may acquire
selection resources through Context.Own; casting is admitted from Tick or an active
context after Enter. Exit must clean up any extra behavior-owned subscriptions/jobs.
Own each IDisposable through Context.Own so the owner can reclaim it even if Exit fails.
Failed resource disposal remains in the owner's retryable cleanup scopes; successful
disposals are removed and never replayed. Unload retries the retained scopes.

Context is valid only for its selected owner/load/world. Retired contexts cannot issue
cast/move/switch/Own calls. Context.Abilities accesses entity-owned state, not mutable
shared assets. Re-selecting a component makes a fresh context and state-local runtime;
cooldown/charges in Ability are preserved, including while the component is unselected.

```csharp
// Inside a selected behavior; names below are implemented APIs.
walk = Context.GoTo(goal, stopDistance);
Context.UpdateGoal(walk, latestGoal);
Context.TryCast(skillId, direction, PNEnemyActionExecution.AuxiliaryTimeline,
    targets, () => cooldown.TryConsume());
Context.Switch("phase-two", PNEnemySwitchPolicy.FinishCurrent);
```

TryCast uses existing admission, BeginActivation, selected factory, Start/Perform,
CommitActivation, Exit/Cancel/Dispose and EndActivation. Cost callback runs once after
Start succeeds and before Perform; false cancels without Perform. Already-spent costs
and delivered hits are not refunded if a later callback fails or interrupts execution.
Target enumeration is captured by the Action context before commit.

Execution mode is explicit: Instant for an immediate Action, AuxiliaryTimeline for an
Action declaring HasAuxiliaryTimeline and implementing AdvanceAuxiliary. A mismatch
fails before Start. Do not infer that every Action without an auxiliary timeline is
instant; extend the owner execution contract before using another kind of main driver.
No reflection or direct construction of an Action implementation is used.
PNMotionAction additionally binds its cancellation owner and keeps RegisterMotion's
exclusive ownership. The selected Action's registered data owns distance/duration/
damage. ExecutionSpeed scales only declared attack-speed-sensitive Action timelines.

## Switching and interruption

Switch(id, policy) returns PNEnemySwitchReceipt (Pending/Applied/Cancelled/Failed/Busy).
Policy is mandatory. FinishCurrent stops new decisions/navigation and waits for the
committed main Action; uncommitted behavior windup does not block switching.
CancelCurrent cancels immediately at a safe point. A second pending request is Busy;
selecting the current set is a no-op. Enter/Exit failure faults the owner, reclaims its
registered resources and reports the failure; it does not silently retry gameplay.

Death/disable/unload/world transfer retire contexts, casts and navigation even at
time scale0 or after gameplay invalidation. Control denies new admission; it does not
implicitly interrupt a running Action. TryInterrupt(reason) explicitly cancels it.
TimeStop suspends timelines. Transfer cancellation goes through CancelWorldExecutions;
after destination readiness the owner enters its configured initial behavior. Keep
phase policy in Game logic when the destination should select another set.

## Navigation requests

GoTo(goal, stopDistance, ownerIsCurrent) returns PNEnemyNavigationRequest. A replacement
cancels the old handle once with Replaced. UpdateGoal/CancelNavigation reject foreign,
retired or terminal handles; a late path callback cannot revive them.
Status: PendingPath, Following, Suspended; terminal Arrived, NoPath, Stuck, Cancelled.
Completed is delivered at most once, with Diagnostic/EndReason where applicable.

Movement speed comes from PNMove, with time scale applied once. Each enemy has at
most one worker request outstanding; goal changes coalesce behind it. Session path
starts are budgeted (default4/frame). Full paths and endpoints within profile tolerance
are required; partial paths do not report success. The original goal remains authoritative.
Use stop/resume hysteresis in behavior logic. Reaching stopping range is not permission
to attack: the behavior must validate target/world/range/LOS separately.

The follower updates direction before its inherited FixedUpdate, follows corners through
real world physics, clips the final step and detects lack of body progress with bounded
repath retries. It suspends while held, time-stopped, denied movement, speed0, registered
special motion or an EveMove command owns movement. SuspendNavigation returns an exact
lease; releasing one cannot release another or a later load's lease. Resume/relocation
invalidates the old route and repaths from the real body. Navigation does not change
BaseCanMove, release other Control leases or advance the entity clock.

## Concrete example

Game's Sentinel prefab is a new example, not a replacement for existing Dummy content.
PNSentinelSkill selects melee then charge below50% health. PNEnemyMeleeBehavior is an
optional FSM example (Acquire/Chase/Windup/Execute/Recover/Backoff), with bounded target
queries from the same world's directory. PNEnemyChargeBehavior reuses its decisions
and selects the registered motion Action. Charge itself deals no contact damage.
The example strike uses the existing instant damage Action; placeholder visuals are
not a finished enemy animation/VFX set.

Factories/enrollment must select character project_n:sentinel and bind world/slot before
loading the prefab. Merely placing a prefab outside a navigated world is not enrollment.
The example does not enable enemy AI in any authored scene or change existing mode
transfer/save policies. Arbitrary FSM/mid-cast checkpoint state is not serialized.
Restore reconstructs behavior through normal load; use explicit state providers if a
mode later requires exact mid-cast recovery.

Native/Player review: PNEnemyMoveSkillPlayReview.StartReview(output, stage). Provide
--pn-enemy-cache-path=<fixture cache directory>. Player opt-in uses
--pn-enemy-review=<output directory>. This exercises authority gates only, not Skill
networking or remote replication. RVO/avoidance remains a separate decision; a crowd
without faults is not proof of no congestion or a performance capacity guarantee.
