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
using UnityEngine.Rendering.Universal;

public static class PNGuraObjectVfxEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed ||
            EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Idle clean Edit Mode required.");
        var scene = EditorSceneManager.NewPreviewScene();
        var managerRoot = new GameObject("Gura object VFX fixture");
        SceneManager.MoveGameObjectToScene(managerRoot, scene);
        var runtime = managerRoot.AddComponent<EveGameRuntime>();
        var oldTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D output = null;
        EveEntityLoader loader = null;
        PNProjectileManager projectiles = null;
        PNBlastManager blasts = null;
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); ++checks; }
        try
        {
            runtime.BeginSession(EveSessionRole.Offline);
            var session = EveRegistrySession.Context;
            var hub = session.Registry;
            projectiles = managerRoot.AddComponent<PNProjectileManager>();
            projectiles.Initialize(session,new PNProjectileProfile());
            projectiles.ManualAdvance = true;
            blasts = managerRoot.AddComponent<PNBlastManager>();
            blasts.Initialize(session);
            var gura = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Gura/Prefab/Gura.prefab"), scene);
            gura.SetActive(false);
            loader = gura.GetComponent<EveEntityLoader>();
            gura.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(930401), PNGuraContent.Character, 1);
            loader.Load(hub);
            gura.SetActive(true);
            var stat = gura.GetComponent<PNGuraStat>();
            var body = gura.GetComponent<PNVisualBody>();
            Check(body.Begin(hub.Characters.Get(PNGuraContent.Character),hub),"Couldn't open the Visual rig.");
            var rigRegistry = hub.Get<PNVisualRigDefinition>();
            var rig = rigRegistry.Get(body.Actor.Appearance.Rig).Resolve(id => rigRegistry.Get(id));
            int weapon = rig.SlotIndex("gura_weapon");
            Check(weapon >= 0 && body.Actor.Animator.SlotAlpha[weapon] == 0f, "The newly bound idle body displays a trident.");
            string outputDir = Path.GetFullPath(".utmp/GuraSharkRough/frames");
            Directory.CreateDirectory(outputDir);
            var cameraRoot = new GameObject("Object test camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled=false; camera.scene=scene; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.09f,.14f,.19f);
            camera.orthographic=true; camera.cullingMask=1<<31;
            target = new RenderTexture(960,540,24);
            output = new Texture2D(960,540,TextureFormat.RGB24,false);
            camera.targetTexture=target;
            void Capture(string file,Vector3 center,float size,GameObject effect)
            {
                foreach (var t in gura.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
                foreach (var t in effect.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
                camera.orthographicSize=size;
                camera.transform.position=center+new Vector3(0,5,-8.4f); camera.transform.LookAt(center);
                camera.Render(); RenderTexture.active=target;
                output.ReadPixels(new Rect(0,0,960,540),0,0); output.Apply();
                File.WriteAllBytes(Path.Combine(outputDir, file + ".png"),output.EncodeToPNG());
            }
            // The drawn water against the hitbox: sheet and edge stay within the hit radius, angle and thickness, and cover the whole radius and angle on the hit.
            // `tilted` (attacks 1-2): the water leans out of the hit thickness on purpose; the footprint on the ground stays the sector and nothing goes under it.
            void CheckSector(Transform drawn, Vector3 centre, Vector3 aim, float radius, float angle, float thickness, bool whole, float innerRadius = 0f, bool tilted = false)
            {
                Check(drawn != null && drawn.gameObject.activeSelf, "Expected sweep layer is not drawn.");
                string node = drawn.name;
                float reach = 0f, nearest = radius, left = 0f, right = 0f;
                foreach (var local in drawn.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    var offset = drawn.TransformPoint(local) - centre;
                    var flat = new Vector3(offset.x, 0f, offset.z);
                    float turn = Vector3.SignedAngle(aim, flat, Vector3.up);
                    Check(flat.magnitude <= radius + .001f, node + " draws past the hit radius: " + flat.magnitude);
                    if (innerRadius > 0f)
                        Check(flat.magnitude >= innerRadius - .001f, node + " draws inside the empty centre: " + flat.magnitude);
                    Check(Mathf.Abs(turn) <= angle * .5f + .05f, node + " draws past the hit angle: " + turn);
                    if (tilted) Check(centre.y + offset.y >= .03f && offset.y <= .85f, node + " tilts under the ground or too high: " + offset.y);
                    else Check(Mathf.Abs(offset.y) <= thickness * .5f + .001f, node + " leaves the hit thickness: " + offset.y);
                    reach = Mathf.Max(reach, flat.magnitude); nearest = Mathf.Min(nearest, flat.magnitude);
                    left = Mathf.Min(left, turn); right = Mathf.Max(right, turn);
                }
                if (whole) Check(reach > radius - .002f && left < -angle * .5f + .1f && right > angle * .5f - .1f,
                    node + " does not cover the hit sector on the hit: radius " + reach + ", angle " + left + ".." + right);
                if (whole && innerRadius > 0f)
                    Check(nearest < innerRadius + .002f, node + " does not reach the inner rim.");
            }
            var attack = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_1")).Data).Action.Perform;
            var sector = EveHitboxShape.Sector(attack["radius"].Value<float>(), attack["thickness"].Value<float>(), attack["angle"].Value<float>());
            SphereCollider SectorTarget(float degrees, float reach = 1.1f)
            {
                var targetObject = new GameObject("Sweep sector fixture target");
                SceneManager.MoveGameObjectToScene(targetObject, scene);
                targetObject.transform.position = Quaternion.AngleAxis(degrees, Vector3.up) * Vector3.right * reach;
                var collider = targetObject.AddComponent<SphereCollider>();
                collider.radius = .025f;
                return collider;
            }
            var insideSector = SectorTarget(106f);
            var outsideSector = SectorTarget(-135f);
            try
            {
                Physics.SyncTransforms();
                var targets = new System.Collections.Generic.List<Collider>();
                using var query = new EveHitboxQuery(sector, scene.GetPhysicsScene());
                query.Query(EveHitboxPose.Planar(Vector3.zero, Vector3.right), targets);
                Check(targets.Contains(insideSector) && !targets.Contains(outsideSector),
                    "The 230 degree hit sector failed the side and rear collider check.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(insideSector.gameObject);
                UnityEngine.Object.DestroyImmediate(outsideSector.gameObject);
            }

            var finisher = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_4")).Data).Action.Perform;
            Check(finisher["angle"].Value<float>() == 180f, "Gura's finisher must sweep 180 degrees.");
            var annulus = EveHitboxShape.Sector(finisher["radius"].Value<float>(), finisher["thickness"].Value<float>(),
                finisher["angle"].Value<float>(), finisher["innerRadius"].Value<float>());
            var finisherTargets = new[] { SectorTarget(-83f, 2.4f), SectorTarget(83f, 2.4f),
                SectorTarget(104f, 2.4f), SectorTarget(0f, 1.2f), SectorTarget(0f, 3.6f) };
            try
            {
                Physics.SyncTransforms();
                var targets = new System.Collections.Generic.List<Collider>();
                using var query = new EveHitboxQuery(annulus, scene.GetPhysicsScene());
                query.Query(EveHitboxPose.Planar(Vector3.zero, Vector3.right), targets);
                Check(targets.Contains(finisherTargets[0]) && targets.Contains(finisherTargets[1]) &&
                    finisherTargets.Skip(2).All(x => !targets.Contains(x)),
                    "The finisher must hit both sides and exclude the back, the inner hole and the far edge.");
            }
            finally
            {
                foreach (var probe in finisherTargets) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
            var rush = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_3")).Data).Action.Perform;
            var rushHits = (JArray)rush["hits"];
            Check(rush["angle"].Value<float>() == 360f && rushHits.Select(hit => hit["yawDegrees"].Value<float>())
                .SequenceEqual(new[] { 36f, 108f, 180f }), "Attack 3 must hit all around, on the first, third and fifth cut of its star.");
            Check(rush["step"] is JArray { Count: 2 } && rush["recoveryCancelAt"].Value<float>() >= rush["step"][1]["at"].Value<float>() + rush["step"][1]["duration"].Value<float>(),
                "Attack 3 must approach, then step back, before the next press may cut it.");
            var rushShape = EveHitboxShape.Sector(rush["radius"].Value<float>(), rush["thickness"].Value<float>(), rush["angle"].Value<float>());
            var rushTargets = new[] { SectorTarget(-155f, 1.05f), SectorTarget(0f, 1.05f), SectorTarget(155f, 1.05f), SectorTarget(90f, 1.45f) };
            try
            {
                Physics.SyncTransforms();
                var targets = new System.Collections.Generic.List<Collider>();
                using var query = new EveHitboxQuery(rushShape, scene.GetPhysicsScene());
                for (int i = 0; i < rushHits.Count; i++)
                {
                    var hitAim = Quaternion.AngleAxis(rushHits[i]["yawDegrees"].Value<float>(), Vector3.up) * Vector3.right;
                    targets.Clear();
                    query.Query(EveHitboxPose.Planar(Vector3.zero, hitAim), targets);
                    Check(rushTargets.Take(3).All(targets.Contains) && !targets.Contains(rushTargets[3]), "Attack 3 hit " + i + " is not the full circle of its radius.");
                }
            }
            finally
            {
                foreach (var probe in rushTargets) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
            // Where the body is during attack 3 in the fixture: the approach, the stay, the step back.
            Vector3 RushAt(float age) => Vector3.right * (age < .12f ? age / .12f * .5f : .5f - Mathf.InverseLerp(.64f, .80f, age) * 1.6f);

            var samples = new[]
            {
                ("attack_1", new[] { .042, .112, .17, .207, .265 }),
                ("attack_2", new[] { .043, .113, .17, .218, .265 }),
                ("attack_3", new[] { .02, .08, .16, .25, .34, .43, .52, .60, .66, .78, .86, .91, .97 }),
                ("attack_4", new[] { .07, .29, .32, .38 }),
                ("trident", new[] { .08, .20 }),
                ("shark", new[] { .020, .129, .165, .200, .275, .390, .485 }),
                ("dive", new[] { .70 }),
                ("dash", new[] { .045, .085, .125 }),
            };
            var skill = gura.GetComponent<PNGuraSkill>();
            Transform SharkRoot() => scene.GetRootGameObjects().Single(obj => obj.name == "Gura shark model").transform;
            foreach (var (state, ages) in samples)
            {
                var data = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_" + state)).Data);
                var definition = hub.Actions().Get(data.Action.Id);
                var view = definition.ViewFactory(new PNActionViewBuild(skill, data,Vector3.right,null,1));
                float weaponLength = 0f;
                Vector3 sharkAtBreach = default;
                Quaternion sharkOpenJaw = default;
                Vector3 sharkStepEnd = gura.transform.position + Vector3.right * .25f;
                bool sweep = state == "attack_1" || state == "attack_2";
                try
                {
                    for (int i = 0; i < ages.Length; i++)
                    {
                        var age = ages[i];
                        if (state == "dash") gura.transform.position = Vector3.right * (float)age * 8.5f;
                        if (state == "attack_3") gura.transform.position = RushAt((float)age);
                        if (i == 0 && state != "dive" && state != "dash") body.Actor.SetFacingLeft(true);
                        Vector3 positionBeforeSample = gura.transform.position;
                        if (state == "shark" && i == 1)
                            view.Fact(PNComboStrikeAction.StepEndFact, new double[] { sharkStepEnd.x, sharkStepEnd.y, sharkStepEnd.z }, .02);
                        if (state == "shark" && i == 2) gura.transform.position += Vector3.right * .30f;
                        view.Advance(age,1f,i==0);
                        if (state == "shark")
                        {
                            var sharkRoot = SharkRoot();
                            if (i == 0) Check(!sharkRoot.gameObject.activeSelf, "Shark breached before its windup.");
                            if (i == 1)
                            {
                                sharkAtBreach = sharkRoot.position;
                                Check(sharkStepEnd.x - sharkAtBreach.x > .2f && sharkStepEnd.x - sharkAtBreach.x < .6f && Mathf.Abs(sharkAtBreach.z - sharkStepEnd.z) < .001f,
                                    "Shark did not breach at the projected end of the approach.");
                            }
                            if (i == 2)
                            {
                                var offset = sharkRoot.position - sharkAtBreach;
                                // Aimed to the right: it comes up along the aim, nose nearly straight up and tipped towards it, mouth that way too.
                                Check(offset.y > 0f && Mathf.Abs(offset.z) < .001f && sharkRoot.forward.y > .93f && sharkRoot.forward.x > .15f,
                                    "Shark does not rise along the aim with a small lean: " + offset + " nose " + sharkRoot.forward);
                                Check(sharkRoot.up.x < -.9f, "The shark's mouth does not face the enemy: back " + sharkRoot.up);
                                Check(sharkRoot.localScale.x == sharkRoot.localScale.y && sharkRoot.localScale.y == sharkRoot.localScale.z,
                                    "Shark model proportions changed when scaled to the attack radius.");
                                Vector3 anchored = sharkRoot.position;
                                gura.transform.position = positionBeforeSample;
                                Check(sharkRoot.position == anchored, "Caster moved the shark after the view advanced.");
                                view.Advance(age, 1f, false);
                                sharkOpenJaw = sharkRoot.GetComponentInChildren<SkinnedMeshRenderer>().bones.Single(bone => bone.name == PNGuraSharkModel.JawJoint).localRotation;
                            }
                            if (i == 3)
                            {
                                float closing = Quaternion.Angle(sharkOpenJaw,
                                    sharkRoot.GetComponentInChildren<SkinnedMeshRenderer>().bones.Single(bone => bone.name == PNGuraSharkModel.JawJoint).localRotation);
                                Check(closing > 40f, "Shark did not snap its jaws shut at the hit: " + closing);
                                var ring = scene.GetRootGameObjects().Single(obj => obj.name == "Gura shark surface").transform.Find("Gura shark ripples");
                                float radius = ring.GetComponent<MeshFilter>().sharedMesh.vertices.Max(point =>
                                {
                                    var offset = ring.TransformPoint(point) - sharkStepEnd;
                                    return new Vector2(offset.x, offset.z).magnitude;
                                });
                                Check(radius > 1.5f && radius < 1.95f, "The shark's wave and shock do not cover the hit radius: " + radius);
                                var crest = ring.GetComponent<MeshFilter>().sharedMesh.vertices.Select(point => ring.TransformPoint(point) - sharkStepEnd)
                                    .Where(offset => offset.y > .15f).Select(offset => new Vector2(offset.x, offset.z).magnitude).ToArray();
                                Check(crest.Length > 50 && crest.Max() > 1.49f && crest.Max() < 1.65f, "The wave's crest does not stand on the hit radius.");
                            }
                        }
                        if (state == "dive")
                        {
                            Check(PNGuraSubmerge.Held(body), "Gura is not taken out of sight while she dives.");
                            var surface = scene.GetRootGameObjects().Single(obj => obj.name == "Gura dive surface").transform;
                            Check(surface.Find("Gura dive water").gameObject.activeSelf && SharkRoot().gameObject.activeSelf,
                                "The dive shows no water or no fin.");
                            var finSkin = SharkRoot().GetComponentInChildren<SkinnedMeshRenderer>();
                            var finBlock = new MaterialPropertyBlock();
                            finSkin.GetPropertyBlock(finBlock);
                            float line = gura.transform.position.y + .03f;
                            Check(finBlock.GetFloat("_Fin") == 1f && Mathf.Abs(finBlock.GetFloat("_Waterline") - line) < .001f,
                                "The dive does not cut the shark off at the water.");
                        }
                        if (i == 0 && state != "dive" && state != "dash") Check(!body.Actor.FacingLeft, state + " did not turn the puppet to its aim.");
                        body.Actor.Tick(1f/120f);
                        float framing = state == "attack_4" ? 2.2f : state == "shark" ? 1.78f : state == "dive" ? .68f : sweep ? 1.17f : .89f;
                        float centerX = state == "attack_4" ? 1.10f : state == "shark" ? .10f : state == "dash" ? .25f : .28f;
                        if (state == "attack_3") { framing = 1.5f; centerX = .45f; }
                        Capture(state + "-" + i, new Vector3(centerX,.34f,0), framing,gura);
                        if (sweep)
                        {
                            var shaft = gura.transform.Find("Gura attack trident").GetComponent<MeshFilter>().sharedMesh;
                            var vertices = shaft.vertices;
                            Vector3 sweepTip = vertices.OrderByDescending(v => (v - vertices[0]).sqrMagnitude).First();
                            float length = Vector3.Distance(sweepTip, vertices[0]);
                            if (i == 0)
                            {
                                weaponLength = length;
                                float side = state == "attack_1" ? 1f : -1f;
                                Check(shaft.vertexCount > 250 && vertices[0].x > .12f && sweepTip.x < vertices[0].x - .12f
                                    && sweepTip.y > vertices[0].y + .26f
                                    && (sweepTip.z - vertices[0].z) * side > .20f,
                                    "Sweep does not start with a forward grip and the tip winding back.");
                            }
                            else Check(Mathf.Abs(length - weaponLength) < .001f, "The trident changes length during " + state);
                            Check(shaft.bounds.min.y >= -.004f, "The downward trident goes through the ground.");
                            var perform = data.Action.Perform;
                            float radius = perform["radius"].Value<float>(), angle = perform["angle"].Value<float>(), thickness = perform["thickness"].Value<float>();
                            Vector3 centre = gura.transform.position + Vector3.up * perform["height"].Value<float>();
                            var crescent = gura.transform.Find("Gura slash bright");
                            if (i == 0) Check(crescent != null && !crescent.gameObject.activeSelf, "Water is drawn during the windup.");
                            else
                            {
                                CheckSector(crescent, centre, Vector3.right, radius, angle, thickness, true, 0f, true);
                                CheckSector(gura.transform.Find("Gura slash deep"), centre, Vector3.right, radius, angle, thickness, true, 0f, true);
                            }
                            if (i == 2)
                            {
                                Check(angle == 230f, "The skill sector is not 230 degrees.");
                                CheckSector(gura.transform.Find("Gura Water"), centre, Vector3.right, radius, angle, thickness, true, 0f, true);
                                // The foam is decoration (it may fly past the sector); it only has to be there on the hit.
                                Check(gura.transform.Find("Gura slash foam").gameObject.activeSelf, "No foam on the hit.");
                                float side = state == "attack_1" ? 1f : -1f;
                                var heights = crescent.GetComponent<MeshFilter>().sharedMesh.vertices;
                                Check(heights[0].y > heights[heights.Length - 1].y + .2f, "The water does not run from the raised side to the low side.");
                                Check(Vector3.SignedAngle(Vector3.right, new Vector3(heights[0].x, 0f, heights[0].z), Vector3.up) * side < 0f,
                                    "The swing turns the wrong way for " + state);
                            }
                        }
                        if (state == "attack_2" && i == 3)
                            Check(gura.transform.Find("Gura trident afterimage 0").gameObject.activeSelf, "Strike afterimage missing.");
                        if (state == "attack_3")
                        {
                            var p = data.Action.Perform;
                            float radius = p["radius"].Value<float>();
                            Vector3 anchor = RushAt(.12f);
                            int doubles = gura.transform.Cast<Transform>().Count(x => x.name.StartsWith("Gura water double ") && !x.name.EndsWith("trident") && x.gameObject.activeSelf);
                            int expected = age < .12 || age >= .88 ? 0 : age < .21 ? 1 : age < .30 ? 2 : age < .39 ? 3 : age < .48 ? 4 : 5;
                            Check(doubles == expected, "Attack 3 shows " + doubles + " doubles at " + age + ", expected " + expected);
                            Check(body.Actor.Renderer.enabled == (age < .035 || age >= .84), "Gura is shown/hidden wrongly at " + age);
                            var coat = body.Actor.transform.Find("Gura water coat");
                            if (age < .12 || age > .75 && age < .97)
                                Check(coat != null && coat.gameObject.activeSelf && ReferenceEquals(coat.GetComponent<MeshFilter>().sharedMesh, body.Actor.Mesh),
                                    "The water coat does not cover Gura's live silhouette at " + age);
                            var cuts = gura.transform.Cast<Transform>().Where(x => x.name == "Gura slash bright" && x.gameObject.activeSelf).ToArray();
                            int shown = age < .175 ? 0 : age < .265 ? 1 : age < .355 ? 2 : age < .445 ? 3 : age < .535 ? 4 : age < .84 ? 5 : -1;
                            if (shown >= 0) Check(cuts.Length == shown, "Attack 3 shows " + cuts.Length + " cuts at " + age + ", expected " + shown);
                            foreach (var drawn in cuts)
                                Check(drawn.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => drawn.TransformPoint(v) - anchor)
                                    .All(v => new Vector2(v.x, v.z).magnitude <= radius + .02f && v.y > .02f), "An attack 3 cut leaves the hit circle at " + age);
                        }
                        if (state == "attack_4" && i == 2)
                        {
                            var p = data.Action.Perform;
                            var centre = gura.transform.position + Vector3.up * p["height"].Value<float>();
                            foreach (string node in new[] { "Gura slash bright", "Gura slash deep" })
                                CheckSector(gura.transform.Find(node), centre, Vector3.right, p["radius"].Value<float>(),
                                    p["angle"].Value<float>(), p["thickness"].Value<float>(), true, p["innerRadius"].Value<float>());
                            var wave = gura.transform.Find("Gura finisher water accents");
                            Check(wave.gameObject.activeSelf && wave.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => wave.TransformPoint(v) - centre)
                                .All(v => new Vector2(v.x, v.z).magnitude <= p["radius"].Value<float>() * 1.25f && v.y < 1.5f),
                                "The finisher's wave is missing or strays far past its sector.");
                        }
                        if (state == "shark" && i == 2)
                        {
                            var rigged = SharkRoot().GetComponentInChildren<SkinnedMeshRenderer>();
                            Check(rigged != null && rigged.bones.Length == 29, "Ultimate shark does not use the imported rig.");
                            var skin = new MaterialPropertyBlock();
                            rigged.GetPropertyBlock(skin);
                            Check(skin.GetFloat("_WaterSkin") == 2f && rigged.sharedMaterials.Length == 2 && rigged.sharedMaterials[0].GetFloat("_DepthWrite") == 1f,
                                "Ultimate does not draw the water shark over its own depth copy.");
                            var renderer = camera.GetUniversalAdditionalCameraData();
                            renderer.SetRenderer(1);
                            try
                            {
                                Capture("shark-3d",new Vector3(centerX,.34f,0),framing,gura);
                                Check(output.GetPixels32().Count(pixel => pixel.b > 100 && pixel.b > pixel.r + 30) > 1800,
                                    "The water shark does not render in the URP 3D renderer.");
                            }
                            finally { renderer.SetRenderer(0); }
                        }
                    }
                    if (state == "trident" || state == "shark")
                    {
                        int count = state == "trident" ? 15 : 30;
                        for (int frame = 0; frame < count; frame++)
                        {
                            view.Advance(frame / 60d, 1d, frame == 0);
                            body.Actor.Tick(1f / 60f);
                            Capture($"{state}-motion-{frame:D2}", Vector3.up * .48f + Vector3.right * .20f, state == "trident" ? .92f : 1.86f, gura);
                        }
                    }
                    view.Hide();
                    if (state == "shark") Check(!SharkRoot().gameObject.activeSelf, "Shark retained visible graphics after Hide.");
                    if (state == "dive") Check(!SharkRoot().gameObject.activeSelf && !PNGuraSubmerge.Held(body), "A hidden dive keeps its fin or keeps Gura out of sight.");
                    Check(!gura.transform.Cast<Transform>().Any(x => (x.name.StartsWith("Gura ") || x.name.EndsWith("(Gura)")) && x.gameObject.activeSelf),
                        state + " retained visible graphics after Hide.");
                    if (state == "attack_3")
                    {
                        view.Advance(.5, 1d, true);
                        Check(!body.Actor.Renderer.enabled, "Attack 3 did not hide Gura again after Hide.");
                        view.End(PNActionViewEnd.Cancelled);
                        Check(body.Actor.Renderer.enabled && body.Actor.transform.Find("Gura water coat") == null
                            && !gura.transform.Cast<Transform>().Any(x => x.name.StartsWith("Gura water double")), "Cancelling attack 3 left Gura hidden or its doubles behind.");
                    }
                }
                finally { view.End(PNActionViewEnd.Cancelled); gura.transform.position = Vector3.zero; }
            }

            // The Dash as it is played: it slides, ends, and its picture goes on by itself.
            {
                var data = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_dash")).Data);
                var definition = hub.Actions().Get(data.Action.Id);
                bool Left() => gura.GetComponentsInChildren<Transform>(true).Any(x => x.name == "Gura Dash Water" || x.name.StartsWith("Gura dash "));
                MeshRenderer Flash() => gura.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x => x.name == "Gura dash flash");
                var view = definition.ViewFactory(new PNActionViewBuild(skill, data, Vector3.right, null, 1d));
                PNGuraDashVisual dash = null;
                try
                {
                    body.Actor.SetFacingLeft(false);
                    for (int frame = 0; frame < 33; frame++)
                    {
                        float age = frame / 60f;
                        if (age <= .16f)
                        {
                            gura.transform.position = Vector3.right * Mathf.Min(1.5f, age / .15f * 1.5f);
                            view.Advance(age, 1d, frame == 0);
                            dash = gura.GetComponentInChildren<PNGuraDashVisual>();
                        }
                        else if (!view.HasTail && dash != null && !dash.Released)
                        {
                            view.End(PNActionViewEnd.Completed);
                            Check(view.HasTail && dash.Released, "The finished Dash kept no tail.");
                        }
                        if (dash != null && dash.Released) dash.Tick(1f / 60f);
                        body.Actor.Tick(1f / 60f);
                        if (frame == 8)
                            Check(dash != null && dash.GhostCount == 3 && Flash() != null && Flash().gameObject.activeSelf
                                && dash.GetComponentsInChildren<MeshFilter>().Any(x => x.name == "Gura dash streak" && x.sharedMesh.vertexCount > 40),
                                "A sliding Dash must show the water flash, three after-images and its line.");
                        Capture($"dash-motion-{frame:D2}", new Vector3(.72f, .30f, 0f), 1.15f, gura);
                    }
                    Check(dash == null && !view.HasTail && !Left(), "The Dash picture did not remove itself.");
                }
                finally { view.End(PNActionViewEnd.Cancelled); gura.transform.position = Vector3.zero; }
                {
                    // A slow frame: the first look at the run comes half way along.
                    var late = definition.ViewFactory(new PNActionViewBuild(skill, data, Vector3.right, null, 1d));
                    gura.transform.position = Vector3.right * 1.0f;
                    late.Advance(.107, 1d, true);
                    var slide = gura.GetComponentInChildren<PNGuraDashVisual>();
                    var images = slide.GetComponentsInChildren<MeshRenderer>().Where(x => x.name == "Gura dash ghost").Select(x => x.transform.position.x).OrderBy(x => x).ToArray();
                    Check(slide.GhostCount == 3 && images[2] - images[0] > .8f, "A Dash first seen half way must still leave its images along the way.");
                    late.End(PNActionViewEnd.Cancelled);
                    gura.transform.position = Vector3.zero;
                }
                foreach (bool completed in new[] { false, true })
                {
                    var cut = definition.ViewFactory(new PNActionViewBuild(skill, data, Vector3.right, null, 1d));
                    cut.Advance(0, 1d, true);
                    gura.transform.position = Vector3.right * .7f; cut.Advance(.07, 1d, false);
                    if (completed) cut.End(PNActionViewEnd.Completed);
                    cut.End(PNActionViewEnd.Cancelled);
                    Check(!cut.HasTail && !Left(), completed ? "Teardown left the tail of a Dash." : "A cancelled Dash left water behind.");
                    gura.transform.position = Vector3.zero;
                }
            }

            for (int beat = 1; beat <= 2; beat++)
            {
                bool facingLeft = beat == 2;
                Vector3 aim = facingLeft ? Vector3.left : Vector3.right;
                var data = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_" + beat)).Data);
                var definition = hub.Actions().Get(data.Action.Id);
                var view = definition.ViewFactory(new PNActionViewBuild(skill, data, aim, null, 1d));
                body.Actor.SetFacingLeft(!facingLeft);
                try
                {
                    for (int frame = 0; frame < 27; frame++)
                    {
                        view.Advance(frame / 60d, 1d, frame == 0);
                        body.Actor.Tick(1f/60f);
                        Check(body.Actor.FacingLeft == facingLeft, "The puppet did not face its attack aim.");
                        Capture($"motion-{beat}-{frame:D2}", aim * .16f + Vector3.up*.57f, 1.39f, gura);
                    }
                    // The same run seen from straight above, with the hit sector itself drawn over it.
                    var perform = data.Action.Perform;
                    float radius = perform["radius"].Value<float>(), angle = perform["angle"].Value<float>(), height = perform["height"].Value<float>();
                    var outline = new GameObject("Hit sector outline") { layer = 31 };
                    SceneManager.MoveGameObjectToScene(outline, scene);
                    var line = outline.AddComponent<LineRenderer>();
                    var lineMaterial = new Material(Shader.Find("Sprites/Default"));
                    try
                    {
                        line.sharedMaterial = lineMaterial; line.widthMultiplier = .014f; line.useWorldSpace = true; line.loop = true; line.sortingOrder = 50;
                        line.startColor = line.endColor = new Color(1f, .35f, .2f);
                        line.positionCount = 66; line.SetPosition(0, Vector3.up * height);
                        for (int i = 0; i <= 64; i++)
                            line.SetPosition(i + 1, Quaternion.AngleAxis(Mathf.Lerp(-angle * .5f, angle * .5f, i / 64f), Vector3.up) * aim * radius + Vector3.up * height);
                        foreach (var (tag, age) in new[] { ("swing", .14f), ("hit", .171f), ("after", .23f), ("fade", .33f) })
                        {
                            view.Advance(age, 1d, true);
                            body.Actor.Tick(1f / 60f);
                            foreach (var t in gura.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                            camera.orthographicSize = 1.9f;
                            camera.transform.SetPositionAndRotation(Vector3.up * 8f, Quaternion.Euler(90f, 0f, 0f));
                            camera.Render(); RenderTexture.active = target;
                            output.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); output.Apply();
                            File.WriteAllBytes(Path.Combine(outputDir, $"top-{beat}-{tag}.png"), output.EncodeToPNG());
                        }
                        // The same hit over a light ground, where the additive layer has the least room.
                        var dark = camera.backgroundColor;
                        camera.backgroundColor = new Color(.62f, .70f, .52f);
                        outline.SetActive(false);
                        view.Advance(.185f, 1d, true);
                        body.Actor.Tick(1f / 60f);
                        Capture($"light-{beat}", aim * .16f + Vector3.up * .57f, 1.39f, gura);
                        camera.backgroundColor = dark;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(lineMaterial); UnityEngine.Object.DestroyImmediate(outline); }
                }
                finally { view.End(PNActionViewEnd.Cancelled); body.Actor.SetFacingLeft(false); }
            }

            {
                var data = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_3")).Data);
                var view = hub.Actions().Get(data.Action.Id).ViewFactory(new PNActionViewBuild(skill, data, Vector3.right, null, 1d));
                var outline = new GameObject("Hit circle outline") { layer = 31 };
                SceneManager.MoveGameObjectToScene(outline, scene);
                var line = outline.AddComponent<LineRenderer>();
                var lineMaterial = new Material(Shader.Find("Sprites/Default"));
                try
                {
                    line.sharedMaterial = lineMaterial; line.widthMultiplier = .014f; line.useWorldSpace = true; line.loop = true; line.sortingOrder = 50;
                    line.startColor = line.endColor = new Color(1f, .35f, .2f);
                    line.positionCount = 64;
                    float radius = data.Action.Perform["radius"].Value<float>();
                    for (int i = 0; i < 64; i++)
                        line.SetPosition(i, RushAt(.12f) + Quaternion.AngleAxis(i * 360f / 64f, Vector3.up) * Vector3.forward * radius + Vector3.up * .03f);
                    outline.SetActive(false);
                    for (int frame = 0; frame < 60; frame++)
                    {
                        float age = frame / 60f;
                        gura.transform.position = RushAt(age);
                        view.Advance(age, 1d, frame == 0);
                        body.Actor.Tick(1f / 60f);
                        Capture($"echo-motion-{frame:D2}", new Vector3(.28f, .36f, 0f), 1.5f, gura);
                    }
                    var dark = camera.backgroundColor;
                    foreach (var (tag, age) in new[] { ("first", .24f), ("all", .66f), ("burst", .80f), ("form", .85f) })
                    {
                        gura.transform.position = RushAt(age);
                        view.Advance(age, 1d, true);
                        // A seek keeps no memory of the approach: ask again from the stay to place the circle.
                        gura.transform.position = RushAt(.5f); view.Advance(.5, 1d, true);
                        gura.transform.position = RushAt(age); view.Advance(age, 1d, false);
                        body.Actor.Tick(1f / 60f);
                        camera.backgroundColor = new Color(.62f, .70f, .52f);
                        Capture("echo-light-" + tag, new Vector3(.28f, .36f, 0f), 1.5f, gura);
                        camera.backgroundColor = dark;
                        outline.SetActive(true);
                        foreach (var t in gura.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                        camera.orthographicSize = 1.7f;
                        camera.transform.SetPositionAndRotation(RushAt(.12f) + Vector3.up * 8f, Quaternion.Euler(90f, 0f, 0f));
                        camera.Render(); RenderTexture.active = target;
                        output.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); output.Apply();
                        File.WriteAllBytes(Path.Combine(outputDir, $"echo-top-{tag}.png"), output.EncodeToPNG());
                        outline.SetActive(false);
                    }
                    var urpData = camera.GetUniversalAdditionalCameraData();
                    urpData.SetRenderer(1);
                    try
                    {
                        gura.transform.position = RushAt(.66f); view.Advance(.66, 1d, false); body.Actor.Tick(1f / 60f);
                        Capture("echo-3d-all", new Vector3(.28f, .36f, 0f), 1.5f, gura);
                        gura.transform.position = RushAt(.08f); view.Advance(.08, 1d, true); body.Actor.Tick(1f / 60f);
                        Capture("echo-3d-melt", new Vector3(.28f, .36f, 0f), 1.5f, gura);
                    }
                    finally { urpData.SetRenderer(0); }
                }
                finally
                {
                    view.End(PNActionViewEnd.Cancelled); gura.transform.position = Vector3.zero;
                    UnityEngine.Object.DestroyImmediate(lineMaterial); UnityEngine.Object.DestroyImmediate(outline);
                }
                Check(body.Actor.Renderer.enabled, "Gura stayed hidden after attack 3.");
                var shadowless = gura.GetComponentInChildren<PNGroundShadow>(true);
                Check(shadowless == null || !shadowless.Suppressed, "Gura's ground shadow stayed off after attack 3.");
            }

            // A skill that turns the body turns the puppet too.
            {
                var turner = gura.GetComponent<PNPlayerMove>();
                turner.FaceSkillDirection(Vector2.left);
                Check(body.Actor.FacingLeft, "A skill aimed left did not turn the puppet.");
                turner.FaceSkillDirection(Vector2.right);
                Check(!body.Actor.FacingLeft, "A skill aimed right did not turn the puppet.");
            }

            var move = gura.GetComponent<PNPlayerMove>();
            var advance = typeof(PNSkill).GetMethod("AdvanceBindings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int previousStarts = skill.ActionViewsStarted;
            int previousFailures = skill.ActionViewFailures;
            move.SetMoveDirection(Vector2.left);
            body.Actor.SetFacingLeft(false);
            var started = new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap);
            var released = new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap);
            var rigidBody = gura.GetComponent<Rigidbody>();
            Check(move.CanMove && advance != null, "Gura could not move before the combo.");
            for (int beat = 1; beat <= 4; beat++)
            {
                skill.RequestSlot(PNSkillSlots.Attack, released);
                Check(skill.RequestSlot(PNSkillSlots.Attack, started) && !move.CanMove, "Combo beat " + beat + " did not lock manual movement.");
                move.SetMoveDirection(Vector2.left);
                move.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
                Check(Mathf.Abs(rigidBody.linearVelocity.x) < .001f, "Manual input moved Gura during beat " + beat);
                int recoveryFrame = beat == 3 ? 55 : beat == 4 ? 21 : 12;
                for (int frame = 0; frame < recoveryFrame; frame++) advance.Invoke(skill, new object[] { 1f / 60f });
                Check(!move.CanMove, "The movement lock released during combo recovery.");
            }
            for (int frame = 0; frame < 60; frame++) advance.Invoke(skill, new object[] { 1f / 60f });
            Check(move.CanMove, "Completing a combo left Gura locked.");
            skill.RequestSlot(PNSkillSlots.Attack, released);
            Check(skill.RequestSlot(PNSkillSlots.Attack, started) && !move.CanMove, "Gura did not lock again on the next Attack.");
            skill.CancelBindingExecution();
            skill.RequestSlot(PNSkillSlots.Attack, released);
            Check(move.CanMove, "Cancelling the last hit left Gura locked.");
            skill.Abilities.GetCounter(PNGuraContent.ComboStep).SetAmount(0);
            move.SetMoveDirection(Vector2.left);
            move.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
            Check(rigidBody.linearVelocity.x < -.01f, "Holding the direction did not resume walking after cancellation.");
            rigidBody.linearVelocity = Vector3.zero;
            previousStarts = skill.ActionViewsStarted;
            previousFailures = skill.ActionViewFailures;
            Check(advance != null && skill.RequestSlot(PNSkillSlots.Attack, started), "The actual Attack binding did not accept the press.");
            try
            {
                for (int i = 0; i < 12; i++)
                    advance.Invoke(skill, new object[] { 1f / 60f });
                Check(skill.ActionViewsStarted == previousStarts + 1 && skill.ActionViewFailures == previousFailures
                    && body.Actor.FacingLeft && gura.transform.Find("Gura Water").gameObject.activeSelf,
                    "The actual Attack binding did not produce the left-facing sweep view.");
                body.Actor.Tick(1f/60f);
                var renderer = camera.GetUniversalAdditionalCameraData();
                renderer.SetRenderer(1);
                try
                {
                    Capture("binding-slash-3d", Vector3.left*.18f + Vector3.up*.57f, 1.14f, gura);
                    int beyond = (int)(camera.WorldToViewportPoint(Vector3.left * .58f).x * output.width);
                    Check(output.GetPixels32().Where((pixel,index) => index % output.width < beyond
                            && pixel.g > 95 && pixel.b > pixel.r + 25).Take(45).Count() == 45,
                        "Sweep trail is too faint in the 3D-renderer Attack frame.");
                }
                finally { renderer.SetRenderer(0); }
            }
            finally
            {
                skill.CancelBindingExecution();
                skill.RequestSlot(PNSkillSlots.Attack, released);
                move.SetMoveDirection(Vector2.zero);
                body.Actor.SetFacingLeft(false);
            }

            // Exercise both new views through the regular Attack binding, using the combo counter only to select the beat under test.
            foreach (var (beat, left, primary) in new[] { (3, false, true), (4, true, true), (3, true, false), (4, false, false) })
            {
                string tag = (primary ? "advanced-" : "opposite-") + beat;
                Vector3 aim = left ? Vector3.left : Vector3.right;
                int hitFrame = beat == 3 ? 36 : 20;
                int frames = beat == 3 ? 64 : 48;
                float framing = beat == 3 ? 1.08f : 2.2f;
                Vector3 offset = aim * (beat == 3 ? .43f : 1.07f) + Vector3.up * .32f;
                var p = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_" + beat)).Data).Action.Perform;
                float radius = p["radius"].Value<float>(), angle = p["angle"].Value<float>();
                float inner = p["innerRadius"]?.Value<float>() ?? 0f, height = p["height"].Value<float>();
                var targetObject = new GameObject("Gura combo fixture target");
                targetObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(targetObject, scene);
                targetObject.layer = PNLayers.Enemy;
                targetObject.transform.position = aim * (beat == 3 ? 2.15f : .86f);
                targetObject.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(930423), EveId.Parse("project_n:dummy"), 0);
                targetObject.AddComponent<PNEnemyStat>();
                targetObject.AddComponent<CapsuleCollider>().radius = .18f;
                var targetLoader = targetObject.AddComponent<EveEntityLoader>();
                targetLoader.Load(hub);
                targetObject.SetActive(true);
                Physics.SyncTransforms();
                skill.Abilities.GetCounter(PNGuraContent.ComboStep).SetAmount(beat - 1);
                skill.Abilities.GrantState(PNGuraContent.ComboWindow);
                move.SetMoveDirection(new Vector2(aim.x, aim.z));
                body.Actor.SetFacingLeft(!left);
                previousStarts = skill.ActionViewsStarted;
                previousFailures = skill.ActionViewFailures;
                Check(skill.RequestSlot(PNSkillSlots.Attack, started), tag + " input was rejected.");
                var renderer = camera.GetUniversalAdditionalCameraData();
                var dark = camera.backgroundColor;
                try
                {
                    for (int frame = 1; frame <= frames; frame++)
                    {
                        advance.Invoke(skill, new object[] { 1f / 60f });
                        body.Actor.Tick(1f / 60f);
                        Vector3 center = gura.transform.position + offset;
                        // A fixed camera during the dash shows the lunge instead of recentering the player every frame.
                        Capture($"{tag}-{frame:D2}", beat == 3 ? offset + aim * .52f : center, beat == 3 ? 1.75f : framing, gura);
                        if (beat == 3 && frame == frames)
                        {
                            float away = Vector3.Distance(Vector3.ProjectOnPlane(gura.transform.position, Vector3.up), Vector3.ProjectOnPlane(targetObject.transform.position, Vector3.up));
                            Check(body.Actor.Renderer.enabled && away > 2f && away < 2.4f, tag + " did not reform Gura at the finisher's distance: " + away);
                        }
                        if (frame != hitFrame) continue;
                        Check(skill.ActionViewsStarted == previousStarts + 1 && skill.ActionViewFailures == previousFailures
                            && body.Actor.FacingLeft == left, tag + " did not bind and aim correctly.");
                        if (beat == 3)
                        {
                            Check(Vector3.Dot(gura.transform.position, aim) > .8f, "The rush fixture did not lunge toward its target.");
                            Check(!body.Actor.Renderer.enabled && gura.transform.Find("Gura water double 4") != null && gura.transform.Find("Gura water double 4").gameObject.activeSelf,
                                "The real attack 3 does not show its five doubles with Gura gone.");
                        }
                        else Check(Vector3.Dot(gura.transform.position, aim) < -.15f, "The movement lock blocked the finisher retreat.");
                        Vector3 hitCentre = gura.transform.position + Vector3.up * height;
                        float yaw = p["hits"][0]["yawDegrees"]?.Value<float>() ?? 0f;
                        var hitAim = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
                        Vector3 colorProbe = hitCentre + (beat == 3 ? aim * .55f : hitAim * Mathf.Lerp(inner, radius, .78f));
                        for (int mode = 0; mode <= 1; mode++)
                        {
                            renderer.SetRenderer(mode);
                            Capture($"{tag}-renderer-{mode}", center, framing, gura);
                            var probe = camera.WorldToScreenPoint(colorProbe);
                            var pixels = output.GetPixels32();
                            int blue = pixels.Where((c, index) => Mathf.Abs(index % output.width - probe.x) < 40
                                && Mathf.Abs(index / output.width - probe.y) < 32 && c.b > 96 && c.g > 64 && c.b > c.r + 24).Take(40).Count();
                            Check(blue == 40, tag + " water body is too faint in renderer " + mode);
                        }
                        renderer.SetRenderer(0);
                        camera.backgroundColor = new Color(.62f, .70f, .52f);
                        Capture(tag + "-light", center, framing, gura);
                        camera.backgroundColor = dark;

                        var outline = new GameObject("Hit sector outline") { layer = 31 };
                        SceneManager.MoveGameObjectToScene(outline, scene);
                        var line = outline.AddComponent<LineRenderer>();
                        var lineMaterial = new Material(Shader.Find("Sprites/Default"));
                        try
                        {
                            line.sharedMaterial = lineMaterial;
                            line.widthMultiplier = .016f;
                            line.useWorldSpace = true;
                            line.loop = true;
                            line.sortingOrder = 50;
                            line.startColor = line.endColor = new Color(1f, .35f, .2f);
                            line.positionCount = 130;
                            Vector3 floor = hitCentre + Vector3.up * p["thickness"].Value<float>() * .5f;
                            for (int i = 0; i <= 64; i++)
                            {
                                Vector3 outward = Quaternion.AngleAxis(Mathf.Lerp(-angle * .5f, angle * .5f, i / 64f), Vector3.up) * hitAim;
                                line.SetPosition(i, floor + outward * radius);
                                line.SetPosition(129 - i, floor + outward * inner);
                            }
                            camera.orthographicSize = Mathf.Max(framing, radius * Mathf.Sin(Mathf.Min(90f, angle * .5f) * Mathf.Deg2Rad) + .25f);
                            camera.transform.SetPositionAndRotation(center + Vector3.up * 8f, Quaternion.Euler(90f, 0f, 0f));
                            camera.Render(); RenderTexture.active = target;
                            output.ReadPixels(new Rect(0,0,960,540),0,0); output.Apply();
                            File.WriteAllBytes(Path.Combine(outputDir, tag + "-top.png"), output.EncodeToPNG());
                        }
                        finally { UnityEngine.Object.DestroyImmediate(lineMaterial); UnityEngine.Object.DestroyImmediate(outline); }
                    }
                }
                finally
                {
                    camera.backgroundColor = dark;
                    renderer.SetRenderer(0);
                    skill.CancelBindingExecution();
                    skill.RequestSlot(PNSkillSlots.Attack, released);
                    move.SetMoveDirection(Vector2.zero);
                    body.Actor.SetFacingLeft(false);
                    gura.GetComponent<Rigidbody>().position = Vector3.zero;
                    gura.transform.position = Vector3.zero;
                    targetLoader.Unload();
                    UnityEngine.Object.DestroyImmediate(targetObject);
                }
            }

            var castData = PNSkillData.Parse(hub.Skills.Get(PNGuraContent.TridentSkill).Data);
            var config = (JObject)castData.Action.Perform;
            // The Skill no longer ends in a blast; the registered trident blast is still checked with the settings the Skill used.
            var splashConfig = JObject.Parse("{'blastId':'project_n:gura_trident_splash','input':{'delay':0.05,'tail':0.75,'radius':2.0,'height':1.5,'value':{'amount':200,'statId':'base:attack_power'},'canCrit':true,'tags':['base:physical','base:skill']}}");
            move.SetMoveDirection(Vector2.right);
            Check(skill.RequestSlot(PNSkillSlots.Skill, started) && !move.CanMove, "Skill windup must lock walking.");
            move.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
            Check(Mathf.Abs(rigidBody.linearVelocity.x) < .001f, "Skill windup let held movement through.");
            skill.CancelBindingExecution();
            skill.RequestSlot(PNSkillSlots.Skill, released);
            Check(move.CanMove, "Canceling the Skill did not release walking.");
            skill.Abilities.GetCooldown(PNGuraContent.SkillReady).ResetToFull();
            move.SetMoveDirection(Vector2.right);
            Check(skill.RequestSlot(PNSkillSlots.Skill, started) && !move.CanMove, "Skill did not relock when cast again.");
            for (int frame = 0; frame < 185; frame++)
            {
                projectiles.Advance(1f / 60f);
                blasts.Advance(1f / 60f);
                advance.Invoke(skill, new object[] { 1f / 60f });
                if (frame == 17) Check(!move.CanMove, "Skill unlocked before its projectile phase ended.");
            }
            Check(move.CanMove, "Skill completion did not release walking.");
            skill.RequestSlot(PNSkillSlots.Skill, released);
            // Each form has its own cooldown and the slot answers only when both are ready.
            var throwReady = skill.Abilities.GetCooldown(PNGuraContent.SkillReady);
            var meleeReady = skill.Abilities.GetCooldown(PNGuraContent.MeleeReady);
            throwReady.ResetToFull(); meleeReady.ResetToFull();
            Check(skill.RequestSlot(PNSkillSlots.Skill, started), "Skill refused with both cooldowns ready.");
            Check(!throwReady.CanConsume() && meleeReady.CanConsume(), "A throw must spend the throw cooldown only.");
            skill.CancelBindingExecution();
            skill.RequestSlot(PNSkillSlots.Skill, released);
            throwReady.ResetToFull();
            Check(meleeReady.TryConsume(), "The near cooldown could not be spent.");
            bool answered = skill.RequestSlot(PNSkillSlots.Skill, started);
            Check(move.CanMove && throwReady.CanConsume(), "Skill cast while the near cooldown ran: answered=" + answered + " move=" + move.CanMove + " throwReady=" + throwReady.CanConsume());
            skill.RequestSlot(PNSkillSlots.Skill, released);
            meleeReady.ResetToFull(); throwReady.ResetToFull();

            // The near form: nine hits, a dash to each side of the enemy, then the hop back while two flying slashes are let go.
            {
                var registered = hub.Skills.Get(EveId.Parse("project_n:gura_vortex")).Data as JObject;
                Check(registered != null, "The near form's data is not a JSON object.");
                var vortex = (JObject)registered.DeepClone();
                void Refused(string why, Action<JObject> change)
                {
                    var copy = (JObject)vortex.DeepClone();
                    change((JObject)copy["action"]["perform"]);
                    bool refused = false;
                    try { PNComboStrikeAction.Validate(PNSkillData.Parse(copy).Action, hub); }
                    catch (FormatException) { refused = true; }
                    Check(refused, "The combo strike accepted " + why + ".");
                }
                var perform = (JObject)vortex["action"]["perform"];
                Check(((JArray)perform["hits"]).Count == 9 && Mathf.Approximately(perform["radius"].Value<float>(), 3.5f), "The near form must carry nine hits of radius 3.5.");
                Check(((JArray)perform["shots"]).Count == 2, "The near form must let two slashes fly.");
                PNComboStrikeAction.Validate(PNSkillData.Parse(vortex).Action, hub);
                Refused("a bearing past 180", x => x["step"][0]["aroundDegrees"] = 181);
                Refused("a bearing with a retreat", x => x["step"][0]["retreat"] = true);
                Refused("too many hits", x => { var list = (JArray)x["hits"]; while (list.Count <= PNComboStrikeAction.MaxHits) list.Add(list[list.Count - 1].DeepClone()); });
                Refused("shots out of order", x => x["shots"][1]["at"] = 1.0);
                Refused("a shot past the end", x => x["shots"][1]["at"] = 9.0);
                Refused("too many shots", x => { var list = (JArray)x["shots"]; while (list.Count <= PNComboStrikeAction.MaxShots) list.Add(list[list.Count - 1].DeepClone()); });
                Refused("a slash leaning past 80 degrees", x => x["shots"][0]["projectile"]["input"]["tilt"] = 120);

                var home = gura.transform.position;
                var nearObject = new GameObject("Gura near fixture target");
                nearObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(nearObject, scene);
                nearObject.layer = PNLayers.Enemy;
                Vector3 foe = new Vector3(1.5f, 0f, 0f);
                nearObject.transform.position = foe;
                nearObject.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(930424), EveId.Parse("project_n:dummy"), 0);
                nearObject.AddComponent<PNEnemyStat>();
                nearObject.AddComponent<CapsuleCollider>().radius = .18f;
                var nearLoader = nearObject.AddComponent<EveEntityLoader>();
                nearLoader.Load(hub);
                nearObject.SetActive(true);
                rigidBody.position = Vector3.zero; gura.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                move.SetMoveDirection(Vector2.right);
                try
                {
                    Check(skill.RequestSlot(PNSkillSlots.Skill, started) && !move.CanMove, "The near form was not cast.");
                    Vector3 Offset() => Vector3.ProjectOnPlane(gura.transform.position - foe, Vector3.up);
                    var seen = new System.Collections.Generic.List<string>();
                    var waves = new System.Collections.Generic.HashSet<PNGuraSlashWave>();
                    var struck = new System.Collections.Generic.HashSet<PNGuraSlashWave>();
                    var hitCount = typeof(PNProjectileInstance).GetProperty("HitCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    for (int frame = 1; frame <= 176; frame++)
                    {
                        advance.Invoke(skill, new object[] { 1f / 60f });
                        body.Actor.Tick(1f / 60f);
                        projectiles.Advance(1f / 60f);
                        foreach (var wave in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNGuraSlashWave>(true)).Where(x => x.IsRunning))
                        {
                            waves.Add(wave);
                            if (Convert.ToInt32(hitCount.GetValue(wave)) > 0) struck.Add(wave);
                        }
                        if (frame == 13) Check(Offset().z < -.8f && Mathf.Abs(Offset().x) < .3f, "The first dash did not end on the right of the enemy: " + Offset());
                        if (frame == 31) Check(Offset().z > .8f && Mathf.Abs(Offset().x) < .3f, "The flight did not end on the left of the enemy: " + Offset());
                        if (frame % 20 == 0 && (frame >= 60 && frame <= 120)) seen.Add(gura.transform.Find("Gura water double 0") != null ? "shadow" : "none");
                    }
                    Check(move.CanMove, "The near form did not finish.");
                    Check(waves.Count == 2 && struck.Count == 2, "The two flying slashes were not let go through the enemy: " + waves.Count + " flew, " + struck.Count + " struck.");
                    Check(seen.Count == 4 && seen.All(x => x == "shadow"), "The near form shows no shadows: " + string.Join(",", seen));
                    float away = Offset().magnitude;
                    Check(away > 1.6f && away < 2f && Offset().x < -1.5f, "The hop back did not leave her 1.8 m in front of the enemy: " + Offset());
                    // The slashes live 3 s: let them run out before the other projectile checks.
                    for (int frame = 0; frame < 200; frame++) projectiles.Advance(1f / 60f);
                    Check(!PNGuraSubmerge.Held(body), "Gura stayed hidden after the near form.");
                }
                finally
                {
                    skill.CancelBindingExecution();
                    skill.RequestSlot(PNSkillSlots.Skill, released);
                    move.SetMoveDirection(Vector2.zero);
                    rigidBody.position = home; gura.transform.position = home;
                    nearLoader.Unload();
                    UnityEngine.Object.DestroyImmediate(nearObject);
                    throwReady.ResetToFull(); meleeReady.ResetToFull();
                    Physics.SyncTransforms();
                }
            }
            var projectileId = EveId.Parse(config["projectile"]["projectileId"].Value<string>());
            var blastId = EveId.Parse(splashConfig["blastId"].Value<string>());
            var combat = PNCombatSource.Capture(stat);
            var shot = projectiles.Create(
                new PNProjectileLaunch(projectileId,config["projectile"]["input"],combat,stat.TargetLayers),
                new PNProjectileSpawn(scene,new Vector3(0,.25f,0),Vector3.right));
            Check(shot.Status == PNProjectileStatus.Created,"Trident spawn failed "+shot.Status+" "+shot.Error);
            var instance = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PNGuraThrownTrident>(true)).Single(x=>x.IsRunning);
            for (int frame = 0; frame < 8; frame++)
            {
                projectiles.Advance(1f / 60f);
                instance.SendMessage("LateUpdate",SendMessageOptions.RequireReceiver);
                Capture($"flight-motion-{frame:D2}", new Vector3(.99f,.26f,0f), 1.20f, instance.gameObject);
            }
            var head = instance.transform.Find("Gura thrown trident").GetComponent<MeshFilter>();
            var ribbon = instance.transform.Find("Gura Water").GetComponent<MeshFilter>();
            Check(head.gameObject.activeSelf && head.sharedMesh.vertexCount > 250 && head.sharedMesh.bounds.size.y > .02f && ribbon.sharedMesh.vertexCount > 100, "Flight does not draw its solid trident and sheath.");
            Vector3 tip = head.transform.TransformPoint(ribbon.sharedMesh.vertices[0]);
            Vector3 drawnTip = head.transform.TransformPoint(head.sharedMesh.bounds.max);
            Check(instance.Position.x > .5f && Mathf.Abs(tip.x - drawnTip.x) < .065f, "The water sheath does not start at the moving tip.");
            Vector3 hitAt=instance.Position;
            Capture("10-flight",instance.Position + Vector3.left * .24f,1.08f,instance.gameObject);
            Check(shot.Handle.End(),"Terminating the trident failed.");
            Check(!head.gameObject.activeSelf && !ribbon.gameObject.activeSelf,"Returned projectile left a visible weapon or trail.");
            var explosion = blasts.Create(new PNBlastCreateRequest(blastId,splashConfig["input"],combat,scene,hitAt,stat));
            Check(explosion.Status==PNBlastStatus.Created,"Blast spawn failed "+explosion.Status+" "+explosion.Error);
            float blastDelay = splashConfig["input"]["delay"].Value<float>();
            blasts.Advance(blastDelay * .5f);
            Check(explosion.Handle.IsAlive, "The trident blast detonated before its registered delay.");
            blasts.Advance(blastDelay * .5f);
            var splash = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PNGuraTridentSplash>(true)).Single(x=>x.Detonated);
            Check(!explosion.Handle.IsAlive && splash.StrikeCount == 1, "The visual tail started without ending the single damage strike.");
            var pulse = splash.transform.Find("Gura Water");
            Check(pulse != null && pulse.gameObject.activeSelf, "The trident blast did not render its pressure wave.");
            var prefabLayers = splash.transform.Find("Water_Explosion (Gura)");
            Check(prefabLayers != null && prefabLayers.GetComponentsInChildren<ParticleSystem>().Count(p => p.particleCount > 0) >= 3,
                "The explosion does not render the source's water ball, spikes and flash.");
            var front = pulse.GetComponent<MeshFilter>().sharedMesh.vertices.Select(p => pulse.TransformPoint(p) - splash.Center);
            Check(front.All(p => new Vector2(p.x,p.z).magnitude <= splash.Radius + .001f)
                && front.Any(p => new Vector2(p.x,p.z).magnitude >= splash.Radius - .002f),
                "At detonation the water wave does not show the blast radius.");
            Capture("11-impact",hitAt+Vector3.up*.35f,1.58f,splash.gameObject);
            var urp = camera.GetUniversalAdditionalCameraData();
            try
            {
                urp.SetRenderer(1);
                Capture("impact-3d", hitAt+Vector3.up*.35f, 1.58f, splash.gameObject);
                Check(output.GetPixels32().Count(c => c.b > c.r + 35 && c.g > 100) > 600, "Explosion is missing in the URP 3D renderer.");
            }
            finally { urp.SetRenderer(0); }
            // The same burst standing on an opaque ground (3D renderer): the lower part of the water ball is under it.
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.name = "Fixture ground"; floor.layer = 31;
                floor.transform.SetPositionAndRotation(new Vector3(hitAt.x, 0f, hitAt.z), Quaternion.Euler(90f, 0f, 0f));
                floor.transform.localScale = new Vector3(14f, 14f, 1f);
                var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                floorMaterial.SetColor("_BaseColor", new Color(.36f, .46f, .30f));
                floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                var groundRenderer = camera.GetUniversalAdditionalCameraData();
                try
                {
                    groundRenderer.SetRenderer(1);
                    foreach (var (tag, age) in new[] { ("flash", 0f), ("ball", .22f), ("ribbons", .36f), ("break", .52f) })
                    {
                        // A pooled blast replays from its own clock; the picture alone is moved here, the object keeps its age.
                        typeof(PNGuraTridentSplash).GetMethod("Draw", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(splash, new object[] { age });
                        Capture("impact-ground-" + tag, hitAt + Vector3.up * .45f, 2.15f, splash.gameObject);
                    }
                    typeof(PNGuraTridentSplash).GetMethod("Draw", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(splash, new object[] { 0f });
                }
                finally
                {
                    groundRenderer.SetRenderer(0);
                    UnityEngine.Object.DestroyImmediate(floor); UnityEngine.Object.DestroyImmediate(floorMaterial);
                }
            }
            float blastTail = splashConfig["input"]["tail"].Value<float>();
            Check(blastTail >= .7f, "The Skill explosion must take its time to come down.");
            int tailFrames = Mathf.FloorToInt(blastTail * 60f) - 1;
            for (int frame = 0; frame < tailFrames; frame++)
            {
                Capture($"impact-motion-{frame:D2}", hitAt+Vector3.up*.45f, 2.15f, splash.gameObject);
                if (frame == 24)
                    Check(prefabLayers.gameObject.activeSelf && prefabLayers.GetComponentsInChildren<ParticleSystem>().Count(p => p.particleCount > 0) >= 3
                        && splash.StrikeCount == 1, "The water did not stay in the air after the single strike.");
                blasts.Advance(1f / 60f);
            }
            blasts.Advance(blastTail - tailFrames / 60f + .02f);
            Check(!pulse.gameObject.activeSelf && !prefabLayers.gameObject.activeSelf, "Pooled blast kept playing.");

            // The held Dash through the Dash binding: one Dash, the coat while held, the flag when it ends.
            {
                var dashStart = new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Repeat);
                var dashHold = new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Repeat);
                var dashRelease = new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat);
                var abilities = gura.GetComponent<PNAbilityManager>();
                var dashStep = typeof(PNPlayerSkill).GetMethod("AdvancePlayerDash", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                bool Coat() => GuraResources.IsOn(skill.Abilities, PNGuraContent.WaterCoat);
                bool Flag() => GuraResources.IsOn(skill.Abilities, PNGuraContent.NextFour);
                void Settle() { for (int i = 0; i < 60; i++) dashStep.Invoke(skill, new object[] { 1f / 60f }); }
                void Fresh() { skill.CancelBindingExecution(); Settle(); GuraResources.End(skill.Abilities, PNGuraContent.NextFour); skill.DashRecovery.Reset(); }
                void Press() { skill.RequestSlot(PNSkillSlots.Dash, dashStart); skill.RequestSlot(PNSkillSlots.Dash, dashHold); }
                Fresh();
                double speed = stat.Evaluate(EveId.Parse("base:move_speed"));
                int charges = skill.DashRecovery.Current;
                Press();
                Check(skill.IsDashing && Coat() && !Flag() && skill.DashRecovery.Current == charges - 1, "A held Dash must dash once and put the coat on, with no flag yet.");
                Check(stat.Evaluate(EveId.Parse("base:move_speed")) > speed + .3, "The coat does not speed Gura up.");
                // The coat is seen for as long as it is on: a look the engine turns on and off with the state.
                var coatLook = gura.GetComponentInChildren<PNGuraCoatVisual>();
                Check(skill.HasLook(PNGuraCoatLooks.Id) && coatLook != null && skill.LookFailures == 0, "The held Dash coat has no picture.");
                bool Seen() => coatLook.GetComponentsInChildren<MeshFilter>().Any(x => x.name == "Gura Dash Coat Water" && x.gameObject.activeSelf && x.sharedMesh.vertexCount > 20);
                // The sheet over her drawing and the ring behind it hang on the puppet, the rest under the look's own object.
                bool Part(string material) => gura.GetComponentsInChildren<MeshRenderer>().Any(x => x.sharedMaterial != null && x.sharedMaterial.name == material && x.gameObject.activeSelf);
                bool Sheet() => Part("Gura Dash coat");
                Check(coatLook.Fill > .95f, "The coat does not go on as solid water.");
                var puppet = gura.GetComponentInChildren<PNVisualBody>().Actor;
                for (int frame = 0; frame < 66; frame++)
                {
                    // Standing while it goes on, then running to the right.
                    gura.transform.position = Vector3.right * (Mathf.Max(0, frame - 30) * (3.35f / 60f));
                    coatLook.Tick(1f / 60f); puppet.Tick(1f / 60f);
                    Capture($"coat-motion-{frame:D2}", new Vector3(gura.transform.position.x * .75f, .30f, 0f), 1.0f, gura);
                }
                Check(Seen() && Sheet() && Part("Gura Dash coat ring") && coatLook.Fill > .95f, "The running coat must stay solid water with a ring, as the Skill's coat.");
                Check(coatLook.GhostCount >= 1 && coatLook.GhostCount <= 3, "Running in the coat leaves no after-image.");
                puppet.Renderer.enabled = false; coatLook.Tick(1f / 60f);
                Check(!Seen() && !Sheet() && !Part("Gura Dash coat ring") && coatLook.GhostCount == 0, "The coat stayed while her puppet was away.");
                puppet.Renderer.enabled = true; coatLook.Tick(1f / 60f);
                Check(Seen() && Sheet(), "The coat did not come back with her puppet.");
                gura.transform.position = Vector3.zero;
                Settle();
                for (int i = 0; i < 10; i++) { abilities.Advance(.1); skill.RequestSlot(PNSkillSlots.Dash, dashHold); }
                Check(!skill.IsDashing && Coat() && !Flag() && skill.DashRecovery.Current == charges - 1, "Holding on dashed again or gave the flag early.");
                // Into the last second of the state: the coat comes and goes.
                for (int i = 0; i < 22; i++) { abilities.Advance(.1); skill.RequestSlot(PNSkillSlots.Dash, dashHold); }
                float low = 1f, high = 0f;
                for (int i = 0; i < 30; i++) { coatLook.Tick(1f / 60f); low = Mathf.Min(low, coatLook.Fill); high = Mathf.Max(high, coatLook.Fill); }
                Check(Coat() && high - low > .1f, "The coat does not flicker before it runs out.");
                skill.RequestSlot(PNSkillSlots.Dash, dashRelease);
                Check(!Coat() && Flag() && Math.Abs(stat.Evaluate(EveId.Parse("base:move_speed")) - speed) < .001, "Letting go must end the coat and give the flag.");
                Check(!skill.HasLook(PNGuraCoatLooks.Id) && coatLook != null && coatLook.Releasing, "Letting go did not play the coat's release.");
                // The release is photographed alone: the Dash that moved her here is put out of sight.
                foreach (var slide in gura.GetComponentsInChildren<PNGuraDashVisual>()) slide.Hide();
                gura.transform.position = Vector3.zero;
                for (int i = 0; i < 36 && coatLook != null; i++)
                {
                    coatLook.Tick(1f / 60f); puppet.Tick(1f / 60f);
                    if (coatLook != null && i == 4) Check(coatLook.Fill > .95f, "The released coat does not turn solid.");
                    Capture($"coat-release-{i:D2}", new Vector3(0f, .30f, 0f), 1.0f, gura);
                }
                var remains = gura.GetComponentsInChildren<Transform>(true)
                    .Where(x => x.name.StartsWith("Gura Dash coat") || x.name == "Gura dash ghost" && x.GetComponentInParent<PNGuraDashVisual>(true) == null)
                    .Select(x => x.name).ToArray();
                Check(coatLook == null && gura.GetComponentsInChildren<PNGuraCoatVisual>(true).Length == 0 && remains.Length == 0,
                    "The released coat was not removed: look " + (coatLook != null) + ", left " + string.Join(", ", remains));
                Fresh(); Press(); Settle();
                for (int i = 0; i < 38; i++) { abilities.Advance(.1); skill.RequestSlot(PNSkillSlots.Dash, dashHold); }
                Check(Coat() && !Flag(), "The coat ended before four seconds.");
                for (int i = 0; i < 3; i++) { abilities.Advance(.1); skill.RequestSlot(PNSkillSlots.Dash, dashHold); }
                Check(!Coat() && Flag() && !skill.IsDashing, "Four seconds must end the coat and give the flag while still held.");
                Check(!skill.HasLook(PNGuraCoatLooks.Id), "The picture of the coat outlived its four seconds.");
                GuraResources.End(skill.Abilities, PNGuraContent.NextFour);
                skill.RequestSlot(PNSkillSlots.Dash, dashRelease);
                Check(!Flag(), "Letting go after the coat ran out gave a second flag.");
                Fresh(); Press(); Settle();
                using (skill.Controls.Acquire("gura fixture stun", new[] { PNControlIds.SkillAll }))
                    Check(!Coat() && Flag() && !skill.HasLook(PNGuraCoatLooks.Id), "A stun must count as letting go.");
                skill.RequestSlot(PNSkillSlots.Dash, dashRelease);
                Fresh();
                skill.RequestSlot(PNSkillSlots.Dash, dashStart); skill.RequestSlot(PNSkillSlots.Dash, dashRelease);
                Check(!Coat() && !Flag() && !skill.IsDashing, "A quick press on the held path did something.");
                Fresh(); Press(); Settle();
                Check(Coat(), "The coat did not come back for the next held press.");
                stat.SetCurrent(PNContentIds.Health, 0);
                skill.RequestSlot(PNSkillSlots.Dash, dashRelease);
                Check(stat.IsDead && !Coat() && !Flag() && !skill.HasLook(PNGuraCoatLooks.Id), "Death must take the coat and give no flag.");
            }

            var modelType = typeof(PNGuraContent).Assembly.GetType("PNGuraSharkModel", true);
            using (var model = (IDisposable)Activator.CreateInstance(modelType, new object[] { gura.transform }))
            {
                var draw = modelType.GetMethod("Draw");
                var facing = Quaternion.LookRotation(new Vector3(.97f, 0f, .23f));
                draw.Invoke(model, new object[] { Vector3.up*.68f, facing, .47f, -100f, 1f, .16f });
                var rigged = SharkRoot().GetComponentInChildren<SkinnedMeshRenderer>();
                Check(rigged != null && rigged.sharedMesh.vertexCount > 2000, "Full shark is missing the imported mesh.");
                var jaw = rigged.bones.Single(b => b.name == PNGuraSharkModel.JawJoint);
                var open = jaw.localRotation;
                draw.Invoke(model, new object[] { Vector3.up*.68f, facing, .47f, -100f, -.85f, .16f });
                Check(Quaternion.Angle(open, jaw.localRotation) > 22f, "The lower jaw does not close between the open and shut gape.");
                var dark = camera.backgroundColor;
                Capture("shark-full", new Vector3(-1.1f, .30f, 0f), 1.52f, gura);
                camera.backgroundColor = new Color(.56f, .68f, .60f);
                try { Capture("shark-full-light", new Vector3(-1.1f, .30f, 0f), 1.52f, gura); }
                finally { camera.backgroundColor = dark; }
            }
            return "PASS " + checks + " Unity presentation, projectile and blast checks; captured sequences.";
        }
        finally
        {
            foreach (var body in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<PNVisualBody>(true))) body.Clear();
            loader?.Unload();
            projectiles?.Shutdown();
            blasts?.Shutdown();
            runtime.UnloadContent();
            PNVisualRuntime.Release();
            PNVisualPacks.Clear();
            RenderTexture.active=oldTarget;
            if (output!=null) UnityEngine.Object.DestroyImmediate(output);
            if (target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

#endif
