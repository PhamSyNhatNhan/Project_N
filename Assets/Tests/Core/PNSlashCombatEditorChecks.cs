#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PNSlashFailingReceiverProbe : PNEnemyStat
{
    public override PNDamageResult ReceiveDamage(PNDamage damage) => throw new InvalidOperationException("Intentional native slash receiver failure.");
}

public class PNSlashTimelineActionProbe : PNSlashAction
{
    public Action<PNArcSlashVisual> Configure;
    protected override void ConfigureAttack(PNArcSlashVisual visual) => Configure?.Invoke(visual);
}

public sealed class PNEveAttackSpeedProbe : EveCharacterSkill
{
    public void BeginCategoryReview()
    {
        var id = EveId.Parse("base:eve_attack_entry");
        if (!CanActivateSkill(id)) throw new InvalidOperationException("Category review attack admission failed.");
        BeginActivation(id);
    }
    public void EndCategoryReview() => EndActivation();
    public void TickAttack(float delta) => AdvanceAttack(delta);
    public void EnsureInput() { if (pc == null) Awake(); OnEnable(); }
}

// EN: Real JSON, VFX pool, PhysX and HP commits in a disposable preview scene.
// VI: JSON, pool VFX, PhysX và HP commit thật trong preview scene dùng tạm.
public static class PNSlashCombatEditorChecks
{
    public static string RepeatedDamageTrace { get; private set; }
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.IsReady || EveRegistrySession.State == EveSessionState.Stopping)
            throw new InvalidOperationException("Slash combat checks require idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        var owned = new List<GameObject>(); bool started = false; int checks = 0; long id = 940000;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, documents);
        void Start(EveSessionRole role)
        {
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, role, EveContentCompatibility.Create(hub, documents, "test:slash_combat"));
        }
        T Entity<T>(string name, Vector3 position, string character, bool skill = false) where T : PNStat
        {
            var go = new GameObject(name); go.SetActive(false); owned.Add(go); SceneManager.MoveGameObjectToScene(go, preview);
            go.transform.position = position;
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = go.AddComponent<T>().Declared(); stat.gameObject.layer = stat is PNEnemyStat ? PNLayers.Enemy : stat is PNPlayerStat ? PNLayers.Player : stat.gameObject.layer;
            if (skill)
            {
                go.AddComponent<Rigidbody>().useGravity = false;
                go.AddComponent<CapsuleCollider>(); go.AddComponent<PNPlayerMove>(); go.AddComponent<PNEveAttackSpeedProbe>();
            }
            var loader = go.AddComponent<EveEntityLoader>(); loader.Load(hub); go.SetActive(true);
            return stat;
        }
        void Hurtbox(PNStat stat, bool duplicate = false)
        {
            for (int i = 0; i < (duplicate ? 2 : 1); i++)
            {
                var child = new GameObject("Slash child hurtbox"); child.transform.SetParent(stat.transform, false);
                child.AddComponent<SphereCollider>().radius = .05f;
            }
        }
        PNSlashAction Cast(EveCharacterSkill skill, out PNSkillActionContext context, Vector3? direction = null)
        {
            var action = (PNSlashAction)skill.CreateAction(EveId.Parse("base:eve_slash"), out context, direction ?? Vector3.right);
            try { action.Start(context); action.Perform(context); return action; }
            catch { action.Dispose(); throw; }
        }
        void Clear()
        {
            foreach (var go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            owned.Clear();
        }
        try
        {
            Start(EveSessionRole.Offline);
            var caster = Entity<EveCharacterStat>("Slash caster", Vector3.zero, "project_n:eve", true);
            // EN: Fixed-damage assertions must not inherit the authored random crit rate.
            // VI: Kiểm tra damage cố định không được dùng crit ngẫu nhiên từ dữ liệu người dùng.
            caster.SetSource(EveId.Parse("test:slash_fixed_crit"), new[] {
                new EveStatModifier(PNContentIds.CritRate, -caster.Evaluate(PNContentIds.CritRate), 0)
            });
            // Dummy stays at original 200M HP. Use exactly representable damage to avoid its float ULP rounding.
            caster.SetSource(EveId.Parse("test:slash_attack"), new[] { new EveStatModifier(PNContentIds.AttackPower, 1014f, 0) });
            var enemy = Entity<DummyStat>("Dummy victim", new Vector3(.5f, .18f, 0f), "project_n:dummy"); Hurtbox(enemy, true);
            var ally = Entity<PNPlayerStat>("Friendly", new Vector3(.4f, .18f, 0f), "project_n:eve"); Hurtbox(ally);
            var neutral = Entity<PNStat>("Neutral", new Vector3(.4f, .18f, 0f), "project_n:dummy"); Hurtbox(neutral);
            var rear = Entity<DummyStat>("Behind", new Vector3(-.5f, .18f, 0f), "project_n:dummy"); Hurtbox(rear);
            var outside = Entity<DummyStat>("Out of range", new Vector3(1.1f, .18f, 0f), "project_n:dummy"); Hurtbox(outside);
            var late = Entity<DummyStat>("Late entrant", new Vector3(2f, .18f, 0f), "project_n:dummy"); Hurtbox(late);
            var skill = caster.GetComponent<EveCharacterSkill>(); var move = caster.GetComponent<PNPlayerMove>();
            var categoryOwner = EveId.Parse("test:slash_category_bonus");
            double categoryHealth = enemy.GetCurrent(PNContentIds.Health);
            caster.SetSource(categoryOwner, new[] { new EveStatModifier(PNContentIds.AttackTag, .2d, 1, PNContentIds.Scale) });
            enemy.SetSource(categoryOwner, new[] { new EveStatModifier(PNContentIds.AttackTag, .25d, 1, PNContentIds.Mitigation) });
            caster.GetComponent<PNEveAttackSpeedProbe>().BeginCategoryReview(); Physics.SyncTransforms();
            using (var categoryAction = Cast(skill, out var categoryContext))
            {
                categoryAction.Advance(1f);
                var categoryHit = categoryAction.Results.First(x => x.TargetId == enemy.EntityId);
                Check(categoryHit.Damage.Tags.Contains(PNContentIds.AttackTag), "Actual PhysX Slash delivers the activation category tag.");
                Check(Math.Abs(categoryHit.Damage.Amount - categoryAction.Damage.Amount * 1.2d) < 1e-8 &&
                    Math.Abs(categoryHit.CalculatedDamage - categoryHit.Damage.Amount * 1.25d) < 1e-8,
                    "Actual Slash category outgoing bonus and receiver mitigation apply once.");
                categoryAction.Exit(categoryContext);
            }
            caster.GetComponent<PNEveAttackSpeedProbe>().EndCategoryReview(); caster.RemoveSource(categoryOwner); enemy.RemoveSource(categoryOwner);
            enemy.SetCurrent(PNContentIds.Health, categoryHealth); enemy.AdvanceCombatTime(100d);
            Physics.SyncTransforms(); int events = 0; enemy.DamageReceived += hit => events++;
            double enemyBefore = enemy.GetCurrent(PNContentIds.Health), allyBefore = ally.GetCurrent(PNContentIds.Health);
            using (var action = Cast(skill, out var context))
            {
                Check(action.Damage.Amount == 1024f && action.Results.Count == 0, "Perform did not snapshot scaled damage before window.");
                Check(!move.CanMove && skill.GetSlashVfxPool().CountActive == 1, "Combat slash lost lock/VFX.");
                action.Advance(.05f); Check(events == 0, "Damage occurred before window start.");
                caster.transform.position = new Vector3(5f, 0f, 0f); move.SetMoveDirection(Vector2.left);
                caster.SetSource(EveId.Parse("test:post_perform_buff"), new[] { new EveStatModifier(PNContentIds.AttackPower, 500f, 0) });
                Physics.SyncTransforms(); action.Advance(.02f);
                Check(events == 1 && enemy.GetCurrent(PNContentIds.Health) == enemyBefore - 1024f, "Fixed-origin slash missed dummy or double-hit child colliders.");
                Check(action.Results.Count == 1 && action.Results[0].CalculatedDamage == 1024f, "Damage snapshot changed after Perform buff.");
                Check(ally.GetCurrent(PNContentIds.Health) == allyBefore && neutral.GetCurrent(PNContentIds.Health) == 200000000f,
                    "Slash damaged friendly/neutral entity.");
                Check(rear.GetCurrent(PNContentIds.Health) == 200000000f && outside.GetCurrent(PNContentIds.Health) == 200000000f, "Slash hit rear/out-of-range entity.");
                late.transform.position = new Vector3(.45f, .18f, 0f); Physics.SyncTransforms(); action.Advance(0f);
                Check(late.GetCurrent(PNContentIds.Health) == 200000000f, "Paused slash applied a new hit without clock advancement.");
                action.Advance(.04f);
                Check(late.GetCurrent(PNContentIds.Health) == 199998976f && events == 1, "Window scan missed late entrant or repeated previous victim.");
                action.Advance(.1f); action.Advance(.2f);
                Check(events == 1 && action.IsComplete, "Window close or once-per-cast failed.");
                action.Exit(context); Check(move.CanMove && skill.GetSlashVfxPool().CountActive == 0, "Exit leaked movement lock or VFX.");
            }
            caster.transform.position = Vector3.zero;
            caster.RemoveSource(EveId.Parse("test:post_perform_buff")); Physics.SyncTransforms();
            var speedProbe = caster.GetComponent<PNEveAttackSpeedProbe>();
            var speedSource = EveId.Parse("test:native_attack_speed");
            caster.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, 100d, 0) });
            int beforeFast = events;
            Check(speedProbe.TryAttack(Vector3.right), "Fast normal attack could not start.");
            speedProbe.TickAttack(.025f);
            Check(events == beforeFast && Math.Abs(speedProbe.AttackVisual.Progress - .05f / .32f) < .0001f,
                "200 percent speed did not advance VFX before hit window in sync.");
            speedProbe.TickAttack(.085f);
            Check(events == beforeFast + 1, "200 percent speed did not open hit window at half normal time.");
            speedProbe.TickAttack(.13f);
            Check(!speedProbe.IsAttacking && move.CanMove, "Fast slash completion leaked movement lock.");
            caster.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -50d, 0) });
            int beforeSlow = events;
            Check(speedProbe.TryAttack(Vector3.right), "Slow normal attack could not start.");
            speedProbe.TickAttack(.1f);
            Check(events == beforeSlow && speedProbe.IsAttacking, "50 percent speed opened the hit window too early.");
            speedProbe.TickAttack(.34f);
            Check(events == beforeSlow + 1 && Math.Abs(speedProbe.AttackVisual.Progress - .22f / .32f) < .0001f,
                "Slow slash hitbox and visual timeline diverged.");
            caster.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100d, 0) });
            float frozenProgress = speedProbe.AttackVisual.Progress;
            speedProbe.TickAttack(1f);
            Check(speedProbe.IsAttacking && speedProbe.AttackVisual.Progress == frozenProgress && events == beforeSlow + 1,
                "Zero attack speed advanced visual or delivered another hit.");
            caster.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, 100d, 0) });
            speedProbe.TickAttack(.13f);
            Check(!speedProbe.IsAttacking && move.CanMove, "Live speed change failed to finish existing slash.");
            caster.SetSource(speedSource, new[] { new EveStatModifier(PNContentIds.AttackSpeed, -100d, 0) });
            Check(!speedProbe.TryAttack(Vector3.right) && move.CanMove, "Zero speed started a new attack and locked movement.");
            caster.RemoveSource(speedSource);
            // Isolate the following fixed-damage check from receiver iframe left by speed tests.
            enemy.AdvanceCombatTime(100d);
            double health = enemy.GetCurrent(PNContentIds.Health);
            using (var action = Cast(skill, out _))
            { action.Advance(.4f); Check(enemy.GetCurrent(PNContentIds.Health) == health - 1024f, "Frame hitch skipped full hit window or new cast retained hit history."); }
            health = enemy.GetCurrent(PNContentIds.Health);
            using (var action = Cast(skill, out var context))
            { action.Cancel(context); action.Advance(.2f); Check(enemy.GetCurrent(PNContentIds.Health) == health && move.CanMove, "Cancel before hit leaked damage/lock."); }

            // Damage callback cancels mid-delivery: cleanup is deferred until this committed hit returns.
            double lateBefore = late.GetCurrent(PNContentIds.Health);
            using (var action = Cast(skill, out var context))
            {
                Action<PNDamageResult> cancel = hit => action.Cancel(context); enemy.DamageReceived += cancel;
                try { action.Advance(.08f); }
                finally { enemy.DamageReceived -= cancel; }
                Check(action.Results.Count == 1 && late.GetCurrent(PNContentIds.Health) == lateBefore && move.CanMove,
                    "Callback cancellation replayed hits or leaked cleanup.");
            }
            var failing = Entity<PNSlashFailingReceiverProbe>("Failing receiver", new Vector3(.45f, .18f, 0f), "project_n:dummy"); Hurtbox(failing);
            Physics.SyncTransforms();
            using (var action = Cast(skill, out _))
            {
                bool threw = false; try { action.Advance(.08f); } catch (InvalidOperationException) { threw = true; }
                int count = action.Results.Count; action.Advance(.08f);
                Check(threw && count >= 1 && action.Results.Count == count && move.CanMove && skill.GetSlashVfxPool().CountActive == 0,
                    "Receiver failure allowed retry or leaked cast resources.");
            }
            failing.gameObject.SetActive(false); enemy.gameObject.SetActive(false); late.gameObject.SetActive(false);
            var direction = new Vector3(Mathf.Cos(25f * Mathf.Deg2Rad), 0f, Mathf.Sin(25f * Mathf.Deg2Rad));
            var angled = Entity<DummyStat>("25 degree victim", direction * .5f + Vector3.up * .18f, "project_n:dummy"); Hurtbox(angled);
            Physics.SyncTransforms();
            using (var action = Cast(skill, out _, direction))
            { action.Advance(.08f); Check(angled.GetCurrent(PNContentIds.Health) == 199998976f, "Skill's continuous aim did not drive real damage sector."); }
            caster.SetCurrent(PNContentIds.Health, 0f);
            Check(!skill.TryAttack(), "Dead caster could start a new attack.");
            caster.SetCurrent(PNContentIds.Health, 100f);
            angled.transform.position = new Vector3(.5f, .18f, 0f); Physics.SyncTransforms();
            var asset = Resources.Load<GameObject>("PNAttacks/EveSlash").GetComponent<PNSlashPrefab>();
            string fingerprint = PNSlashPrefabContent.Fingerprint(asset);
            var firstCopy = asset.Capture(); var secondCopy = asset.Capture();
            firstCopy[0].Geometry.Radius = 9f; firstCopy[0].Radial.keys = new[] { new Keyframe(0f, 9f) };
            Check(secondCopy[0].Geometry.Radius == .6f && secondCopy[0].Radial.Evaluate(0f) < 1f, "Prefab copies share geometry/curves.");
            Check(fingerprint == PNSlashPrefabContent.Fingerprint(asset), "Per-cast edits changed original prefab.");
            var zero = secondCopy[0].Clone(); zero.Radial = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Check(!zero.Sample(0f, new EveHitboxPose(Vector3.zero, Quaternion.identity)).Enabled, "Zero-size timeline must disable querying.");
            angled.AdvanceCombatTime(100d);
            using (var unused = skill.CreateAction(EveId.Parse("base:eve_slash"), out var timelineContext, Vector3.right))
            using (var sweeping = new PNSlashTimelineActionProbe())
            {
                sweeping.Configure = visual =>
                {
                    var track = visual.Tracks[0]; track.Geometry.Kind = EveHitboxKind.Box; track.Geometry.Size = new Vector3(.06f, .1f, .06f);
                    track.Offset = new Vector3(.5f, .18f, -.4f);
                    track.Radial = AnimationCurve.Linear(0f, 1f, 1f, 1f); track.Angular = AnimationCurve.Linear(0f, 1f, 1f, 1f);
                    track.OffsetZ = AnimationCurve.Linear(0f, 0f, 1f, .8f);
                    visual.Tracks = new[] { track, track.Clone() };
                    var parent = new EveHitboxPose(Vector3.zero, Quaternion.identity);
                    Check(Mathf.Abs(track.Sample(.06f / .32f, parent).Pose.Position.z) > .08f &&
                        Mathf.Abs(track.Sample(.20f / .32f, parent).Pose.Position.z) > .08f, "Sweep fixture touches endpoint; cannot prove substeps.");
                };
                double before = angled.GetCurrent(PNContentIds.Health);
                sweeping.Start(timelineContext); sweeping.Perform(timelineContext); sweeping.Advance(.25f);
                Check(sweeping.Results.Count == 1 && angled.GetCurrent(PNContentIds.Health) == before - 1024f,
                    "Substeps missed middle-of-frame target or overlapping tracks double-hit.");
                sweeping.Advance(.1f); Check(sweeping.Results.Count == 1, "Changing geometry reset committed history.");
            }
            Check(fingerprint == PNSlashPrefabContent.Fingerprint(asset), "Runtime code override escaped invocation.");
            Check(PNSlashPrefabContent.Hashes(hub).ContainsKey("PNAttacks/EveSlash"), "Prefab geometry absent from compatibility hashes.");
            foreach (var collider in angled.GetComponentsInChildren<Collider>()) collider.gameObject.layer = 8;
            Physics.SyncTransforms();
            foreach (int mask in new[] { 0, 1 << 7 })
            {
                using (var unused = skill.CreateAction(EveId.Parse("base:eve_slash"), out var maskedContext, Vector3.right))
                using (var action = new PNSlashTimelineActionProbe { Configure = visual => visual.Tracks[0].LayerMask = mask })
                {
                    double before = angled.GetCurrent(PNContentIds.Health);
                    action.Start(maskedContext); action.Perform(maskedContext); action.Advance(.25f);
                    Check(action.Results.Count == 0 && angled.GetCurrent(PNContentIds.Health) == before,
                        "Nothing/excluded component layer mask scanned an unselected collider layer.");
                }
            }
            angled.AdvanceCombatTime(100d);
            using (var unused = skill.CreateAction(EveId.Parse("base:eve_slash"), out var mutableMaskContext, Vector3.right))
            using (var action = new PNSlashTimelineActionProbe { Configure = visual => visual.Tracks[0].LayerMask = 1 << 7 })
            {
                double before = angled.GetCurrent(PNContentIds.Health);
                action.Start(mutableMaskContext); action.Perform(mutableMaskContext); action.Advance(.08f);
                Check(action.Results.Count == 0, "Excluded layer reached damage before mask override.");
                action.Visual.Tracks[0].LayerMask = (1 << 7) | (1 << 8); action.Advance(.02f);
                Check(action.Results.Count == 1 && angled.GetCurrent(PNContentIds.Health) == before - 1024f,
                    "Code cannot update this invocation's layer mask.");
            }
            var secondLayerVictim = Entity<DummyStat>("Second selected layer", new Vector3(.45f, .18f, 0f), "project_n:dummy");
            Hurtbox(secondLayerVictim);
            foreach (var collider in secondLayerVictim.GetComponentsInChildren<Collider>()) collider.gameObject.layer = 7;
            Physics.SyncTransforms();
            using (var unused = skill.CreateAction(EveId.Parse("base:eve_slash"), out var multiMaskContext, Vector3.right))
            using (var action = new PNSlashTimelineActionProbe { Configure = visual => visual.Tracks[0].LayerMask = (1 << 7) | (1 << 8) })
            {
                action.Start(multiMaskContext); action.Perform(multiMaskContext); action.Advance(.25f);
                Check(action.Results.Count == 2, "Combined component layer mask did not scan both selected layers.");
            }
            // Reproduce the reported ten visible casts with the real default ATK10/HP200M.
            caster.RemoveSource(EveId.Parse("test:slash_attack"));
            secondLayerVictim.gameObject.SetActive(false);
            angled.SetCurrent(PNContentIds.Health, 200000000f);
            int repeatedEvents = 0, legacyDisplayChanges = 0;
            angled.DamageReceived += hit => repeatedEvents++;
            var trace = new System.Text.StringBuilder();
            for (int i = 1; i <= 10; i++)
            {
                // Each repetition verifies a separate full-damage cast, not an iframe burst.
                angled.AdvanceCombatTime(100d);
                double before = angled.GetCurrent(PNContentIds.Health);
                using (var action = Cast(skill, out _))
                {
                    action.Advance(.4f);
                    double after = angled.GetCurrent(PNContentIds.Health);
                    if (before.ToString("0.##") != after.ToString("0.##")) legacyDisplayChanges++;
                    Check(action.Results.Count == 1 && action.Damage.Amount == 10f && action.Results[0].CalculatedDamage == 10f && after < before,
                        "Repeated default-damage slash missed target, lost delivery, or retained prior hit history.");
                    trace.AppendLine("Cast " + i + ": packet=" + action.Damage.Amount + ", actualHP=" + ((double)after).ToString("0.##") +
                        ", oldInspector=" + after.ToString("0.##") + ", HPLoss=" + (before - after));
                }
            }
            Check(repeatedEvents == 10 && angled.GetCurrent(PNContentIds.Health) == 199999900d,
                "Ten default-damage casts did not produce ten committed hits.");
            RepeatedDamageTrace = trace + "DamageReceived=" + repeatedEvents + ", legacy Inspector changes=" + legacyDisplayChanges;
            // End the session before destroying entities: Unload (e.g. skill bindings) needs live components in Edit Mode.
            EveRegistrySession.End(); started = false; Clear();
            Start(EveSessionRole.Client);
            var client = Entity<EveCharacterStat>("Client caster", Vector3.zero, "project_n:eve", true);
            var clientVictim = Entity<DummyStat>("Client victim", new Vector3(.5f, .18f, 0f), "project_n:dummy"); Hurtbox(clientVictim);
            Physics.SyncTransforms();
            var clientSkill = client.GetComponent<EveCharacterSkill>();
            using (var action = (PNSlashAction)clientSkill.CreateAction(EveId.Parse("base:eve_slash"), out var clientContext, Vector3.right))
            {
                bool denied = false;
                try { action.Start(clientContext); }
                catch (InvalidOperationException) { denied = true; action.Cancel(clientContext); }
                Check(denied && action.Damage == null && action.Results.Count == 0 &&
                    clientVictim.GetCurrent(PNContentIds.Health) == 200000000d,
                    "Client cannot acquire authoritative action locks or apply damage.");
                Check(clientSkill.GetSlashVfxPool().CountActive == 0, "Rejected client action leaked a visual borrow.");
            }
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { try { Clear(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        Check(original.isDirty == dirty, "Slash checks changed gameplay scene.");
        Check(!UnityEngine.Resources.FindObjectsOfTypeAll<MeshCollider>().Any(p => p.gameObject.name == "Eve Hitbox Query Probes"), "Slash query probes leaked.");
        return "PASS " + checks + " native slash combat checks: real Eve JSON -> sector -> Dummy HP, windows/hitches, dedupe, teams, snapshot/aim/origin, cancel/failure cleanup, client authority.";
    }
}
#endif
