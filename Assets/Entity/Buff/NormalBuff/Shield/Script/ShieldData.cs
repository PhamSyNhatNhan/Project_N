/// <summary>
/// Data truyền vào ShieldEffect qua AddEffect(type, data).
/// </summary>
public class ShieldData
{
    public float Amount;
    public float Duration;

    public ShieldData(float amount, float duration)
    {
        Amount   = amount;
        Duration = duration;
    }
}