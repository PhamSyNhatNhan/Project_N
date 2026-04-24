using System;
using UnityEngine;

/// <summary>
/// Tự disable sau khi animation chạy xong — gọi callback khi hoàn tất.
/// Gắn lên fxSpawn.
/// </summary>
/// 
[RequireComponent(typeof(TimeScale))]
public class AutoDisableNotify : MonoBehaviour
{
    private float     duration;
    private float     timer;
    private TimeScale timeScale;

    public event Action OnFinished;

    private void Awake()
    {
        timeScale = GetComponent<TimeScale>();

        var anim = GetComponent<Animator>();
        if (anim != null)
        {
            var clips = anim.runtimeAnimatorController?.animationClips;
            if (clips != null && clips.Length > 0)
                duration = clips[0].length;
        }

        if (duration <= 0f)
        {
            var ps = GetComponent<ParticleSystem>();
            if (ps != null)
                duration = ps.main.duration;
        }
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
        {
            gameObject.SetActive(false);
            OnFinished?.Invoke();
        }
    }
}