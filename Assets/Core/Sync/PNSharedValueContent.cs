using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: JSON of the shared value store inside `extensions.project_n.sharedValues` (Docs/SkillNetworkFrameworkPlan.md 4.7, G5): an array of
//     { "id", "scope": "world" | "session", "kind": "number" | "flag" | "text" | "list", "item" (lists), "initial", "persist" (default false), "min", "max" }.
//     Parsed into a pending dictionary without touching the hub, checked with the rest of the batch, committed atomically. The ids must belong to the pack, like every other definition.
// VI: JSON của kho giá trị dùng chung trong `extensions.project_n.sharedValues`. Nằm chung batch với Buff/Visual/Roguelite: parse vào từ điển chờ, kiểm cùng batch, commit nguyên tử.
public sealed partial class PNBuffContentBatch
{
    public static readonly string[] SharedFields = { "sharedValues" };
    private readonly Dictionary<EveId, PNSharedValueDefinition> sharedValues = new Dictionary<EveId, PNSharedValueDefinition>();

    private IEnumerable<EveIIdentified> SharedDefinitions => sharedValues.Values;

    private void CommitShared() { foreach (var definition in sharedValues.Values) hub.SharedValues().Register(definition); }

    private void ParseShared(JObject root, EveContentExtensionDocument document)
    {
        bool any = false;
        foreach (var token in Array(root["sharedValues"]))
        {
            any = true;
            var obj = Object(token);
            var id = Owned(String(obj, "id"), document);
            if (sharedValues.ContainsKey(id) || hub.SharedValues().TryGet(id, out _)) throw new FormatException("Duplicate shared value " + id);
            sharedValues.Add(id, PNSharedValueDefinition.Parse(obj, id));
        }
        // Each scope is one sync block: the keys of a scope, those already registered and those of this batch, stay within the bound.
        if (any) checks.Add(() =>
        {
            foreach (PNSharedScope scope in new[] { PNSharedScope.World, PNSharedScope.Session })
            {
                int count = hub.SharedValues().Snapshot().Values.Count(x => x.Scope == scope) + sharedValues.Values.Count(x => x.Scope == scope);
                if (count > PNSharedLimits.KeysPerScope) throw new FormatException("A scope holds at most " + PNSharedLimits.KeysPerScope + " shared values (" + scope + " has " + count + ").");
            }
        });
    }
}
