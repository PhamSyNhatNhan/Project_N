#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: Puts a rig puppet on an entity whose character declares `body.visual` (Docs/VisualPlan.md P4): the prefab's own sprite stays authored but is switched off, a PNVisualActor
//     child draws the body instead, the capsule takes the build's size (feet stay where they were), the ground shadow follows the build, and the movement/hurt/death of the
//     entity drive the puppet's states (position and replicated health, so a replica needs nothing extra on the wire). A character without `body.visual` is untouched. The factory calls Begin after configuring an entity and Clear when it returns to the pool.
// VI: Gắn con rối rig lên entity có `body.visual`: tắt sprite cũ, vẽ bằng PNVisualActor, capsule theo build, bóng theo build, chuyển động/bị đánh/chết của entity điều khiển trạng thái.
[DisallowMultipleComponent]
public sealed class PNVisualBody : MonoBehaviour
{
    [SerializeField] private Transform visual;          // the authored sprite object, switched off while a puppet is drawn
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private Transform shadow;
    [SerializeField, Min(0f)] private float walkSpeed = .08f, runSpeed = 1.1f;   // m/s of the entity; below walkSpeed it stands

    private PNVisualActor actor; private PNStat stat; private Vector3 lastPosition; private bool hasLast, dead, savedCapsule, savedShadow, hasHealth; private double lastHealth;
    private float baseHeight, baseRadius; private Vector3 baseCenter, baseShadowScale; private string locomotion = "";

    public PNVisualActor Actor => actor;
    public bool Active => actor != null;

    // Returns false (and changes nothing) when the character has no body.visual.
    public bool Begin(EveCharacterDefinition character, EveRegistryHub hub)
    {
        if (character == null) throw new ArgumentNullException(nameof(character)); if (hub == null) throw new ArgumentNullException(nameof(hub));
        if (actor != null) Clear();
        var body = PNBodySettings.Parse(character.Data);
        if (!body.Visual.HasValue) return false;
        var appearance = hub.Get<PNVisualAppearanceDefinition>().Get(body.Visual.Value);
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (visual == null) { var found = transform.Find("Visual"); if (found != null) visual = found; }
        // feet in this object's space: the bottom of the authored capsule (the body keeps standing on the same ground)
        var feet = capsule != null ? capsule.center + Vector3.down * (capsule.height * .5f) : Vector3.zero;
        var child = new GameObject("PN Visual Actor"); child.transform.SetParent(transform, false);
        var created = child.AddComponent<PNVisualActor>();
        try { created.Bind(appearance, hub, feet, body.BuildOverride); }
        catch { DestroyObject(child); throw; }
        actor = created;
        if (visual != null) visual.gameObject.SetActive(false);
        if (capsule != null)
        {
            baseHeight = capsule.height; baseRadius = capsule.radius; baseCenter = capsule.center; savedCapsule = true;
            capsule.height = Mathf.Max(actor.CapsuleHeight, actor.CapsuleRadius * 2f); capsule.radius = actor.CapsuleRadius;
            capsule.center = new Vector3(baseCenter.x, feet.y + capsule.height * .5f, baseCenter.z);
        }
        if (shadow != null) { baseShadowScale = shadow.localScale; savedShadow = true; shadow.localScale = new Vector3(baseShadowScale.x * actor.ShadowScale, baseShadowScale.y * actor.ShadowScale, baseShadowScale.z); }
        stat = GetComponent<PNStat>();
        dead = false; hasLast = false; hasHealth = false; locomotion = "";
        actor.Play("idle");
        return true;
    }

    public void Clear()
    {
        stat = null;
        if (actor != null) { DestroyObject(actor.gameObject); actor = null; }
        if (visual != null) visual.gameObject.SetActive(true);
        if (savedCapsule && capsule != null) { capsule.height = baseHeight; capsule.radius = baseRadius; capsule.center = baseCenter; }
        if (savedShadow && shadow != null) shadow.localScale = baseShadowScale;
        savedCapsule = savedShadow = false; hasLast = false; hasHealth = false; dead = false; locomotion = "";
    }

    private static void DestroyObject(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

    // The movement feed: how fast and which way the entity really moved this frame (position based, so it also works for a replica that is not simulated here).
    private void LateUpdate() => Feed(Time.deltaTime <= 0f ? 0f : Time.deltaTime);
    public void Feed(float dt)
    {
        if (actor == null) return;
        // Health is replicated, so the hurt and death of a replica (no damage events run there) are read from it, the same way on every machine.
        if (stat != null && stat.HasHealth)
        {
            double hp = stat.GetCurrent(PNContentIds.Health); bool nowDead = stat.IsDead;
            if (hasHealth && hp < lastHealth - 1e-6 && !nowDead) actor.Play("hurt", true);
            lastHealth = hp; hasHealth = true;
            if (nowDead && !dead) { dead = true; actor.Play("dead"); }
            else if (!nowDead && dead) { dead = false; locomotion = ""; actor.Play("idle"); }
        }
        if (dead) return;
        var position = transform.position;
        if (hasLast && dt > 1e-5f)
        {
            var delta = position - lastPosition; delta.y = 0f; float speed = delta.magnitude / dt;
            if (Mathf.Abs(delta.x) > 1e-4f) actor.SetFacingLeft(delta.x < 0f);
            string next = speed < walkSpeed ? "idle" : speed < runSpeed ? "walk" : "run";
            if (next != locomotion) { locomotion = next; actor.Play(next); }
        }
        lastPosition = position; hasLast = true;
    }

    private void OnDestroy() => Clear();
}
#endif
