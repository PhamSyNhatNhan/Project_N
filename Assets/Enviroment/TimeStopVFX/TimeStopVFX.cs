using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Quản lý visual effect khi time stop.
/// Subscribe OnSlowApply/Remove key "timestop".
/// Điều khiển: Post Process Volume (tint tím, vignette, chromatic aberration) + Particle System.
/// </summary>
public class TimeStopVFX : MonoBehaviour
{
    // ── Post Processing ───────────────────────────────────────────
    [Header("Post Processing")]
    [SerializeField] private Volume          postProcessVolume;
    [SerializeField] private float           fadeInDuration  = 0.1f;
    [SerializeField] private float           fadeOutDuration = 0.3f;

    // ── Particle ──────────────────────────────────────────────────
    [Header("Particle")]
    [SerializeField] private ParticleSystem  dustParticle;

    // ── Runtime ───────────────────────────────────────────────────
    private ColorAdjustments    colorAdj;
    private Vignette            vignette;
    private ChromaticAberration chromaticAb;

    private float   targetWeight  = 0f;
    private float   currentWeight = 0f;
    private bool    isFading      = false;
    private Coroutine fadeCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (postProcessVolume != null)
        {
            postProcessVolume.profile.TryGet(out colorAdj);
            postProcessVolume.profile.TryGet(out vignette);
            postProcessVolume.profile.TryGet(out chromaticAb);
        }

        postProcessVolume.weight = 0f;
        dustParticle?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnEnable()
    {
        EventManager.Time.OnSlowApply .Get().AddListener(OnSlowApply);
        EventManager.Time.OnSlowRemove.Get().AddListener(OnSlowRemove);
    }

    private void OnDisable()
    {
        EventManager.Time.OnSlowApply .Get().RemoveListener(OnSlowApply);
        EventManager.Time.OnSlowRemove.Get().RemoveListener(OnSlowRemove);
    }

    // ── Handlers ──────────────────────────────────────────────────
    private void OnSlowApply(Component sender, object data)
    {
        if (data is not SlowData slowData) return;
        if (slowData.Key != "timestop") return;

        FadeTo(1f, fadeInDuration);
        dustParticle?.Play();
    }

    private void OnSlowRemove(Component sender, object data)
    {
        if (data is not SlowData slowData) return;
        if (slowData.Key != "timestop") return;

        FadeTo(0f, fadeOutDuration);
        dustParticle?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ── Fade ──────────────────────────────────────────────────────
    private void FadeTo(float target, float duration)
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeCoroutine(target, duration));
    }

    private IEnumerator FadeCoroutine(float target, float duration)
    {
        float start   = postProcessVolume.weight;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            postProcessVolume.weight = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        postProcessVolume.weight = target;
        fadeCoroutine = null;
    }
}