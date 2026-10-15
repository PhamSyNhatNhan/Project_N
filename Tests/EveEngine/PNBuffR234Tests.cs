using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

// R2 (S1) checks: SyncPresentation scratch and group-liveness behavior.
public static class PNBuffR234Tests
{
    private sealed class Counted : PNBuffBehavior
    {
        private int stacks = 1;
        public override bool EventDrivenReplication => true;
        public override double? StackCount => stacks;
        public override bool CanChangeStacks => true;
        public override void OnApply(PNBuffContext c, JToken input) { c.SetDuration(10); Presentation.Publish(new PNBuffNumbers(topRight: stacks)); }
        public override void ChangeStacks(PNBuffContext c, int delta) { stacks += delta; Presentation.Publish(new PNBuffNumbers(topRight: stacks)); }
    }

    public static void Run()
    {
        S1GroupsAndScratch();
    }

    // S1: group order is released when the last instance of an Id ends and assigned afresh on re-add; scratch holds no references.
    private static void S1GroupsAndScratch()
    {
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var first = EveId.Parse("test:r234_g_first"); var second = EveId.Parse("test:r234_g_second");
        hub.Buffs().Register(new PNBuffDefinition(first, EveId.Parse("test:r234_g_first_impl"), _ => new Counted(), null));
        hub.Buffs().Register(new PNBuffDefinition(second, EveId.Parse("test:r234_g_second_impl"), _ => new Counted(), null));
        hub.Seal();
        using (var collection = new PNBuffCollection(hub, Guid.Parse("32345678-1234-1234-1234-123456789abc")))
        {
            var source = new PNBuffSource(collection.SessionId, new EveEntityId(1));
            var a = collection.Apply(new PNBuffApplyRequest(first, source));
            var b = collection.Apply(new PNBuffApplyRequest(second, source));
            var orders = collection.PresentationSnapshot().ToDictionary(x => x.Id, x => x.GroupOrder);
            Check(orders[first] == 1 && orders[second] == 2, "S1 groups are numbered in first-seen order");
            collection.Remove(a.Handle, PNBuffEndReason.Dispelled, true);
            collection.Apply(new PNBuffApplyRequest(first, source));
            var after = collection.PresentationSnapshot().ToDictionary(x => x.Id, x => x.GroupOrder);
            Check(after[second] == 2 && after[first] == 3, "S1 a released group is numbered afresh on re-add");
            foreach (var name in new[] { "writerScratch", "presentationScratch", "groupScratch", "liveGroupScratch" })
            {
                var field = typeof(PNBuffCollection).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var count = (int)field.FieldType.GetProperty("Count").GetValue(field.GetValue(collection));
                Check(count == 0, "S1 scratch " + name + " is empty between syncs");
            }
        }
    }
}
