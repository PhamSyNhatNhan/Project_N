using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class PopupText : MonoBehaviour
{
    private float deactivateTime = 0.5f;
    private Vector3 randomIntensity = new Vector3(0.2f, 0.1f, 0.0f);

    private TimeScale timeScale;

    /// <summary>Gọi từ Stat trước khi SetParent(null) để cache TimeScale</summary>
    public void SetTimeScale(TimeScale ts)
    {
        timeScale = ts;
    }

    private void OnEnable()
    {
        RandomizePosition();
        StartCoroutine(DeactivateAfterDelay());
    }

    private void RandomizePosition()
    {
        // Dùng world position vì parent là null
        transform.position += new Vector3(
            Random.Range(-randomIntensity.x, randomIntensity.x),
            Random.Range(0.3f, 0.3f + randomIntensity.y),
            0f
        );
    }

    private IEnumerator DeactivateAfterDelay()
    {
        float elapsed = 0f;
        while (elapsed < deactivateTime)
        {
            elapsed += timeScale != null ? timeScale.DeltaTime : Time.deltaTime;
            yield return null;
        }
        gameObject.SetActive(false);
    }
}