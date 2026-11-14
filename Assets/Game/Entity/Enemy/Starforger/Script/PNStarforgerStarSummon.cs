#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// A Starforger star as a summon (Docs/StrikeBlastPlan.md U2; the character `project_n:starforger_star`, its numbers in the character's `star` block, schema PNStarforgerCombatContent.ValidateStarInput).
// It hangs where the star skill put it (the summon's own position), has no health and cannot be hit, lives the summon's `lifetime` (the skill's registered number) and every `interval` seconds shoots one
// bolt at the nearest hostile within `range` (first shot `firstDelay` + slot * `stagger` after it appeared; the slot is the number in its group name). DEPENDENT: no KeepWhenOwnerGone, so the owner's
// death, unload or leaving the world retires it. The slam asks every star for one extra bolt toward its landing (FireToward), which ignores `range` and does not move the rhythm.
// The damage is prepared once when the star starts, from the owner's combat source captured at the summon, like a projectile's. Its timers run on the summon's own entity clock. Only the authority runs it.
[DisallowMultipleComponent]
public sealed class PNStarforgerStarSummon : MonoBehaviour
{
    private PNSummon summon; private PNStat stat;
    private PNCombatSource source; private PNPreparedDamage prepared; private PNTargetRule rule; private EveEntityId ownerId; private PNProjectileManager projectiles;
    private float interval, range, age, nextShot; private int slot = -1; private bool started; private Vector3 position;
    private PNStarforgerStarVisual visual;
    public int Slot => slot;
    public float Age => age;
    public void Release() => OnDisable();                         // an Edit Mode fixture calls it itself (Edit Mode runs neither OnDisable nor OnDestroy)
    public bool IsRunning => started && summon != null && summon.IsActive && !summon.IsRetiring;
    public bool Started => started;
    public int Shots { get; private set; }
    public void RenderVisual(Camera camera) { visual?.Render(camera); }
    private void OnDisable() => Reset();
    private void OnDestroy() => Reset();
    private void Reset()
    {
        visual?.Dispose(); visual = null; summon = null; stat = null; source = null; prepared = null; rule = default; projectiles = null; slot = -1; started = false; age = 0; Shots = 0;
    }

    private void Update()
    {
        var scale = GetComponent<EveTimeScale>(); float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        if (factor > 0f) Tick(Time.deltaTime * factor);
    }
    // One step of `seconds` of entity time (Update calls it; a fixture calls it by hand).
    public void Tick(float seconds)
    {
        if (!(seconds > 0f) || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return;
        var loader = GetComponent<EveEntityLoader>(); if (loader == null || !loader.IsReady || loader.LoadContext?.CanSimulate != true) return;
        summon = summon ?? GetComponent<PNSummon>(); if (summon == null || !summon.IsActive || summon.IsRetiring) return;
        if (!started && !Begin()) return;
        age += seconds;
        while (age >= nextShot)
        {
            nextShot += interval;
            var toward = NearestHostile();
            if (toward.HasValue) Fire(toward.Value);          // no target in range: the shot is skipped, the rhythm goes on
        }
        float lead = nextShot - age;
        float charge = lead >= 0 && lead < .15f && NearestHostile().HasValue ? 1 - lead / .15f : 0;
        visual?.Advance(age, charge);
    }
    private bool Begin()
    {
        stat = stat ?? GetComponent<PNStat>(); source = summon.OwnerSource; var owner = source?.LoadedSource;
        if (owner == null || stat == null || !PNStarforgerCombatContent.TrySlot(summon.Group, out slot)) { summon.Retire(); return false; }
        var registry = EveRegistrySession.Context.Registry;
        var block = (registry.Characters.Get(stat.CharacterId).Data as JObject)?["star"] ?? throw new InvalidOperationException("Character " + stat.CharacterId + " has no star block.");
        PNStarforgerCombatContent.ValidateStarInput(block); var o = (JObject)block;
        interval = o["interval"].Value<float>(); range = o["range"].Value<float>();
        ownerId = owner.EntityId; rule = PNTargetRules.For(owner); position = transform.position;
        nextShot = PNStarforgerPlanner.FirstShot(o["firstDelay"].Value<float>(), o["stagger"].Value<float>(), slot);
        double amount = 0d; foreach (var component in PNSkillValues.Parse(o["value"]).Components) amount += component.Evaluate(owner);
        var tags = PNSkillEffect.Ids(o["tags"]).Concat(new[] { PNContentIds.ProjectileDamage }).Distinct().ToArray();
        prepared = (source.Snapshot ?? throw new InvalidOperationException("The star needs a combat snapshot.")).PrepareDamage(amount, o["canCrit"].Value<bool>(), null, tags);
        projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene);
        age = 0;
        if (visual == null) visual = new PNStarforgerStarVisual(transform, gameObject.layer);
        var forge = owner.GetComponent<PNStarforgerPresentation>(); visual.Begin(position, forge != null ? forge.ForgePosition : position + Vector3.up * .2f);
        started = true; return true;
    }
    // The planar direction to the nearest living hostile (the owner's target rule) within `range`, or null.
    private Vector3? NearestHostile()
    {
        var target = PNTargetSelector.Select(projectiles.Targets.Acquire(GetComponent<EveEntityIdentity>().World), new PNTargetQuery
        { Origin = position, Range = range, LayerMask = source.TargetLayers.Mask, Rule = rule, Exclude = ownerId, Planar = true });
        if (target == null) return null;
        var d = target.transform.position - position; d.y = 0;
        return d.sqrMagnitude > 1e-8f ? d.normalized : (Vector3?)null;
    }
    // One extra bolt toward `point` (the slam's landing), whatever the range; false when the star is not running.
    public bool FireToward(Vector3 point)
    {
        if (!started || summon == null || !summon.IsActive || summon.IsRetiring) return false;
        var d = point - position; d.y = 0; if (d.sqrMagnitude < 1e-8f) return false;
        Fire(d.normalized); return true;
    }
    private void Fire(Vector3 heading)
    {
        var launch = PNProjectileLaunch.FromSource(PNStarforgerCombatContent.Bolt, new JObject(), source, source.TargetLayers, prepared, null, rule);
        var receipt = projectiles.Create(launch, new PNProjectileSpawn(gameObject.scene, position + Vector3.up * .2f, heading));
        if (receipt.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Star bolt creation failed.", receipt.Error);
        visual?.Flash(heading, age); Shots++;
    }
}

// The star's picture, owned by one star summon: a gold four-point star that bobs, with a soft glow, and a short flash when it shoots. Camera-facing, on the project's own star effect shader.
public sealed class PNStarforgerStarVisual
{
    private readonly GameObject root; private readonly Material starMaterial, glowMaterial; private readonly Mesh mesh, quad; private readonly Transform star, glow; private float age,charge,shotAt=-10; private Vector3 position,shotDirection,birthOrigin; private bool disposed,running; private readonly PNStarreaperEnergyMesh sparks;
    public PNStarforgerStarVisual(Transform parent, int layer)
    {
        root = new GameObject("Starforger star visual"); root.layer = layer; root.transform.SetParent(parent, false);
        var shader = Resources.Load<Shader>("PNStarweaverEffects"); if (shader == null) throw new InvalidOperationException("Missing star VFX shader.");
        starMaterial = new Material(shader) { name = "Starforger star", hideFlags = HideFlags.HideAndDontSave }; starMaterial.SetFloat("_Mode", 0); starMaterial.SetFloat("_DstBlend", 1); starMaterial.SetColor("_Tint", new Color(1.5f, 1.15f, .45f));
        glowMaterial = new Material(shader) { name = "Starforger star glow", hideFlags = HideFlags.HideAndDontSave }; glowMaterial.SetFloat("_Mode", 5); glowMaterial.SetFloat("_DstBlend", 1); glowMaterial.SetColor("_Tint", new Color(1.2f, .8f, .25f));
        glowMaterial.SetTexture("_MainTex", Resources.Load<Texture2D>("StarweaverVfx/Glow"));
        // a four-point star in the XY plane: outer points on the axes, inner points on the diagonals
        var vertices = new Vector3[9]; var colors = new Color[9]; var triangles = new int[24]; vertices[8] = Vector3.zero; colors[8] = new Color(1, 1, .85f);
        for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f, r = i % 2 == 0 ? .5f : .17f; vertices[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0); colors[i] = i % 2 == 0 ? new Color(1, .85f, .4f) : new Color(.9f, .6f, .2f); }
        for (int i = 0; i < 8; i++) { triangles[i * 3] = 8; triangles[i * 3 + 1] = i; triangles[i * 3 + 2] = (i + 1) % 8; }
        mesh = new Mesh { name = "Starforger star", hideFlags = HideFlags.HideAndDontSave }; mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles; mesh.RecalculateBounds();
        quad = Quad(); star = Draw("Star", mesh, starMaterial, layer); glow = Draw("Star glow", quad, glowMaterial, layer);
        sparks=new PNStarreaperEnergyMesh(root.transform,"Starforger star sparks");root.SetActive(false);
    }
    private Mesh Quad()
    {
        var q = new Mesh { name = "Starforger glow quad", hideFlags = HideFlags.HideAndDontSave };
        q.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) }; q.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        q.triangles = new[] { 0, 1, 2, 0, 2, 3 }; var c = new Color[4]; for (int i = 0; i < 4; i++) c[i] = Color.white; q.colors = c; q.RecalculateBounds(); return q;
    }
    private Transform Draw(string label, Mesh m, Material material, int layer)
    {
        var go = new GameObject(label); go.layer = layer; go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return go.transform;
    }
    public void Begin(Vector3 at,Vector3? origin=null)
    {
        if(disposed)return;Stop();position=at;birthOrigin=origin??(at+Vector3.up*.2f);age=charge=0;shotAt=-10;shotDirection=Vector3.zero;running=true;root.SetActive(true);PNViewCamera.Updated+=OnView;OnView();
    }
    public void Flash(Vector3 direction,float seconds){shotAt=seconds;shotDirection=direction;}
    public void Advance(float seconds,float energy=0){if(disposed||!running)return;age=seconds;charge=Mathf.Clamp01(energy);OnView();}
    private void OnView(){Render(PNViewCamera.Current);}
    public void Render(Camera camera)
    {
        if(disposed||!running||camera==null)return;
        float shot=Mathf.Exp(-Mathf.Max(0,age-shotAt)*22),birth=1-Mathf.Exp(-age*16);
        float birthFlare=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.30f));
        float pop=birth*(1+.18f*Mathf.Sin(Mathf.Min(1,age/.32f)*Mathf.PI));
        float bob=Mathf.Sin(age*2.4f)*.018f*(1-Mathf.Max(charge,shot));
        float size=.2f*pop*(1+.12f*charge+.12f*shot),spin=Mathf.Sin(age*1.3f)*8;
        // Aligned with the existing physical muzzle at +0.2m; no projectile physics change.
        float arrival=Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.28f));
        var at=Vector3.Lerp(birthOrigin,position+Vector3.up*(.2f+bob),arrival)+Vector3.up*(Mathf.Sin(arrival*Mathf.PI)*.08f)-shotDirection*(shot*.025f);
        star.SetPositionAndRotation(at,camera.transform.rotation*Quaternion.Euler(0,0,spin));star.localScale=new Vector3(size,size,1);
        glow.SetPositionAndRotation(at+camera.transform.forward*.01f,camera.transform.rotation);glow.localScale=Vector3.one*(.36f+.18f*birthFlare+.15f*charge+.12f*shot)*birth;
        glowMaterial.SetFloat("_Opacity",.25f+.5f*birthFlare+.4f*charge+.35f*shot);
        var right=camera.transform.right;var up=camera.transform.up;var front=-camera.transform.forward*.004f;sparks.Begin(age);
        float appear=1-Mathf.Clamp01(age/.45f);
        sparks.Quad(true,at+front,right,up,.48f,.36f,new Color(3.0f,2.1f,.8f,Mathf.Exp(-Mathf.Max(0,age-.025f)*32)),2);
        if(age<.22f)sparks.Quad(true,at+front*2,right,up,.40f,.32f,new Color(1.15f,.35f,1.8f,appear*.8f),21,frame:Mathf.Clamp01(age/.22f)*3);
        for(int i=0;i<12;i++)
        {
            float a=i*2.39996f;var dir=right*Mathf.Cos(a)+up*Mathf.Sin(a);
            var point=at+dir*(.035f+(1-appear)*.22f)+Vector3.up*(-age*age*.25f);
            sparks.Quad(true,point+front,dir,Vector3.Cross(camera.transform.forward,dir),.065f*appear,.03f,new Color(2.0f,1.3f,.38f,appear),1);
            if(i%3==0)sparks.Quad(true,point+front,right,up,.04f*appear,.04f*appear,new Color(1.8f,1.2f,.45f,appear),3);
        }
        if(charge>0||shot>.005f)
        {
            float c=Mathf.Max(charge,shot);
            sparks.Quad(true,at+front*2,right,up,.20f,.20f,new Color(2.1f,1.6f,.6f,c*.7f),3);
            for(int i=0;i<5;i++)
            {
                float a=i*2.39996f-age*2;var dir=right*Mathf.Cos(a)+up*Mathf.Sin(a);var point=at+dir*(.14f*(1-charge));
                sparks.Quad(true,point+front,right,up,.018f,.018f,new Color(1.8f,.95f,.3f,c*.8f),3);
            }
        }
        sparks.End();
    }
    public void Stop(){if(!running)return;running=false;PNViewCamera.Updated-=OnView;sparks?.Clear();if(root!=null)root.SetActive(false);}
    public void Dispose()
    {
        if (disposed) return;Stop(); disposed = true;sparks?.Dispose();
        if (root != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root); }
        foreach (var o in new UnityEngine.Object[] { starMaterial, glowMaterial, mesh, quad }) if (o != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
    }
}
#endif
