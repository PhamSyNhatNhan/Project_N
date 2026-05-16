using UnityEngine;
using UnityEngine.UI;

public class ScrollBackground : MonoBehaviour
{
    public float speed = 0.1f;
    RawImage bg;

    void Awake()
    {
        bg = GetComponent<RawImage>();
    }

    void Update()
    {
        bg.uvRect = new Rect(Time.time * speed, 0, 1, 1);
    }
}