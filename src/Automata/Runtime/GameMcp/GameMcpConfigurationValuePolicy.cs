#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

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

    /// <summary>
    /// Refuses the one writable value whose safe ceiling is a live game fact rather than a declared
    /// range: reserving the whole action queue leaves Auto Buy no slot it may ever take, so the
    /// feature would report itself on and buy nothing until somebody found this setting again.
    /// </summary>
    internal static bool TryValidateAgainstWorld(
        string section,
        string key,
        string serializedValue,
        GameWorldState? world,
        out string reason,
        out GameMcpConfigurationBound bound)
    {
        bound = GameMcpConfigurationBound.None;
        reason = string.Empty;
        if (!string.Equals(section, "AutoBuy", StringComparison.Ordinal) ||
            !string.Equals(key, "LeaveQueueSlots", StringComparison.Ordinal))
            return true;
        if (!int.TryParse(
                serializedValue ?? string.Empty,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var reserved))
            return true;
        if (!TryReadActionQueueCapacity(world, out var capacity)) return true;
        if (reserved < capacity) return true;
        bound = new GameMcpConfigurationBound(0, capacity - 1, integral: true);
        reason =
            "AutoBuy/LeaveQueueSlots must be from 0 to " + (capacity - 1) +
            "; the action queue holds " + capacity +
            " and reserving all of them leaves Auto Buy no slot to queue into";
        return false;
    }

    /// <summary>
    /// The development queue's declared capacity, off the published world. A capacity nobody has
    /// published yet — no save loaded — is not a ceiling this can hold a write against.
    /// </summary>
    private static bool TryReadActionQueueCapacity(GameWorldState? world, out int capacity)
    {
        capacity = 0;
        if (world is null) return false;
        if (!WorldLookup.TryFind(
                world.ActionQueues, KnownEntities.ActiveActionables.Uuid, out var queue))
            return false;
        if (queue.MaxQueuedItemsId == Guid.Empty ||
            !WorldLookup.TryFind(world.IntVariables, queue.MaxQueuedItemsId, out var maximum))
            return false;
        capacity = maximum.Value.ToInt();
        return capacity > 0;
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
            domain = "integer >= 0, and below the live action-queue capacity";
        else if (entry.SettingType.IsEnum)
            domain = "one of: " + string.Join(", ", Enum.GetNames(entry.SettingType));
        return new GameMcpConfigurationConstraint(
            "exact_parse_and_domain",
            entry.Description.AcceptableValues?.ToDescriptionString() ?? string.Empty,
            domain,
            entry.Description.AcceptableValues is null
                ? GameMcpConfigurationBound.None
                : Bound(entry.Description.AcceptableValues));
    }

    /// <summary>
    /// The refused write restated as facts: which setting, what it was asked to become, and the
    /// domain that refused it. A caller retrying does not have to parse the sentence back apart.
    /// A range states both ends in the type its setting accepts, so an integer setting's ceiling is
    /// a JSON integer rather than the double the bound happens to be carried in.
    /// </summary>
    internal static GameMcpValue RefusalFacts(
        GameMcpCommand command,
        in GameMcpConfigurationBound bound)
    {
        var setting = new GameMcpObjectBuilder
        {
            ["section"] = command.Mode,
            ["key"] = command.PayloadKey,
            ["requestedValue"] = command.PayloadValue,
        };
        AddBound(setting, in bound);
        return new GameMcpObjectBuilder { ["setting"] = setting }.Freeze();
    }

    /// <summary>Writes a declared range onto whatever publishes it, refusal or read alike.</summary>
    internal static void AddBound(
        GameMcpObjectBuilder target,
        in GameMcpConfigurationBound bound)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (!bound.HasRange) return;
        target["minimum"] = Bounded(bound.Minimum!.Value, bound.Integral);
        target["maximum"] = Bounded(bound.Maximum!.Value, bound.Integral);
    }

    private static object Bounded(double value, bool integral) =>
        integral ? (object)(long)value : value;

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
            ? new GameMcpConfigurationBound(minimum, maximum, IsIntegral(acceptable.ValueType))
            : GameMcpConfigurationBound.None;
    }

    private static bool IsIntegral(Type valueType) =>
        (Nullable.GetUnderlyingType(valueType) ?? valueType) is var type &&
        (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong));

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
/// <c>Integral</c> is the setting's own value type, so an integer setting's ceiling ships as a JSON
/// integer rather than as the double this struct happens to hold it in.
/// </summary>
internal readonly struct GameMcpConfigurationBound
{
    internal GameMcpConfigurationBound(double? minimum, double? maximum, bool integral = false)
    {
        Minimum = minimum;
        Maximum = maximum;
        Integral = integral;
    }

    internal double? Minimum { get; }
    internal double? Maximum { get; }
    internal bool Integral { get; }
    internal bool HasRange => Minimum.HasValue && Maximum.HasValue;

    internal static GameMcpConfigurationBound None => new(null, null);
}

internal sealed class GameMcpConfigurationConstraint
{
    internal GameMcpConfigurationConstraint(
        string mode,
        string acceptableValues,
        string domain,
        GameMcpConfigurationBound bound = default)
    {
        Mode = mode ?? string.Empty;
        AcceptableValues = acceptableValues ?? string.Empty;
        Domain = domain ?? string.Empty;
        Bound = bound;
    }

    internal string Mode { get; }
    internal string AcceptableValues { get; }
    internal string Domain { get; }

    /// <summary>
    /// The declared range as numbers, so a caller reading the surface before it writes gets the same
    /// two fields a refused write would hand it back rather than a sentence to parse.
    /// </summary>
    internal GameMcpConfigurationBound Bound { get; }
}
#endif
