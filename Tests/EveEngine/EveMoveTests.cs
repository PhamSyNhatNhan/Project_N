using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: Exposes the fixed physics tick to the smoke harness.
// VI: Cho bộ smoke test gọi bước vật lý cố định.
public class EveTestMove : EveMove2D
{
    public void Tick() => FixedUpdate();
    public void Disable() => OnDisable();
}

// EN: Exposes the 3D variant's fixed physics tick.
// VI: Cho kiểm thử gọi bước vật lý cố định của bản 3D.
public class EveTestMove3D : EveMove3D
{
    public void Tick() => FixedUpdate();
}

public class EveTestPlayerMove : PNPlayerMove
{
    public void Tick() => FixedUpdate();
    public void InitializeInput() => Awake();
    public void ReadInput() => Update();
}

public class EveTestMoveStat : EveStat
{
    public override string CharacterDefinitionId => "project_n:eve";
}

// EN: Checks that movement reads registered stats without Skill or game input components.
// VI: Kiểm tra Move đọc chỉ số đã đăng ký mà không cần Skill hoặc input riêng của game.
public static class EveMoveTests
{
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.0001f;

    public static EveStat CreateMoveStat(EveSessionContext session, bool includeJump = true)
    {
        var set = new EveStatSet(session.Registry.Get<EveStatDefinition>());
        set.AddBase(new EveBaseStat(EveBuiltInContentModule.MoveSpeed, 5f));
        if (includeJump) set.AddBase(new EveBaseStat(EveBuiltInContentModule.JumpSpeed, 6f));
        var stat = new EveTestMoveStat();
        stat.Initialize(set);
        return stat;
    }

    public static void Run(EveSessionContext session)
    {
        var missingStatLoader = new EveTestLoader();
        MonoBehaviour[] missingStat = { missingStatLoader, new EveEntityIdentity(),
            new Rigidbody2D(), new EveTestMove() };
        foreach (var component in missingStat) component.Components = missingStat;
        Reject(() => missingStatLoader.Load(session.Registry), "Move requires a Stat load step");

        var missingJumpLoader = new EveTestLoader();
        MonoBehaviour[] missingJump = { missingJumpLoader, new EveEntityIdentity(), new Rigidbody(),
            CreateMoveStat(session, false), new EveTestMove3D() };
        foreach (var component in missingJump) component.Components = missingJump;
        Reject(() => missingJumpLoader.Load(session.Registry), "3D Move requires a jump-speed base value");

        var loader = new EveTestLoader();
        var identity = new EveEntityIdentity();
        var body = new Rigidbody2D();
        var move = new EveTestMove();
        var stat = CreateMoveStat(session, false);
        MonoBehaviour[] components = { loader, identity, body, stat, move };
        foreach (var component in components) component.Components = components;
        loader.Load(session.Registry);
        Check(move.IsReady && !stat.Set.TryGetBase(EveBuiltInContentModule.JumpSpeed, out _),
            "2D Move loads with movement speed and no jump value");

        move.SetMoveDirection(new Vector2(1f, 0f));
        move.Tick();
        Check(Near(body.linearVelocity.x, 5f) && move.IsMoving && Near(move.MoveSnap.x, 1f),
            "ordinary movement reads stat units per second and records direction");
        EveId speedBuff = EveId.Parse("test:move_speed_buff");
        stat.SetSource(speedBuff, new[] { new EveStatModifier(EveBuiltInContentModule.MoveSpeed, 0.2f, 1) });
        move.Tick();
        Check(Near(body.linearVelocity.x, 6f), "movement reads a stat buff on the next physics step");
        stat.RemoveSource(speedBuff);
        move.Tick();
        Check(Near(body.linearVelocity.x, 5f), "movement restores speed after the buff is removed");

        var results = new List<EveMoveResult>();
        move.CommandEnded += results.Add;
        int dash = move.MoveTo(10f, 0.04f);
        move.Tick();
        Check(Near(body.linearVelocity.x, 10f), "dash overrides ordinary direction");
        move.Tick();
        move.Tick();
        Check(results.Count == 1 && results[0].CommandId == dash &&
              results[0].Reason == EveMoveEndReason.Completed, "dash completes after duration");

        int old = move.MoveTo(10f, 1f);
        int target = move.MoveToXY(new Vector2(0.2f, 0f), 10f, 1f);
        Check(results.Count == 2 && results[1].CommandId == old &&
              results[1].Reason == EveMoveEndReason.Cancelled && target != old,
            "new command cancels and identifies old command");
        move.Tick();
        body.position = body.position + body.linearVelocity * Time.fixedDeltaTime;
        move.Tick();
        Check(Near(body.position.x, 0.2f) && results.Count == 3 &&
              results[2].Reason == EveMoveEndReason.Completed, "target command completes on arrival");

        move.MoveToXY(new Vector2(100f, 0f), 1f, 0.02f);
        move.Tick();
        move.Tick();
        Check(results[results.Count - 1].Reason == EveMoveEndReason.TimedOut,
            "unreachable target reports timeout");

        int interrupted = move.MoveTo(3f, 1f);
        int nested = 0;
        Action<EveMoveResult> onCancelled = null;
        onCancelled = result =>
        {
            if (result.CommandId != interrupted) return;
            move.CommandEnded -= onCancelled;
            nested = move.MoveTo(4f, 1f);
        };
        move.CommandEnded += onCancelled;
        int displaced = move.MoveTo(5f, 1f);
        Check(nested != 0 && results[results.Count - 1].CommandId == displaced &&
              results[results.Count - 1].Reason == EveMoveEndReason.Cancelled &&
              move.Mode == EveMoveMode.Direction, "callback replacing a command still reports its cancellation");
        move.CancelCommand();

        body.position = Vector2.zero;
        move.MoveToXYLimitDistance(new Vector2(1f, 0f), 10f, 1f, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            move.Tick();
            body.position = body.position + body.linearVelocity * Time.fixedDeltaTime;
        }
        Check(Near(body.position.x, 0.5f) && results[results.Count - 1].Reason == EveMoveEndReason.Completed,
            "target with stop distance stops at boundary");

        int delayed = move.TransformToXY(new Vector2(2f, 3f), 0.04f);
        move.Tick();
        Check(Near(body.position.x, 0.5f), "delayed teleport waits");
        move.CancelCommand();
        Check(results[results.Count - 1].CommandId == delayed &&
              results[results.Count - 1].Reason == EveMoveEndReason.Cancelled, "delayed teleport can be cancelled");
        move.TransformToXY(new Vector2(2f, 3f), 0f);
        Check(Near(body.position.x, 2f) && Near(body.position.y, 3f), "zero delay teleports immediately");
        Action relocated=null;
        relocated=()=>{move.PoseRelocated-=relocated;move.MoveTo(4f,1f);};
        move.PoseRelocated+=relocated;move.TransformToXY(new Vector2(3f,4f),0f);
        Check(move.Mode==EveMoveMode.Direction && Near(body.position.x,3f),"teleport publication re-entry cannot complete a replacement command");
        move.CancelCommand();

        move.FaceDirection(new Vector2(-1f, 0f));
        Check(move.FacingDirection == -1, "game logic can request facing independently of input");

        move.MoveTo(10f, 1f);
        move.CanMove = false;
        Check(move.Mode == EveMoveMode.None && Near(body.linearVelocity.x, 0f) &&
              results[results.Count - 1].Reason == EveMoveEndReason.Cancelled, "movement lock cancels and stops");
        move.CanMove = true;
        move.MoveTo(10f, 1f);
        move.Tick();
        move.Disable();
        Check(move.Mode == EveMoveMode.None && Near(body.linearVelocity.x, 0f),
            "disabling a pooled entity stops its movement");
        loader.Unload();
        Check(!move.IsReady && Near(body.linearVelocity.x, 0f), "unload stops Move");

        Run3D(session);
        Run3DJump(session);
        RunCorePlayerMove(session);
        var mixedLoader = new EveTestLoader();
        var mixedIdentity = new EveEntityIdentity();
        var mixed2D = new EveTestMove();
        var mixed3D = new EveTestMove3D();
        MonoBehaviour[] mixed = { mixedLoader, mixedIdentity, new Rigidbody2D(), new Rigidbody(),
            CreateMoveStat(session), mixed2D, mixed3D };
        foreach (var component in mixed) component.Components = mixed;
        Reject(() => mixedLoader.Load(session.Registry), "one entity cannot load both Move variants");
        Check(!mixed2D.IsReady && !mixed3D.IsReady, "mixed variants roll back without a ready Move");
    }

    private static void Run3D(EveSessionContext session)
    {
        var loader = new EveTestLoader();
        var identity = new EveEntityIdentity();
        var body = new Rigidbody { position = new Vector3(0f, 2f, 0f) };
        var move = new EveTestMove3D();
        MonoBehaviour[] components = { loader, identity, body, CreateMoveStat(session), move };
        foreach (var component in components) component.Components = components;
        loader.Load(session.Registry);
        Check(move.IsReady && move.Body == body, "3D Move loads with Rigidbody and Stat");

        body.linearVelocity = new Vector3(0f, 3f, 0f);
        move.SetMoveDirection(new Vector2(1f, 0f));
        move.Tick();
        Check(Near(body.linearVelocity.x, 5f) && Near(body.linearVelocity.y, 3f) &&
              Near(body.linearVelocity.z, 0f), "3D ordinary movement uses XZ and preserves vertical velocity");

        var results = new List<EveMoveResult>();
        move.CommandEnded += results.Add;
        int dash = move.MoveTo(10f, 0.02f);
        move.Tick();
        Check(Near(body.linearVelocity.x, 10f) && Near(body.linearVelocity.y, 3f),
            "3D dash leaves vertical motion intact");
        move.Tick();
        Check(results.Count == 1 && results[0].CommandId == dash &&
              results[0].Reason == EveMoveEndReason.Completed, "3D dash reports completion");

        move.TransformToXZ(new Vector2(4f, 6f), 0f);
        Check(Near(body.position.x, 4f) && Near(body.position.y, 2f) && Near(body.position.z, 6f),
            "3D planar teleport preserves height");

        move.MoveToXZ(new Vector2(4f, 7f), 10f, 1f);
        move.Tick();
        Check(Near(body.linearVelocity.z, 10f) && Near(body.linearVelocity.y, 3f),
            "3D target movement uses world Z");
        move.StopMovement();
        Check(Near(body.linearVelocity.x, 0f) && Near(body.linearVelocity.z, 0f) &&
              Near(body.linearVelocity.y, 3f), "3D stop cancels planar command but preserves jump or fall");

        loader.Unload();
        Check(!move.IsReady && Near(body.linearVelocity.y, 0f), "3D unload stops all body motion");
    }

    private static void Run3DJump(EveSessionContext session)
    {
        var loader = new EveTestLoader();
        var identity = new EveEntityIdentity();
        var body = new Rigidbody();
        var collider = new Collider
        {
            attachedRigidbody = body,
            bounds = new Bounds { center = new Vector3(0f, 0.5f, 0f), min = Vector3.zero }
        };
        var ground = new Collider();
        var move = new EveTestMove3D();
        var stat = CreateMoveStat(session);
        MonoBehaviour[] components = { loader, identity, body, collider, stat, move };
        foreach (var component in components) component.Components = components;
        RaycastHit[] hits = { new RaycastHit { collider = collider, normal = new Vector3(0f, 1f, 0f) },
            new RaycastHit { collider = ground, normal = new Vector3(0f, 1f, 0f) } };
        Physics.GroundHits = () => hits;
        try
        {
            loader.Load(session.Registry);
            Check(move.IsGrounded && move.VerticalMotion == EveVerticalMotion.Grounded,
                "3D ground probe ignores own collider and initializes grounded without landing event");
            int jumps = 0, landings = 0;
            move.JumpStarted += () => jumps++;
            move.Landed += () => landings++;
            EveId jumpLock = EveId.Parse("test:jump_lock");
            stat.SetSource(jumpLock, new[] { new EveStatModifier(EveBuiltInContentModule.JumpSpeed, -1f, 1) });
            Check(!move.TryJump() && jumps == 0, "zero jump speed prevents takeoff without throwing");
            stat.RemoveSource(jumpLock);
            Check(move.TryJump() && Near(body.linearVelocity.y, 6f) && jumps == 1 &&
                  !move.IsGrounded && move.VerticalMotion == EveVerticalMotion.Rising,
                "3D jump sets upward velocity and signals once");
            stat.SetSource(EveId.Parse("test:jump_buff"),
                new[] { new EveStatModifier(EveBuiltInContentModule.JumpSpeed, 0.5f, 1) });
            Check(Near(move.CurrentJumpSpeed, 9f), "3D jump speed reads current stat modifiers");
            Check(!move.TryJump(), "3D jump cannot repeat in air");
            move.CanMove = false;
            move.Tick();
            Check(Near(body.linearVelocity.y, 6f) && !move.IsGrounded && landings == 0,
                "horizontal lock preserves ascent and nearby ground does not cause instant landing");
            hits = Array.Empty<RaycastHit>();
            move.Tick();
            body.linearVelocity = new Vector3(0f, -2f, 0f);
            move.Tick();
            Check(move.VerticalMotion == EveVerticalMotion.Falling && !move.IsGrounded,
                "departing ground and descending enters falling state");
            hits = new[] { new RaycastHit { collider = ground, normal = new Vector3(0f, 0.5f, 0f) } };
            move.Tick();
            Check(!move.IsGrounded && landings == 0, "steep wall does not count as ground");
            hits = new[] { new RaycastHit { collider = ground, normal = new Vector3(0f, 1f, 0f) } };
            body.linearVelocity = Vector3.zero;
            move.Tick();
            move.Tick();
            Check(move.IsGrounded && landings == 1, "landing fires once on valid ground");
            move.CanJump = false;
            Check(!move.TryJump(), "game can lock jumping independently of horizontal movement");
            loader.Unload();
            Check(!move.IsGrounded && !move.IsReady && Near(body.linearVelocity.y, 0f),
                "unload resets jump state and vertical motion");
        }
        finally { Physics.GroundHits = () => Array.Empty<RaycastHit>(); }
    }

    private static void RunCorePlayerMove(EveSessionContext session)
    {
        var loader = new EveTestLoader();
        var move = new EveTestPlayerMove();
        MonoBehaviour[] components = { loader, new EveEntityIdentity(), new Rigidbody(),
            new Collider(), CreateMoveStat(session), move };
        foreach (var component in components) component.Components = components;
        move.InitializeInput();
        loader.Load(session.Registry);
        move.PC.Controller.Move.Value = new Vector2(1f, 0f);
        move.ReadInput();
        move.Tick();
        Check(move.IsReady && Near(move.Body.linearVelocity.x, 5f),
            "Core PlayerMove reads PlayerControls direction into 3D XZ movement");
        move.CanInput = false;
        move.ReadInput();
        move.Tick();
        Check(Near(move.Body.linearVelocity.x, 0f), "Core PlayerMove input lock stops directional movement");
        loader.Unload();
    }
}
