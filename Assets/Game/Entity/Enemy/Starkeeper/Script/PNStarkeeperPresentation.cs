#if UNITY_5_3_OR_NEWER
using UnityEngine;

// The Starkeeper's golden spear. Presentation only: it hovers beside the puppet on the actor's own clock (a pause or time stop freezes it), and while a cast runs the cast
// Action drives it (BeginSweep / BeginPoint, SetAim, SetAge, EndPose) so the spear draws the same arcs and thrust the hitboxes use. It never decides gameplay.
[DefaultExecutionOrder(200)]
public sealed class PNStarkeeperPresentation : MonoBehaviour
{
    [SerializeField] private Transform weapon;
    [SerializeField, Min(0f)] private float hoverHeight = .02f, hoverWidth = .01f;
    [SerializeField, Min(.1f)] private float hoverPeriod = 2.6f;
    [SerializeField, Min(0f)] private float rockDegrees = 6f;
    private enum Pose { None, Sweep, Point }
    private PNVisualBody body;
    private PNVisualActor owner;
    private float phase, age, windup, second, release;
    private Pose pose;
    private Vector3 aim;
    public bool HasWeapon => weapon != null;
    // The spear's blade tip in world space (local +Y of the mesh, 0.5 m long, origin at the middle diamond).
    public Vector3 TipPosition => weapon != null ? weapon.TransformPoint(new Vector3(0, .28f, 0)) : transform.position + Vector3.up * .3f;

    public void BeginSweep(float windupSeconds, float gapSeconds, Vector3 direction) { pose = Pose.Sweep; age = 0; windup = windupSeconds; second = gapSeconds; aim = direction; if (isActiveAndEnabled) TickWeapon(0); }
    public void BeginPoint(float windupSeconds, float lungeSeconds, Vector3 direction) { pose = Pose.Point; age = 0; windup = windupSeconds; second = lungeSeconds; aim = direction; if (isActiveAndEnabled) TickWeapon(0); }
    public void SetAim(Vector3 direction) { if (pose != Pose.None) aim = direction; }
    public void SetAge(float seconds) { if (pose != Pose.None) age = seconds; }
    public void EndPose() { if (pose != Pose.None) release = 1f; pose = Pose.None; if (isActiveAndEnabled) TickWeapon(0); }

    private void Awake() { body = GetComponent<PNVisualBody>(); }
    private void LateUpdate()
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        if (body != null && body.Actor != null) TickWeapon(body.Actor.ClockDelta());
    }
    private void OnDisable() { owner = null; phase = 0f; pose = Pose.None; release = 0f; }

    // A fast swing from `from` to `to` degrees over `seconds` starting at `start`, eased out; before start it holds `from`, after the end `to`.
    private static float Swing(float t, float start, float seconds, float from, float to) => Mathf.Lerp(from, to, 1f - Mathf.Pow(1f - Mathf.Clamp01((t - start) / seconds), 3f));

    public void TickWeapon(float dt)
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        var actor = body != null ? body.Actor : null;
        if (weapon == null || actor == null) return;
        if (owner != actor) { owner = actor; phase = 0f; }
        phase = Mathf.Repeat(phase + Mathf.Max(0f, dt) * Mathf.PI * 2f / Mathf.Max(.1f, hoverPeriod), Mathf.PI * 2f);
        float side = actor.FacingLeft ? -1 : 1, lift = Mathf.Sin(phase), drift = Mathf.Sin(phase * 2f);
        var position = actor.transform.TransformPoint(new Vector3(-side * (.22f + hoverWidth * drift), .2f + hoverHeight * lift, -.025f));
        float angle = side * (18f + rockDegrees * Mathf.Sin(phase + .45f));
        release = Mathf.Max(0f, release - Mathf.Max(0f, dt) * 5f);                 // after a cast the spear drifts back to its rest place instead of snapping
        float engage = pose != Pose.None ? Mathf.Clamp01(age / .2f) : release;
        if (engage > 0f) position = Vector3.Lerp(position, actor.transform.TransformPoint(new Vector3(side * .1f, .2f, -.03f)), Mathf.SmoothStep(0, 1, engage));   // a cast brings it in front of the body
        if (pose != Pose.None)
        {
            var local = actor.transform.InverseTransformDirection(aim);
            float toward = Mathf.Atan2(-local.x, local.y) * Mathf.Rad2Deg;           // the blade (+Y) turned toward the aim
            var forward = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;
            if (pose == Pose.Sweep)
            {
                // cock behind the shoulder through the windup, then two fast arcs: forward through the aim at the first hit, back at the second
                float back = toward - side * 75f, front = toward + side * 75f, cock = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / Mathf.Max(.01f, windup)));
                float second1 = windup + second;
                if (age < windup) { angle = Mathf.LerpAngle(angle, back, cock); position -= forward * .05f * cock; }
                else if (age < second1) { angle = Swing(age, windup, .12f, back, front); position += forward * (.08f * Mathf.Exp(-(age - windup) * 6f) - .01f); }
                else { angle = Swing(age, second1, .12f, front, back); position += forward * (.08f * Mathf.Exp(-(age - second1) * 6f) - .01f); }
            }
            else
            {
                // point: aim at the star, pull back and tremble through the windup, thrust in the lunge, hold the thrust, then let the cast end release it
                float progress = Mathf.Clamp01(age / Mathf.Max(.01f, windup)), pull = Mathf.SmoothStep(0, 1, progress);
                float tremble = age < windup ? Mathf.Sin(age * 60f) * .004f * Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .75f) / .25f)) : 0f;
                angle = Mathf.LerpAngle(angle, toward, pull);
                position += forward * (-.07f * pull + tremble);
                if (age >= windup) position += forward * (.16f * Mathf.Clamp01((age - windup) / Mathf.Max(.01f, second)) + .07f);
            }
        }
        weapon.position = position;
        weapon.rotation = actor.transform.rotation * Quaternion.Euler(0, 0, angle);
    }
}
#endif
