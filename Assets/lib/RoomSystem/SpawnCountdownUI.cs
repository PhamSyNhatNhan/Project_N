using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Gắn trên Canvas — hiện đếm ngược trước khi wave đầu spawn.
/// Chỉ chạy 1 lần khi nhận OnSpawnCountdown.
/// </summary>
public class SpawnCountdownUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnSpawnCountdown.Get().AddListener(HandleCountdown);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnSpawnCountdown.Get().RemoveListener(HandleCountdown);
    }

    private void Start()
    {
        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
    }

    // ── Handler ───────────────────────────────────────────────────
    private void HandleCountdown(Component sender, object data)
    {
        if (data is not float duration) return;
        StartCoroutine(CountdownCoroutine(duration));
    }

    // ── Countdown ─────────────────────────────────────────────────
    private IEnumerator CountdownCoroutine(float duration)
    {
        if (countdownText == null) yield break;

        countdownText.gameObject.SetActive(true);

        float remaining = duration;
        while (remaining > 0f)
        {
            countdownText.text = Mathf.CeilToInt(remaining).ToString();
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        countdownText.text = "";
        countdownText.gameObject.SetActive(false);
    }
}