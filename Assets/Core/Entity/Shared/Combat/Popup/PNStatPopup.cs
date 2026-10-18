#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

public partial class PNStat
{
    [Header("Damage popup")]
    [SerializeField] private bool showDamagePopups = true;
    [SerializeField] private PNDamagePopup damagePopupPrefab;
    [SerializeField] private Vector3 damagePopupOffset = new Vector3(0f, .55f, 0f);
    [SerializeField] private Color physicalPopupColor = new Color(.15f, .9f, 1f);
    [SerializeField] private Color magicPopupColor = new Color(1f, .3f, .9f);
    [SerializeField] private Color truePopupColor = Color.white;
    private PNDamagePopupPool damagePopupPool;
    public virtual bool CanShowDamagePopup => showDamagePopups;
    public PNDamagePopup DamagePopupPrefab { get => damagePopupPrefab; set { RetireDamagePopupPool(); damagePopupPrefab = value; } }
    public Vector3 DamagePopupOffset { get => damagePopupOffset; set => damagePopupOffset = value; }
    public PNDamagePopupPool DamagePopupPool => damagePopupPool;
#if UNITY_EDITOR
    [NonSerialized] public bool PreviewDamagePopupsInEditor;
#endif
    protected virtual Color DamagePopupColor(PNDamageResult result)
    {
        var type = ((PNStatSet)Set).ResolveDamageType(result.Damage.Tags);
        Color color = type.Id.Path == "physical" ? physicalPopupColor : type.Id.Path == "magic" ? magicPopupColor : truePopupColor;
        return color;
    }
    protected virtual void ShowDamagePopup(PNDamageResult result)
    {
        if (!CanShowDamagePopup || !isActiveAndEnabled) return;
#if UNITY_EDITOR
        if (!Application.isPlaying && !PreviewDamagePopupsInEditor) return;
#endif
        // The prefab is only used to create the pool (the pool keeps its own reference), so a live pool needs no lookup.
        // A pool whose prefab was destroyed re-runs the validation/fallback resolution as before. The pool keeps the prefab it was created
        // with, so once its idle popups are used up it still instantiates that destroyed prefab (the previous error path): not recovery.
        if (damagePopupPool == null || !damagePopupPool.PrefabAlive)
        {
            PNDamagePopup prefab;
            using (EveHotPathMetrics.Measure("popup.prefab-resolve"))
                prefab = damagePopupPrefab != null ? damagePopupPrefab : Resources.Load<GameObject>("PNUI/PNDamagePopup")?.GetComponent<PNDamagePopup>();
            if (prefab == null) throw new InvalidOperationException("Missing Resources/PNUI/PNDamagePopup prefab.");
            if (damagePopupPool == null)
                using (EveHotPathMetrics.Measure("popup.pool-create")) damagePopupPool = PNDamagePopupPool.Create(this, prefab, result.Damage.SessionId);
        }
        EveId[] selected;
        using (EveHotPathMetrics.Measure("popup.select-elements"))
        {
            var elements = ((PNStatSet)Set).Registry.Elements();
            var tags = result.Damage.Tags;
            var scratch = PNDamagePopup.ElementScratch();
            for (int i = 0; i < tags.Count; i++)
            {
                var id = tags[i];
                if (elements.TryGet(id, out _)) scratch.Add(elements.ResolveIdentity(id));
            }
            selected = PNDamagePopup.DistinctSorted(scratch);
        }
        damagePopupPool.Show(result.CalculatedDamage, transform.position + damagePopupOffset, DamagePopupColor(result),
            GetComponent<EveEngine.EveTimeScale>(), result.IsCritical, selected);
    }
    private void RetireDamagePopupPool()
    {
        var pool = damagePopupPool; damagePopupPool = null;
        if (pool != null) pool.Retire();
    }
    public void PrepareWorldPresentationChange()=>RetireDamagePopupPool();
    protected virtual void OnDestroy() => RetireDamagePopupPool();
}
#endif
