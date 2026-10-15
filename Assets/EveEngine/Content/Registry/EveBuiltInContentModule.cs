using System;

namespace EveEngine
{
    // EN: Registers definitions owned by EveEngine before game and mod content.
    // VI: Đăng ký định nghĩa thuộc EveEngine trước nội dung game và mod.
    public class EveBuiltInContentModule : EveIContentModule
    {
        public static readonly EveId MoveSpeed = EveId.Parse("eve:move_speed");
        public static readonly EveId JumpSpeed = EveId.Parse("eve:jump_speed");

        public virtual void Register(EveRegistryHub hub)
        {
            if (hub == null) throw new ArgumentNullException(nameof(hub));
            var stats = hub.GetOrCreate<EveStatDefinition>();
            stats.Register(new EveStatDefinition(MoveSpeed, EveStatKind.Number));
            stats.Register(new EveStatDefinition(JumpSpeed, EveStatKind.Number));
        }
    }
}
