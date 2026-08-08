#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using BepInEx.Configuration;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Validates the exact requested value before BepInEx can clamp it or feature policy can
/// reinterpret malformed text. MCP writes either commit the named value or do not mutate config.
/// </summary>
internal static class GameMcpConfigurationValuePolicy
{
    internal static bool TryValidate(
        ConfigEntryBase entry,
        string serializedValue,
        out string reason,
        out GameMcpConfigurationBound bound)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        bound = GameMcpConfigurationBound.None;
        var serialized = serializedValue ?? string.Empty;
        if (Is(entry, "Reserves", "AbsoluteReserve"))
        {
            if (!double.TryParse(
                    serialized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var reserve) ||
                !double.IsFinite(reserve) ||
                reserve < 0.0)
            {
                reason =
                    "Reserves/AbsoluteReserve must be a finite invariant number " +
                    "greater than or equal to zero";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        if (!TryParse(entry.SettingType, serialized, out var parsed))
        {
            reason =
                entry.Definition.Section + "/" + entry.Definition.Key +
                " must parse exactly as " + FriendlyTypeName(entry.SettingType);
            return false;
        }
        if (parsed is float single && !float.IsFinite(single))
        {
            reason =
                entry.Definition.Section + "/" + entry.Definition.Key +
                " must be finite";
            return false;
        }
        if (parsed is float multiplier &&
            multiplier < 0.0f &&
            Is(entry, "Reserves", "RelativeReserveMultiplier"))
        {
            reason =
                "Reserves/RelativeReserveMultiplier must be greater than or equal to zero";
            return false;
        }
        if (parsed is int leaveQueueSlots &&
            leaveQueueSlots < 0 &&
            Is(entry, "AutoBuy", "LeaveQueueSlots"))
        {
            reason =
                "AutoBuy/LeaveQueueSlots must be greater than or equal to zero";
            return false;
        }
        if (parsed is double doubleValue && !double.IsFinite(doubleValue))
        {
            reason =
                entry.Definition.Section + "/" + entry.Definition.Key +
                " must be finite";
            return false;
        }

        var acceptable = entry.Description.AcceptableValues;
        if (acceptable is not null && !acceptable.IsValid(parsed!))
        {
            // BepInEx describes its own domain for a config file comment, and splicing that text
            // into a refusal made the surface say "must be From 0 to 60". The bound is read as
            // numbers and the sentence is written from those numbers, so both say one thing.
            bound = Bound(acceptable);
            reason =
                entry.Definition.Section + "/" + entry.Definition.Key +
                " must be " + Domain(bound);
            return false;
        }
        reason = string.Empty;
        return true;
    }

    internal static GameMcpConfigurationConstraint Describe(ConfigEntryBase entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        var domain = string.Empty;
        if (Is(entry, "Reserves", "AbsoluteReserve"))
            domain = "finite invariant number >= 0";
        else if (Is(entry, "Reserves", "RelativeReserveMultiplier"))
            domain = "finite float >= 0";
        else if (Is(entry, "AutoBuy", "LeaveQueueSlots"))
            domain = "integer >= 0";
        else if (entry.SettingType.IsEnum)
            domain = "one of: " + string.Join(", ", Enum.GetNames(entry.SettingType));
        return new GameMcpConfigurationConstraint(
            "exact_parse_and_domain",
            entry.Description.AcceptableValues?.ToDescriptionString() ?? string.Empty,
            domain);
    }

    /// <summary>
    /// The declared range, read off the acceptable-value object itself rather than off its own
    /// prose. Every writable entry that declares a domain declares it as a range; nothing here
    /// invents one for a shape the suite does not bind.
    /// </summary>
    private static GameMcpConfigurationBound Bound(AcceptableValueBase acceptable)
    {
        var type = acceptable.GetType();
        var minimum = ReadDouble(type, acceptable, "MinValue");
        var maximum = ReadDouble(type, acceptable, "MaxValue");
        return minimum.HasValue && maximum.HasValue
            ? new GameMcpConfigurationBound(minimum, maximum)
            : GameMcpConfigurationBound.None;
    }

    private static double? ReadDouble(Type type, object instance, string property)
    {
        var value = type.GetProperty(property)?.GetValue(instance);
        if (value is null) return null;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception exception) when (
            exception is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string Domain(in GameMcpConfigurationBound bound) => bound.HasRange
        ? "from " + Text(bound.Minimum!.Value) + " to " + Text(bound.Maximum!.Value)
        : "within its declared domain";

    private static string Text(double value) =>
        value.ToString("0.############", CultureInfo.InvariantCulture);

    private static bool TryParse(Type settingType, string serialized, out object? value)
    {
        value = null;
        var type = Nullable.GetUnderlyingType(settingType) ?? settingType;
        try
        {
            if (type == typeof(string))
            {
                value = serialized;
                return true;
            }
            if (type == typeof(bool))
            {
                if (!bool.TryParse(serialized, out var boolean)) return false;
                value = boolean;
                return true;
            }
            if (type.IsEnum)
            {
                if (!Enum.TryParse(type, serialized, ignoreCase: true, out var enumeration) ||
                    enumeration is null ||
                    !Enum.IsDefined(type, enumeration))
                    return false;
                value = enumeration;
                return true;
            }
            value = Convert.ChangeType(serialized, type, CultureInfo.InvariantCulture);
            return value is not null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidCastException or
                OverflowException)
        {
            return false;
        }
    }

    private static bool Is(ConfigEntryBase entry, string section, string key) =>
        string.Equals(entry.Definition.Section, section, StringComparison.Ordinal) &&
        string.Equals(entry.Definition.Key, key, StringComparison.Ordinal);

    private static string FriendlyTypeName(Type type) =>
        type.IsEnum ? string.Join(", ", Enum.GetNames(type)) : type.Name;
}

/// <summary>
/// The declared domain of one writable setting, in machine facts. A refusal whose sentence names a
/// range carries the same range as fields, so a caller need not parse the sentence to retry.
/// </summary>
internal readonly struct GameMcpConfigurationBound
{
    internal GameMcpConfigurationBound(double? minimum, double? maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    internal double? Minimum { get; }
    internal double? Maximum { get; }
    internal bool HasRange => Minimum.HasValue && Maximum.HasValue;

    internal static GameMcpConfigurationBound None => new(null, null);
}

internal sealed class GameMcpConfigurationConstraint
{
    internal GameMcpConfigurationConstraint(
        string mode,
        string acceptableValues,
        string domain)
    {
        Mode = mode ?? string.Empty;
        AcceptableValues = acceptableValues ?? string.Empty;
        Domain = domain ?? string.Empty;
    }

    internal string Mode { get; }
    internal string AcceptableValues { get; }
    internal string Domain { get; }
}
#endif
