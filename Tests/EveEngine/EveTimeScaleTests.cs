using System;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: Exposes Core ticks for clock integration checks without a Unity scene.
// VI: Mở bước cập nhật Core để kiểm tra đồng hồ không cần scene Unity.
public class EveClockTestMove : PNEnemyMove
{
    public void Tick() => FixedUpdate();
    public void Disable() => OnDisable();
}

// EN: Arithmetic, independent expiry, floor penetration, ownership and movement integration.
// VI: Công thức, hết hạn độc lập, xuyên ngưỡng, sở hữu và nối di chuyển.
public static class EveTimeScaleTests
{
    private static readonly EveId Source = EveId.Parse("test:time");
    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;

    public static void Run(EveSessionContext session)
    {
        var state = new EveTimeScaleState();
        long first = state.Add(Source, EveTimeModifierKind.Scale, -0.3f, 5d);
        long second = state.Add(Source, EveTimeModifierKind.Scale, -0.5f, 7d);
        Check(first != second && Near(state.Scale, 0.2f), "same-source time modifiers stack with unique IDs");
        state.Advance(5d);
        Check(!state.TryGet(first, out _) && state.TryGet(second, out var remaining) &&
            remaining.RemainingTime == 2d && Near(state.Scale, 0.5f), "five-second expiry leaves seven-second modifier active");
        state.Advance(2d);
        Check(state.Count == 0 && state.Scale == 1f, "independent final expiry restores normal scale");
        state.MinScale = 1f;
        state.Add(Source, EveTimeModifierKind.Scale, -0.8f, 20d);
        Check(state.Scale == 1f, "player floor protects from time changes without removing modifiers");
        state.Add(Source, EveTimeModifierKind.MinScaleReduction, 0.4f, 5d);
        state.Add(Source, EveTimeModifierKind.MinScaleReduction, 0.3f, 7d);
        Check(Near(state.Scale, 0.3f), "two independent floor reductions expose existing scale modifier");
        state.Advance(5d);
        Check(Near(state.Scale, 0.7f), "first penetration expiry restores part of the floor");
        state.Advance(2d);
        Check(state.Scale == 1f && state.Count == 1, "floor recovers without deleting masked time modifier");
        state.Clear(); state.MinScale = 0f;
        long excess = state.Add(Source, EveTimeModifierKind.Scale, -1.5f, 3d);
        state.Add(Source, EveTimeModifierKind.Scale, 0.2f, 7d);
        Check(state.Scale == 0f && state.RawScale < 0f, "negative raw scale remains stored while effective scale stops at zero");
        state.Advance(3d);
        Check(Near(state.Scale, 1.2f), "stopped clock still expires modifiers and allows scale above one");
        state.Advance(0d);
        Check(state.Count == 1, "game pause does not consume modifier duration");
        state.Clear();
        long fresh = state.Add(Source, EveTimeModifierKind.Scale, 1f, null);
        Check(fresh > excess && !state.Remove(excess) && state.Scale == 2f, "reset does not reuse modifier IDs");
        state.Advance(999d);
        Check(state.Count == 1, "owned duration does not expire by clock");
        Check(state.RemoveSource(Source) == 1 && state.Scale == 1f, "source removal clears only its contributions");
        Reject(() => state.Add(Source, EveTimeModifierKind.Scale, float.NaN, 1d), "time modifier rejects NaN");
        Reject(() => state.Add(Source, EveTimeModifierKind.MinScaleReduction, -1f, 1d), "floor reduction rejects negative values");
        Reject(() => state.Add(Source, EveTimeModifierKind.Scale, 1f, 0d), "zero lifetime is not interpreted as permanent");
        Reject(() => state.Advance(-1d), "clock rejects negative elapsed time");
        Reject(() => state.MinScale = 2f, "configured immunity floor stays within zero and one");

        var clock = new EveTimeScale();
        var owner = new MonoBehaviour();
        clock.AddOwnedModifier(Source, EveTimeModifierKind.Scale, -1f, owner);
        Check(clock.Scale == 0f, "owned modifier can stop entity");
        owner.enabled = false; clock.Advance(0f);
        Check(clock.Scale == 1f && clock.Modifiers.Count == 0, "disabled owner releases permanent modifier");
        clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -0.5f, 5f);
        float oldDelta = Time.deltaTime;
        Time.deltaTime = 0.04f;
        Check(Near(clock.DeltaTime, 0.02f), "entity delta multiplies already-scaled game delta once");
        Time.deltaTime = oldDelta;
        clock.Unload();
        Check(clock.Scale == 1f && clock.Modifiers.Count == 0, "unload clears all clock modifiers");
        clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -1f, 1f);
        clock.ScaleChanged += value => { if (value == 1f) clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -1f, 1f); };
        clock.ClearModifiers();
        Check(clock.Scale == 1f && clock.Modifiers.Count == 0, "reset listeners cannot repopulate pooled clock modifiers");
        RunMovement(session);
    }

    private static void RunMovement(EveSessionContext session)
    {
        var loader = new EveTestLoader();
        var body = new Rigidbody { constraints = RigidbodyConstraints.FreezeRotation };
        var move = new EveClockTestMove();
        var clock = new EveTimeScale();
        MonoBehaviour[] components = { loader, new EveEntityIdentity(), body, new Collider(),
            EveMoveTests.CreateMoveStat(session), clock, move };
        foreach (var component in components) component.Components = components;
        loader.Load(session.Registry);
        var ground = new Collider();
        Physics.GroundHits = () => new[] { new RaycastHit { collider = ground, normal = new Vector3(0f, 1f, 0f) } };
        move.Tick();
        long jumpRate = clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -0.5f, 1f);
        float eventVelocity = 0f;
        move.JumpStarted += () => eventVelocity = body.linearVelocity.y;
        Check(move.TryJump() && Near(body.linearVelocity.y, 3f) && Near(eventVelocity, 3f),
            "jump and its event use scaled takeoff velocity");
        Physics.GroundHits = () => Array.Empty<RaycastHit>();
        clock.RemoveModifier(jumpRate); move.Tick();
        long slow = clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -0.5f, 5f);
        move.SetMoveDirection(Vector2.right);
        body.linearVelocity = new Vector3(0f, 4f, 0f);
        move.Tick();
        Check(Near(body.linearVelocity.x, 2.5f) && Near(body.linearVelocity.y, 2f), "Core scales planar and vertical movement together");
        Check(Near(body.addedAcceleration.y + Physics.gravity.y, Physics.gravity.y * 0.25f), "Core compensates gravity by scale squared");
        int dash = move.MoveTo(10f, 0.02f);
        int completed = 0;
        move.CommandEnded += result => { if (result.CommandId == dash && result.Reason == EveMoveEndReason.Completed) completed++; };
        move.Tick(); move.Tick();
        Check(completed == 0 && Near(body.linearVelocity.x, 5f), "half-scale dash keeps full distance over twice game time");
        move.Tick();
        Check(completed == 1, "scaled command timer completes independently of speed source");
        long stop = clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -1f, 1f);
        move.Tick();
        Check(body.constraints == RigidbodyConstraints.FreezeAll && !body.useGravity && body.linearVelocity.y == 0f,
            "zero clock freezes physical body including gravity");
        Check(!move.TryJump(), "time-stopped entity cannot start a jump");
        move.TransformToXZ(new Vector2(8f, 9f), 0f);
        Check(body.position.x == 0f, "zero-delay teleport waits while entity is stopped");
        clock.RemoveModifier(stop);
        move.Tick();
        Check(Near(body.linearVelocity.y, 2f) && body.useGravity && body.constraints == RigidbodyConstraints.FreezeRotation,
            "resume restores vertical motion and original physics settings");
        Check(body.position.x == 8f && body.position.z == 9f, "pending teleport resumes after clock restarts");
        clock.RemoveModifier(slow);
        move.Tick();
        Check(Near(body.linearVelocity.y, 4f), "return to scale one restores logical vertical speed");
        long half = clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -0.5f, 1f);
        move.Tick(); move.Disable();
        Check(Near(body.linearVelocity.y, 4f), "disabling movement releases scaled vertical velocity");
        clock.RemoveModifier(half);
        clock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -1f, 1f);
        move.Tick(); loader.Unload();
        Check(body.constraints == RigidbodyConstraints.FreezeRotation && body.useGravity && body.linearVelocity.y == 0f && clock.Modifiers.Count == 0,
            "unload while stopped restores physics and clears clocks without launching old motion");

        var player = new EveTestPlayerMove();
        var playerLoader = new EveTestLoader();
        var playerClock = new EveTimeScale();
        components = new MonoBehaviour[] { playerLoader, new EveEntityIdentity(), new Rigidbody(),
            new Collider(), EveMoveTests.CreateMoveStat(session), playerClock, player };
        foreach (var component in components) component.Components = components;
        player.InitializeInput(); playerLoader.Load(session.Registry);
        playerClock.AddTimedModifier(Source, EveTimeModifierKind.Scale, -0.8f, 5f);
        Check(playerClock.MinScale == 1f && player.MovementTimeScale == 1f, "Core player configures min scale one");
        playerClock.AddTimedModifier(Source, EveTimeModifierKind.MinScaleReduction, 0.7f, 5f);
        Check(Near(player.MovementTimeScale, 0.3f), "boss floor reduction reaches player through the Core adapter");
        playerLoader.Unload();
    }
}
