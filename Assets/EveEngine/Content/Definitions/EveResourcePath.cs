using System;

namespace EveEngine
{
    // Content stores portable Resources keys, never Unity asset objects or filesystem paths.
    public static class EveResourcePath
    {
        public static string Validate(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value != value.Trim() || value.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase) ||
                value.IndexOfAny(new[]{'\\', ':', '.'}) >= 0)
                throw new ArgumentException("Use a Resources-relative path without prefix, extension or traversal.");
            foreach(var segment in value.Split('/'))
                if(string.IsNullOrWhiteSpace(segment)) throw new ArgumentException("Empty Resources path segment.");
            return value;
        }
    }
}
