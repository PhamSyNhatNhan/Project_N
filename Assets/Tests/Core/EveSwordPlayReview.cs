#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// EN: Opt-in review of actual scene physics and rendered Game View frames.
// VI: Kiểm tra chủ động vật lý scene thật và ảnh Game View trong Play Mode.
public sealed class EveSwordPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private EveCharacterSkill skill;
    private Rigidbody body;
    private PNStat dummy;
    private readonly List<string> evidence = new List<string>();
    private int checks;
    private string folder;
    private Camera reviewCamera;
    private Unity.Cinemachine.CinemachineBrain brain;
    private bool brainEnabled;
    private float originalFov;
    public static string Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required.");
        var root = new GameObject("Temporary actual Eve review");
        var review = root.AddComponent<EveSwordPlayReview>();
        review.skill = UnityEngine.Object.FindObjectsByType<EveCharacterSkill>().First(x=>x.IsReady);
        review.body = review.skill.GetComponent<Rigidbody>();
        review.dummy = UnityEngine.Object.FindObjectsByType<DummyStat>().First();
        review.folder = Path.GetFullPath(".utmp/EveRegistryMotionReview"); Directory.CreateDirectory(review.folder);
        var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        EditorWindow.GetWindow(gameView).Focus();
        Status = "Running actual scene review";
        review.StartCoroutine(review.Guarded(review.Sequence())); return Status;
    }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        while(true)
        {
            object next;
            try { if(!sequence.MoveNext()) break; next=sequence.Current; }
            catch(Exception error) { Status="FAIL: "+error; Debug.LogException(error); break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();
        if(reviewCamera!=null)reviewCamera.fieldOfView=originalFov;
        if(brain!=null)brain.enabled=brainEnabled;
        File.WriteAllLines(Path.Combine(folder,"Results.txt"), evidence.Concat(new[]{Status}));
        Destroy(gameObject);
    }
    private void Check(bool condition,string message)
    { if(!condition) throw new Exception(message); checks++; evidence.Add("PASS "+message); }
    private void Place(Vector3 p)
    { skill.GetComponentInChildren<EveEnergyVisual>()?.CancelCast(); body.position=p; skill.transform.position=p; body.linearVelocity=Vector3.zero; Physics.SyncTransforms(); }
    private void Target(Vector3 p)
    {
        var b=dummy.GetComponent<Rigidbody>(); if(b!=null) { b.position=p; b.linearVelocity=Vector3.zero; }
        dummy.transform.position=p; Physics.SyncTransforms();
    }
    private IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG()); Destroy(image);
        evidence.Add("Frame "+name+" at "+Time.time.ToString("F3")+" position="+skill.transform.position+" cast="+skill.CurrentCast+" phase="+skill.CurrentPhase);
    }
    private IEnumerator Idle()
    { float until=Time.time+4; while(skill.IsBusy && Time.time<until) yield return null; Check(!skill.IsBusy,"Cast ends and releases movement"); }
    private IEnumerator Sequence()
    {
        yield return new WaitForSeconds(.2f);
        var origin=new Vector3(-1,.14f,0);
        Place(origin); Target(origin+new Vector3(.45f,.28f,0));
        skill.GetComponent<PNMove>().SetMoveDirection(Vector2.right);skill.GetComponent<PNMove>().SetMoveDirection(Vector2.zero);
        skill.DashRecovery.ResetToFull();
        yield return Capture("Dash-before");
        var start=body.position; float began=Time.time;
        Check(skill.TryDash(),"Dash activates against target closer than stop distance");
        yield return new WaitForSeconds(.06f);
        float mid=Vector3.Distance(start,body.position);
        Check(mid>.15f,"Rigidbody moves during Dash: "+mid.ToString("F3")+" units");
        yield return Capture("Dash-middle");
        yield return Idle();
        float travelled=Vector3.Distance(new Vector3(start.x,0,start.z),new Vector3(body.position.x,0,body.position.z));
        Check(Math.Abs(travelled-skill.Moveset.DashDistance)<.12f,"Nearby-target Dash actual distance="+travelled.ToString("F3")+" configured="+skill.Moveset.DashDistance+" elapsed="+(Time.time-began).ToString("F3"));
        Check(skill.DashRecovery.Current==2,"Dash consumes one of three charges");
        yield return Capture("Dash-after");
        // EN: A physical wall must clip motion even when entity colliders are skipped.
        // VI: Tường thật vẫn chặn Dash khi cấu hình bỏ qua collider entity.
        Place(origin); Target(origin+new Vector3(.45f,.28f,0));
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Temporary Dash wall";
        wall.transform.position=origin+new Vector3(1.5f,.4f,0);wall.transform.localScale=new Vector3(.2f,1,2);
        Physics.SyncTransforms(); double timer=skill.DashRecovery.CooldownRemaining;
        Check(skill.TryDash(),"Second Dash activates during existing recovery"); yield return Idle();
        float clipped=body.position.x-origin.x;
        Check(clipped>.8f && clipped<1.4f,"World wall clips actual Dash: "+clipped.ToString("F3"));
        Check(skill.DashRecovery.CooldownRemaining<timer,"Spending next charge preserves recovery progress");
        yield return Capture("Dash-wall"); Destroy(wall); yield return null;
        Place(origin); Target(origin+new Vector3(.45f,.28f,0));
        skill.GetComponent<PNMove>().SetMoveDirection(Vector2.left);skill.GetComponent<PNMove>().SetMoveDirection(Vector2.zero);
        Check(skill.TryDash() && skill.CurrentTarget==null,"Dash ignores nearby target on right while movement is left");yield return Capture("Dash-away-from-target");yield return Idle();
        Check(Math.Abs(body.position.x-origin.x+skill.Moveset.DashDistance)<.12f,"Dash follows movement despite opposing target: delta="+(body.position.x-origin.x).ToString("F3"));
        Check(body.interpolation==RigidbodyInterpolation.Interpolate,"Dash restores authored Rigidbody interpolation");
        Check(!skill.TryDash(),"Exhausted Dash cannot activate");
        yield return new WaitForSeconds(5.1f);
        Check(skill.DashRecovery.Current==1,"Five seconds recovers one charge sequentially");
        Place(origin);Target(new Vector3(40,.42f,0));Check(skill.TryDash(),"No-target movement Dash starts");yield return Idle();
        Check(Math.Abs(body.position.x-origin.x+skill.Moveset.DashDistance)<.12f,"No-target movement Dash travels left three units");
        for(int i=0;i<6;i++)
        {
            Place(origin);skill.DashRecovery.ResetToFull();
            float sign=i%2==0?1:-1;
            skill.GetComponent<PNMove>().SetMoveDirection(new Vector2(sign,0));skill.GetComponent<PNMove>().SetMoveDirection(Vector2.zero);
            Check(skill.TryDash(),"Repeated physical Dash "+i+" starts");yield return Idle();
            yield return new WaitForFixedUpdate();yield return null;
            Check(Math.Abs(body.position.x-origin.x-sign*skill.Moveset.DashDistance)<.12f,
                "Repeated Dash remains at final physics position after interpolation resumes, pass="+i+" delta="+(body.position.x-origin.x).ToString("F3"));
        }
        skill.DashRecovery.ResetToFull(); Place(origin); Target(origin+new Vector3(.65f,.28f,0));
        Check(skill.TryAttack(),"Normal attack starts");
        Check(skill.TryDash() && skill.CurrentCast==EveCast.Dash,"Dash cancels attack"); yield return Idle();
        Place(origin); Target(origin+new Vector3(.65f,.28f,0));
        Check(skill.TrySkill(),"Skill starts");
        Check(skill.TryDash() && skill.CurrentCast==EveCast.Dash,"Dash cancels Skill");yield return Idle();
        skill.Empowerment.TryConsume();skill.SkillRecovery.ResetToFull();
        Place(origin);Target(origin+new Vector3(2.5f,.28f,0));
        double health=dummy.GetCurrent(PNContentIds.Health);
        Check(!skill.WeaponRig.BladeRenderer.enabled,"Sword hidden while idle");
        Check(skill.TrySkill(),"Real nondamaging Skill rush starts");
        yield return new WaitForSeconds(.065f);yield return Capture("Skill-middle");
        Check(body.position.x-origin.x>.5f,"Skill actually moves on Rigidbody during rush");
        yield return Idle();yield return Capture("Skill-after");
        Check(body.position.x-origin.x>2f && body.position.x-origin.x<2.35f,"Skill approaches target: delta="+(body.position.x-origin.x).ToString("F3"));
        Check(dummy.GetCurrent(PNContentIds.Health)==health,"Skill rush deals no damage");
        Check(!skill.WeaponRig.BladeRenderer.enabled,"Skill rush leaves sword hidden");
        Place(origin);Target(origin+new Vector3(2.5f,.28f,0));
        Check(skill.TryAttack() && skill.CurrentPhase==EveCastPhase.Rush,"Empowered attack rush starts before slash");
        yield return new WaitForSeconds(.06f);yield return Capture("Empowered-middle");
        Check(body.position.x-origin.x>.5f && !skill.WeaponRig.BladeRenderer.enabled,"Empowered attack moves before sword appears");
        while(skill.CurrentPhase==EveCastPhase.Rush)yield return null;
        Check(skill.WeaponRig.BladeRenderer.enabled && skill.AttackVisual!=null,"Sword appears for empowered slash after rush");
        yield return new WaitForSeconds(.1f);yield return Capture("Empowered-slash");yield return Idle();
        Check(body.position.x-origin.x>1.9f && !skill.WeaponRig.BladeRenderer.enabled,"Empowered rush finishes and sword hides after attack");
        yield return new WaitForSeconds(1.1f);
        skill.Empowerment.TryConsume(); Place(origin); Target(origin+new Vector3(.65f,.28f,0));
        for(int i=0;i<3;i++)
        {
            int gauge=skill.BurstGauge.Current;
            Check(skill.TryAttack(),"Normal combo action "+i);
            yield return new WaitForSeconds(.025f); yield return Capture("Attack-"+i+"-windup");
            yield return new WaitForSeconds(.08f); yield return Capture("Attack-"+i+"-swing");
            yield return new WaitForSeconds(.06f); yield return Capture("Attack-"+i+"-followthrough");
            var completedTrail=skill.AttackVisual;
            yield return Idle();
            if(i==0)
            {
                Check(completedTrail!=null && completedTrail.GetComponent<MeshFilter>().sharedMesh.vertexCount>0,"Completed slash retains visible afterglow without keeping cast active");
                yield return Capture("Attack-afterglow");
                yield return new WaitForSeconds(.5f);
                Check(completedTrail==null,"Completed slash cleans up after particles and afterglow finish");
            }
            Check(skill.BurstGauge.Current==gauge+2,"Normal combo hit grants exactly two gauge, action "+i);
        }
        Check(skill.WeaponRig.GetComponentsInChildren<SpriteRenderer>().Length==0,"Weapon contains no sword sprite");
        Check(skill.TryAttack(),"Depth review attack starts");yield return new WaitForSeconds(.12f);
        yield return CaptureDepthViews();yield return Idle();
        skill.BurstGauge.Add(150);Check(skill.TryBurst(),"Burst starts with six stacks");
        yield return new WaitForSeconds(.08f);yield return Capture("Burst-activation");
        yield return new WaitForSeconds(.8f);yield return Capture("Burst-idle");
        var motes=skill.GetComponentsInChildren<ParticleSystem>().Single(x=>x.name=="Burst rising motes");
        var particles=new ParticleSystem.Particle[motes.main.maxParticles];int particleCount=motes.GetParticles(particles);
        Check(particleCount>=4 && particles.Take(particleCount).All(x=>x.velocity.y>0),"Burst emits upward light particles from body");
        Check(!skill.WeaponRig.BladeRenderer.enabled,"Burst alone does not reveal sword");
        Check(skill.SlashStacks.Current==6,"Burst grants six stacks");
        int burstGauge=skill.BurstGauge.Current;
        Check(skill.TryRepeatAttack(),"Repeat consumes a stack");yield return new WaitForSeconds(.13f);yield return Capture("Burst-repeat");yield return Idle();
        Check(skill.SlashStacks.Current==5 && skill.BurstGauge.Current==burstGauge,"Repeat gives no stacks or gauge");
        reviewCamera=Camera.main;originalFov=reviewCamera.fieldOfView;
        brain=reviewCamera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        if(brain!=null){brainEnabled=brain.enabled;brain.enabled=false;}
        reviewCamera.fieldOfView=65;
        skill.UltimateRecovery.ResetToFull();Check(skill.TryUltimate(),"Burst ultimate starts");
        int charges=skill.DashRecovery.Current;
        Check(!skill.TryDash() && skill.DashRecovery.Current==charges,"Ultimate rejects Dash without consuming charge");
        yield return new WaitForSeconds(.7f);yield return Capture("Ultimate-charge");
        int frames=0;
        while(skill.IsBusy)
        {
            if(skill.CurrentPhase==EveCastPhase.Slash && skill.AttackVisual!=null && skill.AttackVisual.Progress>.25f && skill.AttackVisual.Progress<.65f && frames<3)
            {yield return Capture("Ultimate-slash-"+frames);frames++;yield return new WaitForSeconds(.22f);}
            else yield return null;
        }
        Check(!skill.IsInBurst && skill.SlashStacks.Current==0 && skill.BurstGauge.Current==burstGauge+30,"Three-hit finisher clears Burst/stacks and refunds thirty");
        yield return Capture("Burst-ended");
        Status="PASS "+checks+" actual scene Play Mode checks; real Rigidbody Dash and Game View captures.";
        Debug.Log(Status);
    }
    private IEnumerator CaptureDepthViews()
    {
        var camera=Camera.main;var controller=camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        bool enabled=controller!=null && controller.enabled;float fov=camera.fieldOfView,time=Time.timeScale;
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        var bladeRotation=skill.WeaponRig.BladeRenderer.transform.rotation;
        Check(Vector3.Distance(skill.WeaponRig.GripPosition,skill.transform.position)>.28f,"Light blade origin is outside entity center");
        Check(skill.AttackVisual.LiveParticles>=5,"Slash emits clearly visible light particles");
        Check(skill.WeaponRig.BladeRenderer.transform.localScale==Vector3.one,"Physical sword remains fixed size during slash");
        Check(Vector3.Distance(skill.WeaponRig.EnergyBlade.TransformPoint(Vector3.right),skill.WeaponRig.BladeTip)<.001f,"Energy layer extends to actual slash tip");
        try
        {
            Time.timeScale=0;if(controller!=null)controller.enabled=false;
            yield return Capture("Blade-3D-gameplay");
            camera.transform.position=skill.transform.position+new Vector3(.55f,.8f,-2.1f);
            camera.transform.LookAt(skill.transform.position+new Vector3(.55f,.2f,0));camera.fieldOfView=48;
            yield return Capture("Blade-3D-side");
            Check(Quaternion.Angle(bladeRotation,skill.WeaponRig.BladeRenderer.transform.rotation)<.001f,
                "Changing camera angle does not rotate or flatten energy blade");
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;
            if(controller!=null)controller.enabled=enabled;Time.timeScale=time;
        }
    }
}
#endif
