using UnityEngine;

[RequireComponent(typeof(TimeScale))]
public class AutoDisable : MonoBehaviour
{
    private float     duration;
    private float     timer;
    private TimeScale timeScale;

    private void Awake()
    {
        timeScale = GetComponent<TimeScale>();

        var anim = GetComponent<Animator>();
        if (anim == null) return;

        var clips = anim.runtimeAnimatorController?.animationClips;
        if (clips != null && clips.Length > 0)
            duration = clips[0].length;
    }

    private void OnEnable()
    {
        timer = duration;
    }

    private void Update()
    {
        if (timer <= 0f) return;

        timer -= timeScale.DeltaTime;
        if (timer <= 0f)
            gameObject.SetActive(false);
    }
}