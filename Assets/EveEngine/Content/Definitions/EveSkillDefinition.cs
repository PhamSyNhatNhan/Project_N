using System;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // EN: Registered skill content; the game owns the payload's schema and meaning.
    // VI: Nội dung skill được đăng ký; game quyết định cấu trúc và ý nghĩa dữ liệu.
    public class EveSkillDefinition : EveIIdentified
    {
        private readonly object data;
        public virtual EveId Id { get; }

        // EN: Data may be null. JSON is copied on access; other code objects must be immutable.
        // VI: Data có thể null. JSON được sao chép khi đọc; object code khác phải bất biến.
        public virtual object Data => data is JToken json ? json.DeepClone() : data;

        public EveSkillDefinition(EveId id, object data = null)
        {
            if (!id.IsValid) throw new ArgumentException("Skill ID is required.", nameof(id));
            Id = id;
            this.data = data is JToken json
                ? (json.Type == JTokenType.Null ? null : json.DeepClone()) : data;
        }
    }
}
