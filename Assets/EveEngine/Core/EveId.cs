using System;

namespace EveEngine
{
    // EN: Stable namespaced ID for definitions, events and contribution sources.
    // VI: ID có namespace dùng cho định nghĩa, event và nguồn đóng góp chỉ số.
    public readonly struct EveId : IEquatable<EveId>, IComparable<EveId>
    {
        public const int MaxNamespaceLength = 64;
        public const int MaxPathLength = 256;
        public string Namespace { get; }
        public string Path { get; }
        public bool IsValid => Namespace != null && Path != null;

        public EveId(string nameSpace, string path)
        {
            Validate(nameSpace, nameof(nameSpace), false, MaxNamespaceLength);
            Validate(path, nameof(path), true, MaxPathLength);
            Namespace = nameSpace;
            Path = path;
        }

        public static EveId Parse(string text)
        {
            return TryParse(text, out EveId id) ? id
                : throw new FormatException($"Invalid EveId '{text}'; expected namespace:path.");
        }

        public static bool TryParse(string text, out EveId id)
        {
            id = default;
            if (string.IsNullOrEmpty(text)) return false;
            int separator = text.IndexOf(':');
            if (separator <= 0 || separator == text.Length - 1 || text.IndexOf(':', separator + 1) >= 0)
                return false;
            string nameSpace = text.Substring(0, separator);
            string path = text.Substring(separator + 1);
            if (!IsValidSegment(nameSpace, false, MaxNamespaceLength) ||
                !IsValidSegment(path, true, MaxPathLength)) return false;
            id = new EveId(nameSpace, path);
            return true;
        }

        private static void Validate(string value, string parameter, bool allowSlash, int maxLength)
        {
            if (!IsValidSegment(value, allowSlash, maxLength))
                throw new ArgumentException($"Invalid ID segment '{value}' (max {maxLength} characters).", parameter);
        }

        private static bool IsValidSegment(string value, bool allowSlash, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength) return false;
            if (!allowSlash && (value == "." || value == "..")) return false;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || (allowSlash && c == '/')))
                    return false;
            return true;
        }

        public bool Equals(EveId other) => Namespace == other.Namespace && Path == other.Path;
        public override bool Equals(object obj) => obj is EveId other && Equals(other);
        public override int GetHashCode() => unchecked(((Namespace?.GetHashCode() ?? 0) * 397) ^ (Path?.GetHashCode() ?? 0));
        public int CompareTo(EveId other)
        {
            int result = string.CompareOrdinal(Namespace, other.Namespace);
            return result != 0 ? result : string.CompareOrdinal(Path, other.Path);
        }
        public override string ToString() => IsValid ? $"{Namespace}:{Path}" : "<invalid>";
        public static bool operator ==(EveId left, EveId right) => left.Equals(right);
        public static bool operator !=(EveId left, EveId right) => !left.Equals(right);
    }
}
