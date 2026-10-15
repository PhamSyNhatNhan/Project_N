using System;
using System.Threading;

namespace EveEngine
{
    // EN: Identifies one live entity; unrelated to its shared character definition ID.
    // VI: Xác định một entity đang tồn tại; tách khỏi ID định nghĩa nhân vật dùng chung.
    public readonly struct EveEntityId : IEquatable<EveEntityId>
    {
        private static long nextLocal;

        public long Value { get; }
        public bool IsValid => Value != 0;
        public bool IsHostAssigned => Value > 0;

        public EveEntityId(long value)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value), "Entity ID zero is reserved.");
            Value = value;
        }

        // EN: Local IDs are negative; the host owns positive network IDs.
        // VI: ID nội bộ là số âm; host quản lý ID mạng là số dương.
        internal static EveEntityId NewLocal() => new EveEntityId(Interlocked.Decrement(ref nextLocal));

        public bool Equals(EveEntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EveEntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(EveEntityId left, EveEntityId right) => left.Equals(right);
        public static bool operator !=(EveEntityId left, EveEntityId right) => !left.Equals(right);
    }
}
