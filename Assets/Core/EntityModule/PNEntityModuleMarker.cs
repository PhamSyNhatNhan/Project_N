#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Which entity modules the factory applied to this entity when it configured it (Docs/EntityModulePlan.md step E3). Only for checking and debugging: nothing reads it to decide anything. The factory resets it every time it
//     configures the object, so a pooled instance never keeps the list of its previous use.
// VI: Mô-đun thực thể nào factory đã áp cho entity này lúc cấu hình. Chỉ để kiểm tra/dò lỗi; không hệ nào đọc để quyết định. Factory đặt lại mỗi lần cấu hình.
public sealed class PNEntityModuleMarker : MonoBehaviour
{
    private readonly List<EveId> applied = new List<EveId>();
    public IReadOnlyList<EveId> Applied => applied;
    public bool Has(EveId module) => applied.Contains(module);
    public override string ToString() => string.Join(",", applied.Select(x => x.ToString()));
    public void Begin() { applied.Clear(); }
    public void Record(EveId module) { applied.Add(module); }
}
#endif
