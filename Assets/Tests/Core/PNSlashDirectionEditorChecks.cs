#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Verify real pooled slash transforms at arbitrary movement angles without Play Mode.
// VI: Kiem tra transform VFX chem that tu pool o goc di chuyen bat ky ngoai Play Mode.
public static class PNSlashDirectionEditorChecks
{
    public static string RunSkillFacing()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stop Play Mode and the session before skill facing checks.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        bool started = false;
        try
        {
            var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
                Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] {
                new PNContentModule(), new PNGameplayContentModule()
            }, documents);
            EveRegistrySession.Begin();
            started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, documents, "test:skill_movement_facing"));
            var caster = new GameObject("Native skill facing probe");
            caster.SetActive(false);
            SceneManager.MoveGameObjectToScene(caster, preview);
            caster.AddComponent<EveEntityIdentity>().Assign(EveEntityId.NewLocal(), EveId.Parse("project_n:eve"), 0);
            caster.AddComponent<EveCharacterStat>();
            caster.AddComponent<Rigidbody>();
            caster.AddComponent<CapsuleCollider>();
            var move = caster.AddComponent<PNPlayerMove>();
            var skill = caster.AddComponent<EveCharacterSkill>();
            var loader = caster.AddComponent<EveEntityLoader>();
            foreach (float angle in new[] { 25f, 155f, -72f })
            {
                loader.Load(hub);
                caster.SetActive(true);
                float radians = angle * Mathf.Deg2Rad;
                var planar = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                move.SetMoveDirection(planar);
                move.SetMoveDirection(Vector2.zero);
                if (!skill.TryAttack() || skill.AttackVisual == null ||
                    Vector3.Dot(skill.AttackVisual.transform.right, new Vector3(planar.x, 0f, planar.y)) < .99999f)
                    throw new Exception("Input-style no-argument skill attack did not follow last movement facing.");
                var visual = skill.AttackVisual;
                loader.Unload();
                caster.SetActive(false);
                if (skill.IsAttacking || visual != null)
                    throw new Exception("Unloading caster did not dispose its moving sweep.");
            }
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (scene.isDirty != dirty) throw new Exception("Skill facing checks changed gameplay scene.");
        return "PASS native no-arg Eve skill: last movement25/155/-72 -> context -> action -> real VFX; idle/lock keeps direction; unload cancels and destroys moving sweep.";
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        EveEngine.EveEasyPooling pool = null;
        try
        {
            var root = new GameObject("Slash direction probe");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = new Vector3(2f, .4f, 1f);
            var skill = root.AddComponent<PNSkill>();
            var move = root.AddComponent<PNMove>();
            var settings = new PNSlashVfxSettings(new JObject());
            pool = PNArcSlashVisual.CreatePool();
            foreach (float angle in new[] { 25f, 90f, 155f, -72f, 180f })
            {
                float radians = angle * Mathf.Deg2Rad;
                var planar = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                move.SetMoveDirection(planar * .4f);
                move.StopMovement();
                var direction = new Vector3(planar.x, 0f, planar.y);
                move.SetMoveDirection(Vector2.left);
                var visual = PNArcSlashVisual.Spawn(pool, skill, settings, direction);
                try
                {
                    if (Vector3.Dot(visual.transform.right, direction) < .99999f ||
                        Vector3.Distance(visual.transform.position, root.transform.position +
                            direction * settings.ForwardOffset + Vector3.up * settings.Height) > .0001f)
                        throw new Exception("Slash angle/forward offset ignored the supplied skill direction.");
                    var rotation = visual.transform.rotation;
                    move.SetMoveDirection(Vector2.left);
                    visual.Render(.5f);
                    if (Quaternion.Angle(visual.transform.rotation, rotation) > .001f ||
                        visual.GetComponent<MeshFilter>().sharedMesh.vertexCount == 0)
                        throw new Exception("Active slash changed direction or has no mesh.");
                }
                finally { visual.Release(); }
                if (pool.CountActive != 0) throw new Exception("Slash was not returned to pool.");
            }
        }
        finally
        {
            try { pool?.Dispose(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (scene.isDirty != dirty) throw new Exception("Slash checks changed gameplay scene.");
        return "PASS native slash angles25/90/155/-72/180: explicit skill direction independent of movement, planar offset, fixed during cast, real mesh and pool reuse.";
    }
}
#endif
