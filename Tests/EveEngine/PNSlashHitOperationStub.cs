using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// EN: Stub runner verifies action transitions only. Real geometry/damage operation is checked in Unity PhysX.
// VI: Runner stub chỉ kiểm tra action transitions. Operation hình học/damage thật kiểm tra bằng Unity PhysX.
public class PNSlashHitOperation : PNSkillOperation
{
    public virtual PNDamage Damage => null;
    public virtual IReadOnlyList<PNDamageResult> Results => Array.AsReadOnly(Array.Empty<PNDamageResult>());
    public override void Execute(PNSkillActionContext context, JToken data) => PNSlashHitSettings.Parse(data);
    public virtual void Advance(float deltaTime) { }
}
