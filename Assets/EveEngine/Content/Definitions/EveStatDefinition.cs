using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Distinguishes plain numbers from resources with a current value.
    // VI: Phân biệt chỉ số thường và tài nguyên có giá trị hiện tại.
    public enum EveStatKind { Number, Resource }
    public enum EveStatReplication { None, Everyone, Owner }
    // EN: Determines how current resource value changes when its maximum changes.
    // VI: Quy định cách giá trị hiện tại đổi khi giới hạn tài nguyên thay đổi.
    public enum EveMaxChangePolicy { PreserveCurrent, PreserveRatio }

    // EN: Registered metadata for one stat ID; game-specific names remain data.
    // VI: Định nghĩa được đăng ký cho một ID chỉ số; tên riêng của game nằm trong dữ liệu.
    public class EveStatDefinition : EveIIdentified
    {
        public virtual EveId Id { get; }
        public virtual EveStatKind Kind { get; }
        public virtual EveMaxChangePolicy MaxChangePolicy { get; }
        public virtual EveStatReplication Replication { get; }

        // Preserve the existing three-argument constructor for compiled callers.
        public EveStatDefinition(EveId id, EveStatKind kind, EveMaxChangePolicy maxChangePolicy = EveMaxChangePolicy.PreserveCurrent)
            : this(id, kind, maxChangePolicy, EveStatReplication.None) { }
        public EveStatDefinition(EveId id, EveStatKind kind, EveStatReplication replication)
            : this(id, kind, EveMaxChangePolicy.PreserveCurrent, replication) { }
        public EveStatDefinition(EveId id, EveStatKind kind, EveMaxChangePolicy maxChangePolicy, EveStatReplication replication)
        {
            if (!id.IsValid) throw new ArgumentException("Stat ID is required.", nameof(id));
            if (!Enum.IsDefined(typeof(EveStatReplication), replication) || replication == EveStatReplication.Owner ||
                kind != EveStatKind.Resource && replication != EveStatReplication.None)
                throw new ArgumentException("Unsupported Stat replication policy/kind.", nameof(replication));
            Replication = replication;
            Id = id;
            Kind = kind;
            MaxChangePolicy = maxChangePolicy;
        }
    }
}
