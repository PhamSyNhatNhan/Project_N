#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;

// EN: `project_n:solizeitastronhringur` (Docs/InstanceLifecyclePlan.md 3.4): in one world instance only one area carrying this trait lives at a time. When a new one is made (by anyone, in the same
//     world instance) every other running one carrying it ends with `Replaced`: the normal End path, so the old area plays its own Ending. No refund, no message; areas of other worlds are not touched.
//     Two made in the same frame: the queue orders them, the one handled later wins.
// VI: Trong mot world instance chi mot area mang trait nay song cung luc. Khi cai moi duoc tao (boi bat ky ai, cung world instance) moi cai khac dang chay mang trait nay ket thuc bang `Replaced`:
//     duong End binh thuong, cai cu choi Ending cua no. Khong hoan lai, khong thong bao; area o world khac khong bi dong.
public sealed class PNSolizeitastronhringurTrait : PNAreaTrait
{
    public override void OnCreated(PNAreaTraitContext context)
    {
        var running = new List<PNAreaHandle>();
        context.CollectRunning(running);
        foreach (var handle in running)
            if (handle.InstanceId != context.Area.InstanceId) context.End(handle, PNAreaEndReason.Replaced);
    }
}
#else
// Metadata only in standalone contract checks; native Unity owns the hooks.
public sealed class PNSolizeitastronhringurTrait { }
#endif
