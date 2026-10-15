#if UNITY_EDITOR
using EveEngine;
using UnityEngine;

// EN: Opt-in Editor verification; never attached to gameplay assets.
// VI: Component kiểm thử Editor chạy tường minh; không gắn vào tài nguyên gameplay.
[ExecuteAlways]
public sealed class EveEasyPoolingProbe : MonoBehaviour, EveIPoolable
{
    public static int Created, Taken, Released, Enabled;
    public static bool FailGet, FailRelease;
    public EveEasyPooling Pool;
    public int State;
    private void OnEnable() => Enabled++;
    public void OnPoolCreated() => Created++;
    public void OnPoolGet(EveEasyPooling pool)
    {
        Pool = pool; Taken++;
        if (FailGet) throw new System.Exception("Expected Get hook failure.");
    }
    public void OnPoolRelease()
    {
        State = 0; Pool = null; Released++;
        if (FailRelease) throw new System.Exception("Expected Release hook failure.");
    }
}
#endif
