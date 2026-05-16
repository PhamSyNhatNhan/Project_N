using UnityEngine;

public class FloatEffectForPole : MonoBehaviour
{
    public float amplitude = 30f;  // biên độ (px)
    public float frequency = 1f;   // tốc độ lắc

    Vector3 startPos;

    void Awake()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        float y = Mathf.Sin(Time.time * frequency) * amplitude;
        transform.localPosition = startPos + new Vector3(0, y, 0);
    }
}
