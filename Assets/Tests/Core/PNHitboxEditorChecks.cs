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

// EN: Real PhysX in an isolated local scene; never move/save gameplay objects.
// VI: PhysX thật trong scene vật lý tách riêng; không di chuyển/lưu object gameplay.
public static class PNHitboxEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.IsReady || EveRegistrySession.State == EveSessionState.Stopping)
            throw new InvalidOperationException("Hitbox checks require idle Edit Mode.");
        var original = SceneManager.GetActiveScene();
        bool dirty = original.isDirty;
        var scene = EditorSceneManager.NewPreviewScene();
        var world = scene.GetPhysicsScene();
        int checks = 0;
        bool started = false;
        var owned = new List<GameObject>();
        var results = new List<Collider>();
        var targets = new List<PNStat>();
        var pose = new EveHitboxPose(Vector3.zero, Quaternion.identity);
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Reject(Action action, string message)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new Exception(message);
        }
        GameObject Create(string name, Vector3 position)
        {
            var go = new GameObject(name); owned.Add(go);
            SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = position; return go;
        }
        SphereCollider Ball(Vector3 position, float radius = .05f)
        {
            var go = Create("Geometry target", position);
            var collider = go.AddComponent<SphereCollider>(); collider.radius = radius; return collider;
        }
        bool Hits(EveHitboxShape shape, Collider target, EveHitboxPose? at = null)
        {
            using (var query = new EveHitboxQuery(shape, world))
            { query.Query(at ?? pose, results); return results.Contains(target); }
        }
        void Sync() { Physics.SyncTransforms(); }
        try
        {
            Reject(() => EveHitboxShape.Box(new Vector3(1f, 0f, 1f)), "Zero thickness accepted.");
            Reject(() => EveHitboxShape.Sphere(float.NaN), "NaN radius accepted.");
            Reject(() => EveHitboxShape.Capsule(1f, 1f), "Invalid capsule accepted.");
            Reject(() => EveHitboxShape.Sector(1f, .2f, 361f), "Invalid angle accepted.");
            Reject(() => EveHitboxShape.Sector(1f, .2f, 90f, 1f), "Invalid annular hole accepted.");
            Reject(() => EveHitboxShape.Cylinder(1f, .2f, 65), "Oversized convex mesh accepted.");
            Reject(() => EveHitboxPose.Planar(Vector3.zero, Vector3.up), "No planar direction accepted.");
            using (var query = new EveHitboxQuery(EveHitboxShape.Sphere(1f), world))
                Reject(() => query.Query(default, results), "Default pose accepted.");

            var edge = Ball(new Vector3(1.15f, 0f, 0f), .2f);
            var above = Ball(new Vector3(.5f, .4f, 0f));
            var behind = Ball(new Vector3(-.7f, 0f, 0f));
            var hole = Ball(new Vector3(.1f, 0f, 0f));
            var outer = Ball(new Vector3(.75f, 0f, .1f));
            var diagonal = Ball(new Vector3(.8f, 0f, .8f));
            var tall = Ball(new Vector3(0f, 1.1f, 0f), .2f);
            Sync();
            foreach (var shape in new[] { EveHitboxShape.Box(new Vector3(2f, .2f, 2f)),
                EveHitboxShape.Sphere(1f), EveHitboxShape.Capsule(1f, 2f),
                EveHitboxShape.Cylinder(1f, .2f), EveHitboxShape.Sector(1f, .2f, 90f) })
                Check(Hits(shape, edge), "Collider overlap missed when its center lies outside " + shape.Kind);
            Check(!Hits(EveHitboxShape.Box(new Vector3(2f, .2f, 2f)), above), "Flat box leaked vertically.");
            Check(Hits(EveHitboxShape.Sphere(1f), above), "3D sphere missed height.");
            Check(Hits(EveHitboxShape.Capsule(.3f, 2.4f), tall), "Capsule cap missed.");
            Check(!Hits(EveHitboxShape.Cylinder(1f, .2f), above), "Flat cylinder leaked vertically.");
            Check(!Hits(EveHitboxShape.Cylinder(1f, .2f), diagonal), "Cylinder broad box was treated as exact shape.");
            var ring = EveHitboxShape.Sector(1f, .2f, 90f, .4f);
            Check(Hits(ring, outer), "Sector missed annular body.");
            Check(!Hits(ring, hole), "Sector filled its hole.");
            Check(!Hits(ring, behind), "Sector filled its rear broad phase.");
            Check(!Hits(ring, above), "Sector leaked thickness.");
            Check(Hits(EveHitboxShape.Sector(1f, .2f, 360f, .4f), behind), "Full ring missed rear.");
            Check(!Hits(EveHitboxShape.Sector(1f, .2f, 360f, .4f), hole), "Full ring filled hole.");
            var angled = Ball(new Vector3(Mathf.Cos(25f * Mathf.Deg2Rad), 0f, Mathf.Sin(25f * Mathf.Deg2Rad)) * .8f);
            Sync();
            var aimed = EveHitboxPose.Planar(Vector3.zero, angled.transform.position);
            Check(Hits(EveHitboxShape.Sector(1f, .2f, 10f), angled, aimed), "25-degree direction was quantized or reversed.");
            Check(!Hits(EveHitboxShape.Sector(1f, .2f, 10f), angled), "Narrow sector ignored aim.");
            var tilted = Ball(new Vector3(0f, .7f, 0f)); Sync();
            Check(Hits(EveHitboxShape.Box(new Vector3(2f, .1f, .2f)), tilted,
                new EveHitboxPose(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))), "3D rotated box failed.");
            Check(Hits(EveHitboxShape.Sector(1f, .1f, 90f), tilted,
                new EveHitboxPose(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))), "Tilted sector did not form a 3D slice.");
            var wideSector = Ball(new Vector3(-.5f, 0f, .6f)); Sync();
            Check(Hits(EveHitboxShape.Sector(1f, .2f, 300f), wideSector), "Sector wider than 180 degrees missed side.");
            Check(!Hits(EveHitboxShape.Sector(1f, .2f, 300f), behind), "Wide sector filled its excluded rear wedge.");
            var scaledBox = Create("Scaled box target", new Vector3(1.2f, 0f, 0f));
            scaledBox.transform.localScale = new Vector3(2f, 1f, 1f);
            var boxCollider = scaledBox.AddComponent<BoxCollider>(); boxCollider.size = new Vector3(.3f, .1f, .1f); Sync();
            Check(Hits(ring, boxCollider), "Scaled target collider geometry was replaced by its center.");
            using (var query = new EveHitboxQuery(EveHitboxShape.Sphere(2f), world))
            {
                for (int i = 0; i < 80; i++) Ball(new Vector3(.01f * i, 0f, .02f));
                Sync(); query.Query(pose, results);
                Check(results.Count >= 80, "Full query buffer silently truncated.");
                var trigger = Ball(Vector3.zero); trigger.isTrigger = true; trigger.gameObject.layer = 11; Sync();
                query.Query(pose, results, 1 << 11, QueryTriggerInteraction.Ignore);
                Check(results.Count == 0, "Trigger Ignore ignored.");
                query.Query(pose, results, 1 << 11, QueryTriggerInteraction.Collide);
                Check(results.Count == 1 && results[0] == trigger, "Layer or trigger Collide failed.");
                query.Query(pose, results, 0);
                Check(results.Count == 0, "Query retained stale output.");
                trigger.enabled = false; Sync();
                query.Query(pose, results, 1 << 11, QueryTriggerInteraction.Collide);
                Check(results.Count == 0, "Disabled collider detected.");
            }
            var disposed = new EveHitboxQuery(ring, world); disposed.Dispose(); disposed.Dispose();
            try { disposed.Query(pose, results); throw new Exception("Disposed query remained usable."); }
            catch (ObjectDisposedException) { checks++; }

            Reject(() => EveHitboxShape.Ellipse(0f, 1f, .2f), "Ellipse accepted zero radius.");
            Reject(() => EveHitboxShape.Cone(1f, 0f), "Cone accepted zero length.");
            Reject(() => EveHitboxShape.Polygon(new[] { Vector2.zero, Vector2.one }, .2f), "Polygon accepted fewer than 3 points.");
            Reject(() => EveHitboxShape.Polygon(new[] { Vector2.zero, Vector2.right, Vector2.right * 2f }, .2f), "Collinear polygon accepted.");
            Reject(() => EveHitboxShape.Polygon(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.zero }, .2f), "Repeated closing point accepted.");
            Reject(() => EveHitboxShape.Polygon(new[] { Vector2.zero, new Vector2(2f, 2f), new Vector2(0f, 2f), new Vector2(2f, 0f) }, .2f), "Self-intersecting polygon accepted.");
            Reject(() => EveHitboxShape.Polygon(new[] { Vector2.zero, Vector2.right, new Vector2(float.NaN, 1f) }, .2f), "Nonfinite polygon accepted.");
            var ellipseMajor = Ball(new Vector3(1.55f, 0f, 0f), .1f);
            var ellipseMinor = Ball(new Vector3(0f, 0f, .6f), .04f);
            var ellipseCorner = Ball(new Vector3(1.2f, 0f, .4f), .03f);
            var ellipse = EveHitboxShape.Ellipse(1.5f, .5f, .2f);
            Sync();
            Check(Hits(ellipse, ellipseMajor), "Ellipse missed major-axis collider edge.");
            Check(!Hits(ellipse, ellipseMinor), "Ellipse treated minor axis as broad radius.");
            Check(!Hits(ellipse, ellipseCorner), "Ellipse returned broad box corner.");
            Check(!Hits(ellipse, above), "Ellipse ignored thickness.");
            Check(Hits(ellipse, ellipseMajor, new EveHitboxPose(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))) == false,
                "Tilted ellipse retained original plane.");
            Check(Hits(ellipse, tall, new EveHitboxPose(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))), "Tilted ellipse missed vertical major axis.");

            var cone = EveHitboxShape.Cone(1f, 2f);
            var coneInside = Ball(new Vector3(1f, .3f, 0f));
            var coneOutside = Ball(new Vector3(.4f, .6f, 0f));
            var coneCap = Ball(new Vector3(2.05f, 0f, 0f), .1f);
            var coneRear = Ball(new Vector3(-.2f, 0f, 0f));
            var coneRotated = Ball(new Vector3(0f, 1f, .3f)); Sync();
            Check(Hits(cone, coneInside), "Cone missed interior volume.");
            Check(!Hits(cone, coneOutside), "Cone was queried as cylinder/broad box.");
            Check(Hits(cone, coneCap), "Cone base-cap intersection missed outside target center.");
            Check(!Hits(cone, coneRear), "Cone extended behind its apex.");
            Check(Hits(cone, coneRotated, new EveHitboxPose(Vector3.zero, Quaternion.Euler(0f, 0f, 90f))), "Cone rotation lost +X apex-to-base convention.");

            var outline = new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, .6f),
                new Vector2(.6f, .6f), new Vector2(.6f, 2f), new Vector2(0f, 2f) };
            var polygon = EveHitboxShape.Polygon(outline, .2f);
            var polygonBody = Ball(new Vector3(1.6f, 0f, .3f));
            var polygonNotch = Ball(new Vector3(1.3f, 0f, 1.3f));
            var polygonEdge = Ball(new Vector3(2.05f, 0f, .3f), .1f);
            var polygonAbove = Ball(new Vector3(.3f, .4f, .3f)); Sync();
            Check(Hits(polygon, polygonBody), "Concave polygon missed its arm.");
            Check(!Hits(polygon, polygonNotch), "Polygon convex hull filled concave notch.");
            Check(Hits(polygon, polygonEdge), "Off-center polygon bounds missed collider edge.");
            Check(!Hits(polygon, polygonAbove), "Polygon ignored thickness.");
            Array.Reverse(outline);
            var reversed = EveHitboxShape.Polygon(outline, .2f);
            Check(Hits(reversed, polygonBody) && !Hits(reversed, polygonNotch), "Polygon winding changed region.");
            outline[0] = new Vector2(100f, 100f);
            Check(Hits(polygon, polygonBody) && polygon.Points.All(p => p.x < 100f), "Polygon retained mutable input points.");
            try { ((IList<Vector2>)polygon.Points)[0] = Vector2.one; throw new Exception("Polygon exposed mutable points."); }
            catch (NotSupportedException) { checks++; }
            var shiftedPose = new EveHitboxPose(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            var shiftedTarget = Ball(shiftedPose.Position + shiftedPose.Rotation * new Vector3(1.6f, 0f, .3f)); Sync();
            Check(Hits(polygon, shiftedTarget, shiftedPose), "Polygon translated/rotated bounds failed.");
            var sixtyFour = Enumerable.Range(0, 64).Select(i => new Vector2(Mathf.Cos(i * Mathf.PI / 32f), Mathf.Sin(i * Mathf.PI / 32f))).ToArray();
            Check(Hits(EveHitboxShape.Polygon(sixtyFour, .2f), outer), "Maximum-size polygon triangulation failed.");
            Reject(() => EveHitboxShape.Polygon(sixtyFour.Concat(new[] { new Vector2(10f, 10f) }), .2f), "Oversized polygon accepted.");
            var authored = Create("Hitbox authoring probe", Vector3.zero).AddComponent<EveHitboxRegion>();
            var settings = new SerializedObject(authored);
            settings.FindProperty("kind").enumValueIndex = (int)EveHitboxKind.Ellipse;
            settings.FindProperty("ellipseRadii").vector2Value = new Vector2(1.5f, .5f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            authored.Query(pose, results);
            Check(results.Contains(ellipseMajor) && !results.Contains(ellipseMinor), "Ellipse Inspector/local-scene query failed.");
            authored.enabled = false;
            settings.Update(); settings.FindProperty("kind").enumValueIndex = (int)EveHitboxKind.Cone;
            settings.FindProperty("height").floatValue = 2f; settings.ApplyModifiedPropertiesWithoutUndo();
            authored.enabled = true; authored.Query(pose, results);
            Check(results.Contains(coneInside) && !results.Contains(coneOutside), "Cone Inspector query failed.");
            authored.enabled = false;
            settings.Update(); settings.FindProperty("kind").enumValueIndex = (int)EveHitboxKind.Polygon;
            settings.FindProperty("height").floatValue = .2f;
            var verticesProperty = settings.FindProperty("polygonPoints"); verticesProperty.arraySize = polygon.Points.Count;
            for (int i = 0; i < polygon.Points.Count; i++) verticesProperty.GetArrayElementAtIndex(i).vector2Value = polygon.Points[i];
            settings.ApplyModifiedPropertiesWithoutUndo(); authored.enabled = true; authored.Query(pose, results);
            Check(results.Contains(polygonBody) && !results.Contains(polygonNotch), "Polygon Inspector query filled notch.");
            authored.enabled = false;

            var documents = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
                Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, documents);
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, documents, "test:hitbox"));
            PNStat Entity(string name, Vector3 position, bool load = true)
            {
                var go = Create(name, position); go.SetActive(false);
                go.AddComponent<EveEntityIdentity>().Assign(EveEntityId.NewLocal(), EveId.Parse("project_n:eve"), 0);
                var stat = go.AddComponent<PNStat>();
                var loader = go.AddComponent<EveEntityLoader>();
                if (load) loader.Load(hub);
                go.SetActive(true); return stat;
            }
            SphereCollider Child(PNStat entity)
            {
                var go = Create("Child collider", entity.transform.position);
                go.transform.SetParent(entity.transform, true); return go.AddComponent<SphereCollider>();
            }
            var source = Entity("Source", Vector3.zero); Child(source);
            var target = Entity("Target", new Vector3(.5f, 0f, 0f)); Child(target); Child(target);
            var dead = Entity("Dead", new Vector3(.7f, 0f, 0f)); Child(dead); dead.SetCurrent(PNContentIds.Health, 0f);
            var notReady = Entity("Not ready", Vector3.zero, false); Child(notReady);
            var nested = Create("Nested unbound entity", target.transform.position);
            nested.transform.SetParent(target.transform, true); nested.AddComponent<EveEntityIdentity>(); nested.AddComponent<SphereCollider>();
            Sync();
            using (var query = new EveHitboxQuery(EveHitboxShape.Sphere(2f), world))
            {
                var resolver = new PNHitboxTargets();
                resolver.Query(query, pose, targets, source);
                Check(targets.Count == 1 && targets[0] == target, "Self/dead/Ready/child collider dedupe failed.");
                resolver.Query(query, pose, targets, source, filter: candidate => false);
                Check(targets.Count == 0, "Caller faction/filter ignored.");
                resolver.Query(query, pose, targets, source, includeDead: true);
                Check(targets.Count == 2, "Explicit includeDead failed.");
                target.GetComponent<EveEntityLoader>().Unload();
                resolver.Query(query, pose, targets, source);
                Check(targets.Count == 0, "Unloaded entity detected.");
                target.GetComponent<EveEntityLoader>().Load(hub);
            }
            using (var cast = new PNHitboxCast(EveHitboxShape.Sphere(2f), world))
            {
                cast.Query(pose, 0d, targets, source); Check(targets.Count == 1, "Cast failed to query target.");
                cast.Query(pose, 0d, targets, source); Check(targets.Count == 1, "Query consumed hit before commit.");
                cast.CommitHit(target, 0d);
                cast.Query(pose, 5d, targets, source); Check(targets.Count == 0, "Once-per-cast target repeated.");
                Reject(() => cast.Query(pose, 4d, targets, source), "Clock reversal accepted.");
            }
            using (var cast = new PNHitboxCast(EveHitboxShape.Sphere(2f), world, .5d))
            {
                cast.Query(pose, 0d, targets, source); cast.CommitHit(target, 0d);
                cast.Query(pose, .49d, targets, source); Check(targets.Count == 0, "Repeat delay ignored.");
                cast.Query(pose, .5d, targets, source); Check(targets.Count == 1, "Repeat interval boundary missed.");
                cast.CommitHit(target, .5d);
                cast.Query(pose, .75d, targets, source); Check(targets.Count == 0, "Repeat did not update last-hit time.");
            }
            using (var fresh = new PNHitboxCast(ring, world))
            { fresh.Query(pose, 0d, targets, source); Check(targets.Count == 1, "New cast inherited old hit history."); }
            var endedCast = new PNHitboxCast(ring, world);
            EveRegistrySession.End(); started = false;
            try { endedCast.Query(pose, 0d, targets, source); throw new Exception("Ended-session cast remained usable."); }
            catch (InvalidOperationException) { checks++; }
            finally { endedCast.Dispose(); }
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally
            {
                foreach (var go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(scene);
                SceneManager.SetActiveScene(original);
            }
        }
        Check(original.isDirty == dirty, "Hitbox checks changed gameplay scene dirty state.");
        return "PASS " + checks + " native hitbox checks: collider intersection, eight shapes, concave polygon, local physics, continuous aim, buffers, filters, entity dedupe and per-cast repetition.";
    }
}
#endif
