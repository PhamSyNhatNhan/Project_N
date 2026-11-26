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
            string outputDir = Path.GetFullPath(".utmp/GuraArc/frames");
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
            void CheckSector(string node, Vector3 centre, Vector3 aim, float radius, float angle, float thickness, bool whole)
            {
                var drawn = gura.transform.Find(node);
                Check(drawn != null && drawn.gameObject.activeSelf, node + " is not drawn.");
                float reach = 0f, left = 0f, right = 0f;
                foreach (var local in drawn.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    var offset = drawn.TransformPoint(local) - centre;
                    var flat = new Vector3(offset.x, 0f, offset.z);
                    float turn = Vector3.SignedAngle(aim, flat, Vector3.up);
                    Check(flat.magnitude <= radius + .001f, node + " draws past the hit radius: " + flat.magnitude);
                    Check(Mathf.Abs(turn) <= angle * .5f + .05f, node + " draws past the hit angle: " + turn);
                    Check(Mathf.Abs(offset.y) <= thickness * .5f + .001f, node + " leaves the hit thickness: " + offset.y);
                    reach = Mathf.Max(reach, flat.magnitude); left = Mathf.Min(left, turn); right = Mathf.Max(right, turn);
                }
                if (whole) Check(reach > radius - .002f && left < -angle * .5f + .1f && right > angle * .5f - .1f,
                    node + " does not cover the hit sector on the hit: radius " + reach + ", angle " + left + ".." + right);
            }
            var attack = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_attack_1")).Data).Action.Perform;
            var sector = EveHitboxShape.Sector(attack["radius"].Value<float>(), attack["thickness"].Value<float>(), attack["angle"].Value<float>());
            SphereCollider SectorTarget(float degrees)
            {
                var targetObject = new GameObject("Sweep sector fixture target");
                SceneManager.MoveGameObjectToScene(targetObject, scene);
                targetObject.transform.position = Quaternion.AngleAxis(degrees, Vector3.up) * Vector3.right * 1.1f;
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

            var samples = new[]
            {
                ("attack_1", new[] { .042, .112, .17, .207, .265 }),
                ("attack_2", new[] { .043, .113, .17, .218, .265 }),
                ("attack_3", new[] { .20, .30, .36, .42, .48, .54, .60 }),
                ("attack_4", new[] { .07, .29, .32, .38 }),
                ("trident", new[] { .08, .20 }),
                ("shark", new[] { .052, .105, .156, .182, .205, .29, .407 }),
                ("dive", new[] { .70 }),
                ("dash", new[] { .045, .085, .125 }),
            };
            var skill = gura.GetComponent<PNGuraSkill>();
            foreach (var (state, ages) in samples)
            {
                var data = PNSkillData.Parse(hub.Skills.Get(EveId.Parse("project_n:gura_" + state)).Data);
                var definition = hub.Actions().Get(data.Action.Id);
                var view = definition.ViewFactory(new PNActionViewBuild(skill, data,Vector3.right,null,1));
                float weaponLength = 0f;
                bool sweep = state == "attack_1" || state == "attack_2";
                try
                {
                    for (int i = 0; i < ages.Length; i++)
                    {
                        var age = ages[i];
                        if (state == "dash") gura.transform.position = Vector3.right * (float)age * 8.5f;
                        if (state == "attack_3") gura.transform.position = Vector3.right * Mathf.Clamp01(((float)age-.2f)*4f)*.5f;
                        view.Advance(age,1f,i==0);
                        body.Actor.Tick(1f/120f);
                        float framing = state == "attack_4" ? 1.50f : state == "shark" ? 1.38f : state == "dive" ? .68f : sweep ? 1.17f : .89f;
                        float centerX = state == "attack_4" ? 1.10f : state == "shark" ? .10f : state == "dash" ? .25f : .28f;
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
                                CheckSector("Gura slash bright", centre, Vector3.right, radius, angle, thickness, true);
                                CheckSector("Gura slash deep", centre, Vector3.right, radius, angle, thickness, true);
                            }
                            if (i == 2)
                            {
                                Check(angle == 230f, "The skill sector is not 230 degrees.");
                                CheckSector("Gura Water", centre, Vector3.right, radius, angle, thickness, true);
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
                        if (state == "attack_3" && i is 1 or 3 or 5)
                        {
                            int beat = (i - 1)/2;
                            var cuts = gura.transform.Cast<Transform>().Where(x => x.name == "Slash_Water (Gura)").ToArray();
                            Check(cuts.Length == 3 && cuts.Count(x=>x.gameObject.activeSelf)==1 && cuts[beat].gameObject.activeSelf,
                                "Rush does not show one separate cut at each hit.");
                            Check(gura.transform.Find("Water_Splash (Gura)").gameObject.activeSelf, "Rush is missing its water cover.");
                        }
                        if (state == "shark" && i == 2)
                        {
                            var jaws = gura.transform.Find("Gura shark upper jaw");
                            Check(jaws.gameObject.activeSelf && Mathf.Approximately(jaws.localScale.x, 1.5f), "Shark head is not scaled to the hit radius.");
                            var renderer = camera.GetUniversalAdditionalCameraData();
                            renderer.SetRenderer(1);
                            try
                            {
                                Capture("shark-3d",new Vector3(centerX,.34f,0),framing,gura);
                                Check(output.GetPixels32().Count(pixel => pixel.b > 100 && pixel.b > pixel.r + 30) > 12000,
                                    "The solid head does not render in the URP 3D renderer.");
                            }
                            finally { renderer.SetRenderer(0); }
                        }
                    }
                    view.Hide();
                    Check(!gura.transform.Cast<Transform>().Any(x => (x.name.StartsWith("Gura ") || x.name.EndsWith("(Gura)")) && x.gameObject.activeSelf),
                        state + " retained visible graphics after Hide.");
                }
                finally { view.End(PNActionViewEnd.Cancelled); gura.transform.position = Vector3.zero; }
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

            var move = gura.GetComponent<PNPlayerMove>();
            var advance = typeof(PNSkill).GetMethod("AdvanceBindings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int previousStarts = skill.ActionViewsStarted;
            int previousFailures = skill.ActionViewFailures;
            move.SetMoveDirection(Vector2.left);
            body.Actor.SetFacingLeft(false);
            var started = new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap);
            var released = new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap);
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

            var castData = PNSkillData.Parse(hub.Skills.Get(PNGuraContent.TridentSkill).Data);
            var config = (JObject)castData.Action.Perform;
            var projectileId = EveId.Parse(config["projectile"]["projectileId"].Value<string>());
            var blastId = EveId.Parse(config["blast"]["blastId"].Value<string>());
            var combat = PNCombatSource.Capture(stat);
            var shot = projectiles.Create(
                new PNProjectileLaunch(projectileId,config["projectile"]["input"],combat,stat.TargetLayers),
                new PNProjectileSpawn(scene,new Vector3(0,.25f,0),Vector3.right));
            Check(shot.Status == PNProjectileStatus.Created,"Trident spawn failed "+shot.Status+" "+shot.Error);
            var instance = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PNGuraThrownTrident>(true)).Single(x=>x.IsRunning);
            projectiles.Advance(.10f);
            instance.SendMessage("LateUpdate",SendMessageOptions.RequireReceiver);
            var head = instance.transform.Find("Gura thrown trident").GetComponent<MeshFilter>();
            var ribbon = instance.transform.Find("Gura Water").GetComponent<MeshFilter>();
            Check(head.gameObject.activeSelf && head.sharedMesh.vertexCount > 250 && head.sharedMesh.bounds.size.y > .02f && ribbon.sharedMesh.vertexCount > 100, "Flight does not draw its solid trident and sheath.");
            Vector3 tip = head.transform.TransformPoint(ribbon.sharedMesh.vertices[0]);
            Vector3 drawnTip = head.transform.TransformPoint(head.sharedMesh.bounds.max);
            Check(instance.Position.x > .5f && Mathf.Abs(tip.x - drawnTip.x) < .065f, "The water sheath does not start at the moving tip.");
            Vector3 hitAt=instance.Position;
            Capture("10-flight",new Vector3(.72f,.23f,0),.75f,instance.gameObject);
            Check(shot.Handle.End(),"Terminating the trident failed.");
            Check(!head.gameObject.activeSelf && !ribbon.gameObject.activeSelf,"Returned projectile left a visible weapon or trail.");
            var explosion = blasts.Create(new PNBlastCreateRequest(blastId,config["blast"]["input"],combat,scene,hitAt,stat));
            Check(explosion.Status==PNBlastStatus.Created,"Blast spawn failed "+explosion.Status+" "+explosion.Error);
            blasts.Advance(.064f);
            var splash = scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PNGuraTridentSplash>(true)).Single(x=>x.Detonated);
            var pack = splash.transform.Find("Water_Explosion (Gura)");
            Check(pack!=null && pack.gameObject.activeSelf && pack.GetComponentsInChildren<ParticleSystem>().Any(p=>p.particleCount>0 && p.GetComponent<ParticleSystemRenderer>().enabled),
                "Water_Explosion has no visible sampled particles.");
            Capture("11-impact",hitAt+Vector3.up*.35f,1.28f,splash.gameObject);
            blasts.Advance(.22f);
            Check(!pack.gameObject.activeSelf,"Pooled blast kept playing.");
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
