[System.Serializable]
public class ShakeData
{
    public float amplitude  = 1f; // Độ mạnh
    public float frequency  = 1f; // Tốc độ rung
    public float duration   = 0.3f; // Thời gian
    public float fadeOutTime = 0.1f; // Thời gian trở về ban đầu

    public ShakeData() { }
    public ShakeData(float amplitude, float frequency, float duration, float fadeOutTime = 0.1f)
    {
        this.amplitude   = amplitude;
        this.frequency   = frequency;
        this.duration    = duration;
        this.fadeOutTime = fadeOutTime;
    }
}