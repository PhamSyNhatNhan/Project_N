using UnityEngine;

public class EasyBreathEffect : MonoBehaviour
{
    [Header("Lên xuống")]
    public float amplitude = 0.1f;
    public float frequency = 1f;

    [Header("Thở (to nhỏ)")]
    public float scaleAmplitude = 0.08f;
    public float scaleFrequency = 1f;
    public bool  syncWithFloat  = true;

    [Header("Điều khiển")]
    public bool playOnStart = true;

    private bool    _isPlaying;
    private Vector3 _startPos;
    private Vector3 _startScale;

    void Awake()
    {
        _startPos   = transform.localPosition;
        _startScale = transform.localScale;
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    void Update()
    {
        if (!_isPlaying) return;

        float sinFloat = Mathf.Sin(Time.time * frequency);
        float sinScale = syncWithFloat ? sinFloat : Mathf.Sin(Time.time * scaleFrequency);

        transform.localPosition = _startPos + new Vector3(0, sinFloat * amplitude, 0);
        transform.localScale    = _startScale * (1f + sinScale * scaleAmplitude);
    }

    public void Play()   => _isPlaying = true;
    public void Stop()   => _isPlaying = false;
    public void Toggle() => _isPlaying = !_isPlaying;

    public void Reset()
    {
        Stop();
        transform.localPosition = _startPos;
        transform.localScale    = _startScale;
    }

    public void SetAmplitude(float v)      => amplitude      = v;
    public void SetFrequency(float v)      => frequency      = v;
    public void SetScaleAmplitude(float v) => scaleAmplitude = v;
    public void SetScaleFrequency(float v) => scaleFrequency = v;

    public bool IsPlaying => _isPlaying;
}