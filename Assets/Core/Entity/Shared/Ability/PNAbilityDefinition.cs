using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public enum PNAbilityKind { Cooldown, Counter, State }
public enum PNAbilityClock { Game, Entity, EntityWithoutMinimum }
public enum PNAbilityRecovery { PerStack, AllAtOnce }
public enum PNAbilityLifetime { Shared, PerGrant }
public enum PNAbilityReapply { Keep, Refresh, Extend, Independent }
public enum PNAbilityCooldownStart { OnActivate, OnEnd }

// EN: Inspector/code inputs. Runtime copies these into a validated immutable definition.
// VI: Đầu vào Inspector/code; runtime sao chép thành định nghĩa bất biến đã kiểm tra.
[Serializable]
public sealed class PNAbilitySettings
{
    public string Id;
    public PNAbilityKind Kind;
    public PNAbilityClock Clock;
    public bool ShowInUI;
    public string IconPath = "";
    public int DisplayOrder;
    public int Maximum = 1;
    public int InitialAmount;
    public bool AutoRecover = true;
    public double Cooldown;
    public PNAbilityRecovery Recovery;
    public bool HasDuration;
    public double Duration = 10;
    public PNAbilityLifetime Lifetime;
    public bool InitiallyActive;
    public PNAbilityReapply Reapply;
    public PNAbilityCooldownStart CooldownStart;

    public PNAbilityDefinition Create() => new PNAbilityDefinition(this);
}

// EN: Entity state configuration only: no action, effect, UI or skill dependency.
// VI: Chỉ cấu hình trạng thái entity; không phụ thuộc action, hiệu ứng, UI hay skill.
public sealed class PNAbilityDefinition
{
    public EveId Id { get; }
    public PNAbilityKind Kind { get; }
    public PNAbilityClock Clock { get; }
    public bool ShowInUI { get; }
    public string IconPath { get; }
    public int DisplayOrder { get; }
    public int Maximum { get; }
    public int InitialAmount { get; }
    public bool AutoRecover { get; }
    public double Cooldown { get; }
    public PNAbilityRecovery Recovery { get; }
    public double? Duration { get; }
    public PNAbilityLifetime Lifetime { get; }
    public bool InitiallyActive { get; }
    public PNAbilityReapply Reapply { get; }
    public PNAbilityCooldownStart CooldownStart { get; }

    public PNAbilityDefinition(PNAbilitySettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        Id = EveId.Parse(settings.Id);
        ShowInUI = settings.ShowInUI; IconPath = EveResourcePath.Validate(settings.IconPath);
        DisplayOrder = settings.DisplayOrder;
        RequireEnum(settings.Kind); RequireEnum(settings.Clock); RequireEnum(settings.Recovery);
        RequireEnum(settings.Lifetime); RequireEnum(settings.Reapply); RequireEnum(settings.CooldownStart);
        RequireSeconds(settings.Cooldown, true);
        if (settings.HasDuration) RequireSeconds(settings.Duration, false);
        if (settings.Kind != PNAbilityKind.State && (settings.Maximum <= 0 || settings.InitialAmount < 0 || settings.InitialAmount > settings.Maximum))
            throw new ArgumentOutOfRangeException(nameof(settings.Maximum), "Amount must be within a positive maximum.");
        if (settings.Kind == PNAbilityKind.Cooldown && settings.HasDuration)
            throw new ArgumentException("Cooldown abilities cannot have stack duration.");
        if (settings.Kind == PNAbilityKind.Counter && settings.Cooldown != 0)
            throw new ArgumentException("Counter abilities do not recover automatically.");
        Kind = settings.Kind; Clock = settings.Clock;
        Maximum = Kind == PNAbilityKind.State ? 0 : settings.Maximum;
        InitialAmount = Kind == PNAbilityKind.State ? 0 : settings.InitialAmount;
        AutoRecover = Kind == PNAbilityKind.Cooldown && settings.AutoRecover;
        Cooldown = settings.Cooldown; Recovery = settings.Recovery;
        Duration = settings.HasDuration ? settings.Duration : (double?)null;
        Lifetime = settings.Lifetime; InitiallyActive = settings.InitiallyActive;
        Reapply = settings.Reapply; CooldownStart = settings.CooldownStart;
    }

    internal static void RequireSeconds(double value, bool allowZero)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || (allowZero ? value < 0 : value <= 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Time must be finite and nonnegative (duration must be positive).");
    }
    internal static void RequireEnum<T>(T value) where T : struct
    { if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(nameof(value)); }

    // EN: Direct percentage; negative values lengthen cooldown, 100+ removes it.
    // VI: Phần trăm trực tiếp; giá trị âm kéo dài hồi chiêu, 100+ loại bỏ hồi chiêu.
    internal static double CalculateCooldown(double seconds, double flat, double percent)
    {
        RequireSeconds(seconds, true); RequireSeconds(flat, true);
        if (double.IsNaN(percent) || double.IsInfinity(percent))
            throw new ArgumentOutOfRangeException(nameof(percent));
        double value = seconds * (1 - Math.Min(100d, percent) / 100d);
        if (double.IsInfinity(value)) throw new OverflowException("Adjusted cooldown must be finite.");
        return Math.Max(0d, value - flat);
    }

    // EN: Strict standalone JSON array, independent of skill/action envelopes.
    // VI: Mảng JSON độc lập, kiểm tra chặt; không cần khung skill/action.
    public static IReadOnlyList<PNAbilityDefinition> Parse(string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));
        JToken root;
        try { root = JToken.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
        catch (Newtonsoft.Json.JsonException error) { throw new FormatException("Invalid ability JSON.", error); }
        if (!(root is JArray array)) throw new FormatException("Ability definitions must be a JSON array.");
        var result = new List<PNAbilityDefinition>();
        var ids = new HashSet<EveId>();
        foreach (var token in array)
        {
            if (!(token is JObject item)) throw new FormatException("Ability definition must be an object.");
            var kind = ReadEnum(item, "kind", PNAbilityKind.Cooldown, true);
            string[] common = { "id", "kind", "clock", "showInUI", "iconPath", "displayOrder" };
            string[] fields = kind == PNAbilityKind.Cooldown
                ? new[] { "maximum", "initialAmount", "autoRecover", "cooldown", "recovery" }
                : kind == PNAbilityKind.Counter
                    ? new[] { "maximum", "initialAmount", "duration", "lifetime" }
                    : new[] { "initiallyActive", "duration", "cooldown", "cooldownStart", "reapply" };
            foreach (var property in item.Properties())
                if (!common.Concat(fields).Contains(property.Name)) throw new FormatException("Unknown ability field: " + property.Name);
            if (item["id"]?.Type != JTokenType.String) throw new FormatException("Ability id is required.");
            if (item["showInUI"]?.Type != JTokenType.Boolean) throw new FormatException("Ability showInUI is required and must be boolean.");
            if (item["iconPath"] != null && item["iconPath"].Type != JTokenType.String)
                throw new FormatException("Ability iconPath must be a Resources path string.");
            var input = new PNAbilitySettings
            {
                Id = item["id"].Value<string>(), Kind = kind,
                ShowInUI = item["showInUI"].Value<bool>(), IconPath = item["iconPath"]?.Value<string>(),
                DisplayOrder = ReadInt(item, "displayOrder", 0),
                Clock = ReadEnum(item, "clock", PNAbilityClock.Game),
                Maximum = ReadInt(item, "maximum", 1), InitialAmount = ReadInt(item, "initialAmount", 0),
                AutoRecover = ReadBool(item, "autoRecover", true), Cooldown = ReadTime(item, "cooldown", 0),
                Recovery = ReadEnum(item, "recovery", PNAbilityRecovery.PerStack),
                HasDuration = item["duration"] != null, Duration = ReadTime(item, "duration", 10),
                Lifetime = ReadEnum(item, "lifetime", PNAbilityLifetime.Shared),
                InitiallyActive = ReadBool(item, "initiallyActive", false),
                Reapply = ReadEnum(item, "reapply", PNAbilityReapply.Keep),
                CooldownStart = ReadEnum(item, "cooldownStart", PNAbilityCooldownStart.OnActivate)
            };
            var definition = input.Create();
            if (!ids.Add(definition.Id)) throw new FormatException("Duplicate ability id: " + definition.Id);
            result.Add(definition);
        }
        return result.AsReadOnly();
    }
    private static T ReadEnum<T>(JObject item, string field, T fallback, bool required = false) where T : struct
    {
        var token = item[field];
        if (token == null && !required) return fallback;
        if (token?.Type != JTokenType.String || !Enum.TryParse(token.Value<string>(), false, out T value) ||
            !Enum.IsDefined(typeof(T), value) || value.ToString() != token.Value<string>())
            throw new FormatException("Invalid ability enum: " + field);
        return value;
    }
    private static int ReadInt(JObject item, string field, int fallback)
    {
        var token = item[field]; if (token == null) return fallback;
        if (token.Type != JTokenType.Integer) throw new FormatException(field + " must be an integer.");
        long value = token.Value<long>();
        if (value < int.MinValue || value > int.MaxValue) throw new FormatException(field + " is out of range.");
        return (int)value;
    }
    private static bool ReadBool(JObject item, string field, bool fallback)
    {
        var token = item[field]; if (token == null) return fallback;
        if (token.Type != JTokenType.Boolean) throw new FormatException(field + " must be boolean.");
        return token.Value<bool>();
    }
    private static double ReadTime(JObject item, string field, double fallback)
    {
        var token = item[field]; if (token == null) return fallback;
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException(field + " must be numeric.");
        return token.Value<double>();
    }
}
