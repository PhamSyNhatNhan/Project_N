using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Registered request to compile a shader's GPU programs ahead of its first use (docs: Docs/ShaderWarmupPlan.md). Pure data; the runtime part is PNShaderWarmup (Game).
//     A shader that a game system or a mod draws must be registered here, otherwise its first use compiles during gameplay (a visible hitch).
// VI: Yêu cầu đăng ký biên dịch trước chương trình GPU của một shader. Chỉ là dữ liệu; phần chạy nằm ở PNShaderWarmup (Game).
public sealed class PNShaderWarmupDefinition : EveIIdentified
{
    public const int MaximumKeywords = 16;
    public EveId Id { get; }
    public string ShaderName { get; }
    public IReadOnlyList<string> Keywords { get; }
    // Every pass type the shader has is warmed (untagged and SRPDefaultUnlit passes, UniversalForward and custom light modes); the runtime skips the types it does not have.
    public PNShaderWarmupDefinition(EveId id, string shaderName, IEnumerable<string> keywords = null)
    {
        if (!id.IsValid || id.ToString().Length > 128) throw new ArgumentException("Shader warm-up id is invalid.");
        if (string.IsNullOrWhiteSpace(shaderName) || shaderName.Length > 256) throw new ArgumentException("Shader warm-up needs a shader name of at most 256 characters.");
        var keywordList = (keywords ?? Array.Empty<string>()).ToList();
        if (keywordList.Count > MaximumKeywords || keywordList.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64 || x.Any(char.IsWhiteSpace)) || keywordList.Distinct().Count() != keywordList.Count)
            throw new ArgumentException("Shader warm-up keywords must be distinct, non-empty, without spaces, at most " + MaximumKeywords + ".");
        Id = id; ShaderName = shaderName; Keywords = keywordList.AsReadOnly();
    }
}
