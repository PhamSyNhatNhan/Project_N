using System;
using System.Collections.Generic;

namespace EveEngine
{
    // Optional preparation runs before the loader captures dependency-ordered modules.
    public interface EveIEntityLoadPreparation { void PrepareEntityLoad(); }
    // EN: One entity initialization step run by the loader after content is ready.
    // VI: Một bước khởi tạo entity do loader chạy sau khi nội dung đã sẵn sàng.
    public interface EveIEntityLoadStep
    {
        int LoadOrder { get; }
        IEnumerable<Type> Dependencies { get; }
        void Load(EveEntityLoadContext context);
        void Unload();
    }
    public interface EveIEntityCommitStep
    {
        void CommitEntity(EveEntityLoadContext context);
        void EntityPublished(EveEntityLoadContext context);
    }
}
