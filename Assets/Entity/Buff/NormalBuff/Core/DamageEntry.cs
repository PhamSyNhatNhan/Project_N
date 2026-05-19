/// <summary>
/// 1 entry damage — dùng trong StatusEffect.
/// </summary>
[System.Serializable]
public class DamageEntry
{
    public DamageType Type;
    public float      Amount;

    public DamageEntry(DamageType type, float amount)
    {
        Type   = type;
        Amount = amount;
    }
}