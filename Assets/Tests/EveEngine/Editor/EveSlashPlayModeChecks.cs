#if UNITY_EDITOR
using UnityEngine;

// EN: Historical entry point now runs the current Eve input integration check.
// VI: Điểm chạy cũ chuyển sang kiểm tra input của bộ skill Eve hiện tại.
public static class EveSlashPlayModeChecks
{
    public const string ResultPath = "F:/unity/Project_N/.utmp/EveMovesetInputResult.txt";
    public static void Begin() => Debug.Log(EveMovesetPlayModeChecks.Run());
}
#endif
