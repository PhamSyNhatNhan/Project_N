using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class EveSlashProbeSkill : EveCharacterSkill
{
    public void InitializeInput() { Awake(); OnEnable(); }
    public void Tick() => Update();
    public void DisableInput() { enabled = false; OnDisable(); }
    public void EnableInput() { enabled = true; OnEnable(); }
    public void DestroyInput() => OnDestroy();
    public Vector3 ResolveAim() => ResolveAttackDirection();
}

public static class PNFirstSlashTests
{
    public static void Run()
    {
        Reject(() => new PNSlashVfxSettings(JObject.Parse("{\"duration\":0}")), "slash rejects zero lifetime");
        Reject(() => new PNSlashVfxSettings(JObject.Parse("{\"radius\":\"large\"}")), "slash rejects invalid radius");
        Check(PNSlashHitSettings.Parse(new JObject()) == null, "old slash payload remains visual-only");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100}}")), "slash rejects value without hitbox");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"hitbox\":{}}")), "slash rejects hitbox without value");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":-1},\"hitbox\":{}}")), "slash rejects negative damage");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100},\"hitbox\":{\"windowStart\":0.2,\"windowEnd\":0.1}}")), "slash rejects reversed hit window");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100},\"hitbox\":{\"windowEnd\":1}}")), "slash rejects hit window beyond VFX");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100},\"hitbox\":{\"thickness\":0}}")), "slash rejects zero thickness");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100},\"hitbox\":{\"followCaster\":1}}")), "slash rejects nonboolean movement policy");
        Reject(() => PNSlashHitSettings.Parse(JObject.Parse("{\"value\":{\"amount\":100},\"hitbox\":{\"layerMask\":1.5}}")), "slash rejects fractional layer mask");
        var runtime = new EveGameRuntime();
        runtime.Initialize();
        var loader = new EveTestLoader();
        var identity = new EveEntityIdentity();
        identity.Assign(new EveEntityId(930), EveId.Parse("project_n:eve"), 0);
        var skill = new EveSlashProbeSkill();
        var stat = new EveCharacterStat();
        var move = new EveTestPlayerMove();
        MonoBehaviour[] components = { loader, identity, skill, stat, move, new PNAbilityManager(), new Rigidbody(), new Collider() };
        foreach (var component in components) component.Components = components;
        try
        {
            skill.InitializeInput();
            loader.Load(EveRegistrySession.Context.Registry);
            int spawned = PNArcSlashVisual.Spawned;
            int released = PNArcSlashVisual.Released;
            int created = PNArcSlashVisual.Created;
            double health = stat.GetCurrent(PNContentModule.Health);
            Check(move.LastMoveDirection.x == 1f && move.LastMoveDirection.y == 0f,
                "new movement starts with rightward planar facing");
            var exactDirection = new Vector2(.9063078f, .4226183f);
            move.SetMoveDirection(exactDirection * .4f);
            Check(System.Math.Abs(move.LastMoveDirection.x - exactDirection.x) < .0001f &&
                System.Math.Abs(move.LastMoveDirection.y - exactDirection.y) < .0001f,
                "continuous 25-degree analog input is normalized without cardinal snapping");
            move.SetMoveDirection(Vector2.zero);
            Check(System.Math.Abs(move.LastMoveDirection.y - exactDirection.y) < .0001f,
                "idle input retains last nonzero planar direction");
            Reject(() => move.SetMoveDirection(new Vector2(float.NaN, 0f)),
                "invalid facing input is rejected before changing stored direction");
            using (var unAimed = skill.CreateAction(PNGameplayContentModule.EveSlash, out var unAimedContext))
                Reject(() => unAimed.Start(unAimedContext), "slash requires explicit skill direction before locking movement");
            skill.AttackDirection = new Vector3(1f, 0f, 0f);
            var aim = skill.ResolveAim();
            Check(System.Math.Abs(aim.x - exactDirection.x) < .0001f && aim.y == 0f &&
                System.Math.Abs(aim.z - exactDirection.y) < .0001f,
                "prototype skill selects continuous movement facing rather than fixed fallback");
            // Legacy slash remains independently usable; Eve's actual moveset uses a new Action.
            int legacySpawns = PNArcSlashVisual.Spawned;
            var pool = skill.GetSlashVfxPool();
            using (var legacy = (PNSlashAction)skill.CreateAction(PNGameplayContentModule.EveSlash, out var legacyContext, aim))
            {
                legacy.Start(legacyContext); legacy.Perform(legacyContext);
                Check(pool.CountActive == 1 && !move.CanMove, "legacy slash still borrows visual and locks movement");
                legacy.Advance(.4f); legacy.Exit(legacyContext);
                Check(pool.CountActive == 0 && move.CanMove && legacy.IsComplete, "legacy slash completes independently");
            }
            Check(PNArcSlashVisual.Spawned == legacySpawns + 1, "legacy slash invocation retains registered VFX operation");
            Check(skill.TryAttack() && skill.IsAttacking, "Eve starts independent registered sweep");
            Check(PNArcSlashVisual.Spawned == legacySpawns + 1 && !move.CanMove, "new Eve sweep never invokes legacy slash VFX");
            Check(!skill.TryAttack(), "Eve cannot overlap attacks");
            Time.deltaTime = .4f; skill.Tick();
            Check(!skill.IsAttacking && move.CanMove && stat.GetCurrent(PNContentIds.Health) == health,
                "sweep completes and restores movement without self damage");
            Check(skill.TryAttack(new Vector3(.3f, 0f, .4f)), "sweep supports explicit planar aim");
            skill.DisableInput(); Check(!skill.IsAttacking && move.CanMove, "disable cancels sweep");
            skill.EnableInput(); Check(skill.TryAttack(), "reenable restores attacks"); loader.Unload();
            Check(!skill.IsAttacking && move.CanMove && pool.IsDisposed, "unload cancels sweep and cleans legacy pool");
            loader.Load(EveRegistrySession.Context.Registry); skill.EnableInput();
            Check(skill.TryAttack(), "reload starts a fresh sweep");
            var speedSource = EveId.Parse("test:slash_speed");
            stat.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -50d, 0) });
            Time.deltaTime = .4f; skill.Tick(); Check(skill.IsAttacking, "50 percent speed extends sweep duration");
            stat.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100d, 0) });
            skill.Tick(); Check(skill.IsAttacking, "zero speed pauses sweep");
            stat.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, 100d, 0) });
            Time.deltaTime = .07f; skill.Tick(); Check(!skill.IsAttacking && move.CanMove, "live speed resumes and completes sweep");
            Check(skill.TryAttack(), "200 percent speed permits another sweep");
            Time.deltaTime = .17f; skill.Tick(); Check(!skill.IsAttacking, "200 percent speed halves duration");
            stat.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100d, 0) });
            Check(!skill.TryAttack() && move.CanMove, "zero speed prevents starting sweep");
            stat.RemoveSource(speedSource);
            skill.DestroyInput();
        }
        finally
        {
            PNArcSlashVisual.Fail = false;
            Time.deltaTime = 0.02f;
            skill.DestroyInput();
            EveRegistrySession.End();
        }
    }
}
