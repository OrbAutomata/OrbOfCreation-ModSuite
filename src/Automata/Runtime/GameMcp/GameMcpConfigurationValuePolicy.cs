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
                GameMcpConfigurationAddress.OnTheWire(entry.Definition) +
                " must parse exactly as " + FriendlyTypeName(entry.SettingType);
            return false;
        }
        if (parsed is float single && !float.IsFinite(single))
        {
            reason =
                GameMcpConfigurationAddress.OnTheWire(entry.Definition) +
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
                GameMcpConfigurationAddress.OnTheWire(entry.Definition) +
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
                GameMcpConfigurationAddress.OnTheWire(entry.Definition) +
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
    private static bool TryReadActionQueueCapacity(GameWorldState? world, out int capacity) =>
        GameMcpWorldQuery.TryReadActionQueueCapacity(world, out capacity);

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
        else if ((Nullable.GetUnderlyingType(entry.SettingType) ?? entry.SettingType) ==
            typeof(bool))
            domain = "one of: yes, no";
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
                if (!TryParseBoolean(serialized, out var boolean)) return false;
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

    /// <summary>
    /// A boolean however the caller spelled it: the <c>yes</c>/<c>no</c> every read on this wire
    /// prints, and the <c>true</c>/<c>false</c> the config file and .NET use. A caller handing back
    /// exactly what it just read is never refused, and neither is one typing the spelling it knows
    /// from the file.
    /// </summary>
    private static bool TryParseBoolean(string serialized, out bool value)
    {
        var text = serialized.Trim();
        if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        return bool.TryParse(text, out value);
    }

    /// <summary>
    /// The text BepInEx is handed for an admitted value. The wire spells a boolean <c>yes</c>/
    /// <c>no</c>; the config file is TOML and spells the same fact <c>true</c>/<c>false</c>, so the
    /// file's spelling is restored here rather than leaving a value this policy already admitted to
    /// be refused a second time by the serializer beneath it.
    /// </summary>
    internal static string NativeSerializedValue(Type settingType, string serializedValue)
    {
        if (settingType is null) throw new ArgumentNullException(nameof(settingType));
        var type = Nullable.GetUnderlyingType(settingType) ?? settingType;
        if (type != typeof(bool)) return serializedValue;
        return TryParseBoolean(serializedValue, out var boolean)
            ? boolean ? "true" : "false"
            : serializedValue;
    }

    private static bool Is(ConfigEntryBase entry, string section, string key) =>
        string.Equals(entry.Definition.Section, section, StringComparison.Ordinal) &&
        string.Equals(entry.Definition.Key, key, StringComparison.Ordinal);

    /// <summary>
    /// The one place a setting's type becomes a word on the wire. Every surface that names a type —
    /// <c>mode="describe"</c> and the refusal a mistyped value earns — asks here, so the wire speaks
    /// one vocabulary rather than each caller's own spelling of the same fact.
    /// </summary>
    /// <remarks>
    /// The words are the ones a player writing a value would use, not the runtime's: a setting that
    /// takes <c>true</c> is a <c>bool</c>, never <c>System.Boolean</c>. An enum keeps its own name
    /// because that name is a suite concept the caller already reads elsewhere, and the values it
    /// accepts are listed beside it — but the namespace it happens to live in is not a player fact,
    /// so it is dropped. An unmapped type is a defect rather than something to pass through: a
    /// writable setting the suite cannot spell must not reach the wire wearing .NET's word for it.
    /// </remarks>
    internal static string SettingTypeWord(Type settingType)
    {
        if (settingType is null) throw new ArgumentNullException(nameof(settingType));
        var type = Nullable.GetUnderlyingType(settingType) ?? settingType;
        if (type.IsEnum) return type.Name;
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(string)) return "string";
        throw new InvalidOperationException(
            "no wire word is declared for writable setting type '" +
            (settingType.FullName ?? settingType.Name) +
            "'; declare one in GameMcpConfigurationValuePolicy.SettingTypeWord before making a " +
            "setting of that type writable");
    }

    private static string FriendlyTypeName(Type type) =>
        type.IsEnum ? string.Join(", ", Enum.GetNames(type)) : SettingTypeWord(type);
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
