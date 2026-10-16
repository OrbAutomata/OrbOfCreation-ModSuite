using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using OrbModding.Common;
using UnityEngine;

namespace OrbModConfig;

internal sealed class ConfigEditValue
{
    public ConfigEditValue(ConfigSettingDescriptor setting)
    {
        Setting = setting;
        OriginalSerialized = setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
        ExternalSerialized = OriginalSerialized;
    }

    public ConfigSettingDescriptor Setting { get; }
    public string OriginalSerialized { get; private set; }
    public string StagedSerialized { get; private set; }
    public string ExternalSerialized { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public bool IsDirty => !string.Equals(OriginalSerialized, StagedSerialized, StringComparison.Ordinal);
    public bool HasExternalConflict { get; private set; }
    public bool IsValid => Error.Length == 0 && !HasExternalConflict;
    public bool IsEditable => Setting.Kind != ConfigEditorKind.Unsupported;

    public void Stage(string serialized)
    {
        if (!IsEditable)
        {
            Error = "This setting type is read-only.";
            return;
        }

        StagedSerialized = serialized;
        Error = ConfigValueValidator.Validate(Setting, serialized);
        if (HasExternalConflict &&
            string.Equals(StagedSerialized, ExternalSerialized, StringComparison.Ordinal))
            TakeExternalValue();
    }

    public void StageDefault()
    {
        Stage(Setting.DefaultSerializedValue);
    }

    public void Revert()
    {
        OriginalSerialized = Setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
        ExternalSerialized = OriginalSerialized;
        Error = string.Empty;
        HasExternalConflict = false;
    }

    public void AcceptAppliedValue()
    {
        OriginalSerialized = Setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
        ExternalSerialized = OriginalSerialized;
        Error = string.Empty;
        HasExternalConflict = false;
    }

    public bool RefreshExternalValue()
    {
        var current = Setting.Source.GetSerializedValue();
        if (string.Equals(current, ExternalSerialized, StringComparison.Ordinal))
        {
            return false;
        }

        ExternalSerialized = current;
        if (!IsDirty || string.Equals(current, StagedSerialized, StringComparison.Ordinal))
        {
            OriginalSerialized = current;
            StagedSerialized = current;
            Error = ConfigValueValidator.Validate(Setting, current);
            HasExternalConflict = false;
        }
        else
        {
            HasExternalConflict = !string.Equals(current, OriginalSerialized, StringComparison.Ordinal);
        }
        return true;
    }

    public void KeepStagedValue()
    {
        if (!HasExternalConflict) return;
        OriginalSerialized = ExternalSerialized;
        HasExternalConflict = false;
    }

    public void TakeExternalValue()
    {
        OriginalSerialized = ExternalSerialized;
        StagedSerialized = ExternalSerialized;
        Error = ConfigValueValidator.Validate(Setting, ExternalSerialized);
        HasExternalConflict = false;
    }
}

internal sealed class ConfigEditSession
{
    private readonly Dictionary<ConfigEntryBase, ConfigEditValue> _values;

    public ConfigEditSession(ConfigCatalogSnapshot catalog)
    {
        _values = catalog.Mods
            .SelectMany(mod => mod.Sections)
            .SelectMany(section => section.Settings)
            .ToDictionary(setting => setting.Source, setting => new ConfigEditValue(setting));
    }

    public IEnumerable<ConfigEditValue> Values => _values.Values;
    public bool IsDirty => _values.Values.Any(value => value.IsDirty);
    public bool IsValid => _values.Values.All(value => value.IsValid);
    public bool IsModDirty(ModConfigDescriptor mod) => ValuesFor(mod).Any(value => value.IsDirty);
    public bool IsModValid(ModConfigDescriptor mod) => ValuesFor(mod).All(value => value.IsValid);
    public int CountModDirty(ModConfigDescriptor mod) => ValuesFor(mod).Count(value => value.IsDirty);
    public int CountModConflicts(ModConfigDescriptor mod) =>
        ValuesFor(mod).Count(value => value.HasExternalConflict);
    public bool ModHasExternalConflicts(ModConfigDescriptor mod) =>
        ValuesFor(mod).Any(value => value.HasExternalConflict);

    public ConfigEditValue Get(ConfigSettingDescriptor setting) => _values[setting.Source];

    public bool DependencySatisfied(ConfigSettingDescriptor setting)
    {
        for (var index = 0; index < setting.Dependencies.Count; index++)
        {
            var required = setting.Dependencies[index];
            var dependency = FindDependency(setting, required);
            if (dependency is null ||
                !string.Equals(dependency.StagedSerialized, required.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public string DescribeUnsatisfiedDependencies(ConfigSettingDescriptor setting)
    {
        var descriptions = new List<string>();
        for (var index = 0; index < setting.Dependencies.Count; index++)
        {
            var required = setting.Dependencies[index];
            var dependency = FindDependency(setting, required);
            if (dependency is not null &&
                string.Equals(dependency.StagedSerialized, required.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = dependency?.Setting.DisplayName ?? $"{required.Section}.{required.Key}";
            descriptions.Add($"{name} = {required.ExpectedValue}");
        }
        return descriptions.Count == 0
            ? string.Empty
            : "Requires " + string.Join(" and ", descriptions);
    }

    private ConfigEditValue? FindDependency(
        ConfigSettingDescriptor setting,
        ModConfigDependency required) =>
        _values.Values.FirstOrDefault(value =>
            ReferenceEquals(value.Setting.Source.ConfigFile, setting.Source.ConfigFile) &&
            string.Equals(value.Setting.SourceSection, required.Section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.Setting.Key, required.Key, StringComparison.OrdinalIgnoreCase));

    public void RevertAll()
    {
        foreach (var value in _values.Values)
        {
            value.Revert();
        }
    }

    public void Revert(ModConfigDescriptor mod)
    {
        foreach (var value in ValuesFor(mod))
            value.Revert();
    }

    public bool RefreshExternalValues()
    {
        var changed = false;
        foreach (var value in _values.Values)
        {
            changed |= value.RefreshExternalValue();
        }

        return changed;
    }

    public bool Apply(out string error) => Apply(out error, out _);

    public bool Apply(
        out string error,
        out IReadOnlyList<ConfigSettingDescriptor> appliedSettings)
        => ApplyValues(_values.Values.ToArray(), out error, out appliedSettings);

    public bool Apply(
        ModConfigDescriptor mod,
        out string error,
        out IReadOnlyList<ConfigSettingDescriptor> appliedSettings) =>
        ApplyValues(ValuesFor(mod).ToArray(), out error, out appliedSettings);

    private static bool ApplyValues(
        IReadOnlyList<ConfigEditValue> candidates,
        out string error,
        out IReadOnlyList<ConfigSettingDescriptor> appliedSettings)
    {
        error = string.Empty;
        appliedSettings = Array.Empty<ConfigSettingDescriptor>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].IsDirty)
                candidates[index].RefreshExternalValue();
        }

        var conflict = candidates.FirstOrDefault(value => value.HasExternalConflict);
        if (conflict is not null)
        {
            error = $"{conflict.Setting.Section}.{conflict.Setting.Key} changed outside this edit session. Choose Keep mine or Take live.";
            return false;
        }

        var invalid = candidates.FirstOrDefault(value => !value.IsValid);
        if (invalid is not null)
        {
            error = $"{invalid.Setting.Section}.{invalid.Setting.Key}: {invalid.Error}";
            return false;
        }

        var dirty = candidates.Where(value => value.IsDirty).ToArray();
        if (dirty.Length == 0)
        {
            return true;
        }

        var originals = dirty.ToDictionary(value => value, value => value.Setting.Source.GetSerializedValue());
        try
        {
            foreach (var value in dirty)
            {
                value.Setting.Source.SetSerializedValue(value.StagedSerialized);
            }

            foreach (var configFile in dirty.Select(value => value.Setting.Source.ConfigFile).Distinct())
            {
                configFile.Save();
            }

            foreach (var value in dirty)
            {
                value.AcceptAppliedValue();
            }

            var committed = new ConfigSettingDescriptor[dirty.Length];
            for (var index = 0; index < dirty.Length; index++)
            {
                committed[index] = dirty[index].Setting;
            }
            appliedSettings = committed;
            return true;
        }
        catch (Exception ex)
        {
            foreach (var pair in originals)
            {
                try
                {
                    pair.Key.Setting.Source.SetSerializedValue(pair.Value);
                }
                catch
                {
                }
            }

            foreach (var configFile in dirty.Select(value => value.Setting.Source.ConfigFile).Distinct())
            {
                try
                {
                    configFile.Save();
                }
                catch
                {
                }
            }

            error = ex.GetBaseException().Message;
            return false;
        }
    }

    private IEnumerable<ConfigEditValue> ValuesFor(ModConfigDescriptor mod)
    {
        if (mod is null) throw new ArgumentNullException(nameof(mod));
        return _values.Values.Where(value =>
            string.Equals(value.Setting.PluginGuid, mod.Guid, StringComparison.Ordinal));
    }
}

internal static class ConfigValueValidator
{
    public static string Validate(ConfigSettingDescriptor setting, string serialized)
    {
        if (setting.Kind == ConfigEditorKind.Unsupported)
        {
            return "This setting type is read-only.";
        }

        if (setting.Kind == ConfigEditorKind.String)
        {
            return string.Empty;
        }

        if (setting.Kind == ConfigEditorKind.KeyboardShortcut)
        {
            return TryParseKeyboardShortcut(serialized)
                ? string.Empty
                : "Expected a keyboard shortcut such as X + LeftAlt, or None.";
        }

        if (!TryParse(setting.SettingType, serialized, out var parsed))
        {
            return $"Expected {FriendlyTypeName(setting.SettingType)}.";
        }

        var acceptable = setting.Source.Description.AcceptableValues;
        if (acceptable is not null)
        {
            var isValid = acceptable.GetType().GetMethod(
                "IsValid",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(object) },
                null);
            try
            {
                if (isValid?.Invoke(acceptable, new[] { parsed }) is false)
                {
                    return acceptable.ToDescriptionString();
                }
            }
            catch
            {
                return acceptable.ToDescriptionString();
            }
        }

        return string.Empty;
    }

    private static bool TryParseKeyboardShortcut(string serialized)
    {
        var parts = serialized.Split('+');
        if (parts.Length == 0) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index].Trim();
            if (part.Length == 0 ||
                !Enum.TryParse(part, ignoreCase: true, out KeyCode key) ||
                !Enum.IsDefined(typeof(KeyCode), key))
                return false;
        }
        return true;
    }

    private static bool TryParse(Type type, string serialized, out object? value)
    {
        value = null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (type == typeof(bool))
            {
                if (!bool.TryParse(serialized, out var result))
                {
                    return false;
                }

                value = result;
                return true;
            }

            if (type.IsEnum)
            {
                value = Enum.Parse(type, serialized, true);
                return true;
            }

            value = Convert.ChangeType(serialized, type, CultureInfo.InvariantCulture);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The panel's copy of the vocabulary the MCP wire speaks, so a player who mistypes a value
    /// reads one word for one fact wherever they meet it: "Expected int." in the Mods tab and
    /// "must parse exactly as int" over MCP, never "Expected Int32." in one of them.
    /// </summary>
    /// <remarks>
    /// Mirrored rather than shared, and its twin is
    /// <c>GameMcpConfigurationValuePolicy.SettingTypeWord</c>: that one is compiled only under
    /// <c>SERVICE_CYCLE_PROFILE</c> and this panel ships in release builds, so the panel cannot
    /// call it without either taking the profiler into release or losing the fix there. Keep the
    /// two lists in step. The fallback is where they part on purpose: the twin throws, because a
    /// suite setting the wire has no word for is a defect worth catching when the schema is built,
    /// while this panel edits any loaded plugin's config and a third-party setting of an unmapped
    /// type must not turn a typo into an exception the player cannot type their way out of.
    /// </remarks>
    internal static string SettingTypeWord(Type settingType)
    {
        var type = Nullable.GetUnderlyingType(settingType) ?? settingType;
        if (type.IsEnum) return type.Name;
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(string)) return "string";
        return type.Name;
    }

    private static string FriendlyTypeName(Type type)
    {
        return type.IsEnum ? string.Join(", ", Enum.GetNames(type)) : SettingTypeWord(type);
    }
}
