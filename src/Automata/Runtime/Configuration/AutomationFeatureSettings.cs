using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>A feature's declared settings, including their one immutable publication mapping.</summary>
internal sealed class AutomationFeatureSettings
{
    private readonly AutomationFeatureSetting[] _settings;

    internal AutomationFeatureSettings(string description, params AutomationFeatureSetting[] settings)
    {
        Description = description;
        _settings = (AutomationFeatureSetting[])settings.Clone();
        if (_settings.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count() != _settings.Length ||
            _settings.Count(setting => setting.Key == "Mode") != 1)
            throw new ArgumentException("Feature settings require unique keys and exactly one Mode.", nameof(settings));
    }

    internal string Description { get; }
    internal IReadOnlyList<AutomationFeatureSetting> Settings => _settings;

    internal BoundAutomationFeatureSettings Bind(ConfigFile file, AutomataFeatureContribution feature) =>
        new(feature.Id, _settings.Select(setting => setting.Bind(file, feature)).ToArray());

    internal bool TryReadPublishedValue(SuiteRuntimeConfiguration configuration, string key, out object value)
    {
        foreach (var setting in _settings)
        {
            if (!string.Equals(setting.Key, key, StringComparison.Ordinal)) continue;
            value = setting.ReadPublishedValue(configuration);
            return true;
        }
        value = null!;
        return false;
    }
}

/// <summary>Typed binding and projection kept together so a new setting has no second registry.</summary>
internal abstract class AutomationFeatureSetting
{
    protected AutomationFeatureSetting(string key, string label, string description, int order)
    {
        Key = key;
        Label = label;
        Description = description;
        Order = order;
    }

    internal string Key { get; }
    internal string Label { get; }
    internal string Description { get; }
    internal int Order { get; }
    internal abstract object ReadPublishedValue(SuiteRuntimeConfiguration configuration);
    internal abstract BoundAutomationFeatureSetting Bind(ConfigFile file, AutomataFeatureContribution feature);

    internal static AutomationFeatureSetting Define<T>(string key, T defaultValue, string label,
        string description, int order, Func<SuiteRuntimeConfiguration, T> read,
        Func<SuiteRuntimeConfiguration, T, SuiteRuntimeConfiguration> write,
        AcceptableValueBase? acceptableValues = null) =>
        new TypedSetting<T>(key, defaultValue, label, description, order, read, write, acceptableValues);

    private sealed class TypedSetting<T> : AutomationFeatureSetting
    {
        private readonly T _defaultValue;
        private readonly Func<SuiteRuntimeConfiguration, T> _read;
        private readonly Func<SuiteRuntimeConfiguration, T, SuiteRuntimeConfiguration> _write;
        private readonly AcceptableValueBase? _acceptableValues;

        internal TypedSetting(string key, T defaultValue, string label, string description, int order,
            Func<SuiteRuntimeConfiguration, T> read,
            Func<SuiteRuntimeConfiguration, T, SuiteRuntimeConfiguration> write,
            AcceptableValueBase? acceptableValues) : base(key, label, description, order)
        {
            _defaultValue = defaultValue;
            _read = read;
            _write = write;
            _acceptableValues = acceptableValues;
        }

        internal override object ReadPublishedValue(SuiteRuntimeConfiguration configuration) => _read(configuration)!;

        internal override BoundAutomationFeatureSetting Bind(ConfigFile file, AutomataFeatureContribution feature)
        {
            var section = feature.ConfigurationSection ??
                throw new InvalidOperationException("A feature with settings requires a configuration section.");
            if (Key == "Mode" && (!typeof(T).IsEnum ||
                !Enum.IsDefined(typeof(T), "Disabled") || !Enum.IsDefined(typeof(T), "Active")))
                throw new InvalidOperationException("A feature Mode must declare Disabled and Active.");
            var dependencies = Key == "Mode"
                ? Array.Empty<ModConfigDependency>()
                : new[] { new ModConfigDependency(section, "Mode", "Active") };
            var metadata = new ModConfigMetadata(feature.PageOrder, Order, dependencies,
                displaySection: feature.PageLabel, displayName: Key == "Mode" ? feature.DisplayName : Label);
            var entry = file.Bind(section, Key, _defaultValue,
                new ConfigDescription(Description, _acceptableValues, metadata));
            return new BoundAutomationFeatureSetting(entry,
                configuration => _write(configuration, entry.Value));
        }
    }
}

internal sealed class BoundAutomationFeatureSetting
{
    private readonly Func<SuiteRuntimeConfiguration, SuiteRuntimeConfiguration> _read;

    internal BoundAutomationFeatureSetting(ConfigEntryBase entry,
        Func<SuiteRuntimeConfiguration, SuiteRuntimeConfiguration> read)
    {
        Entry = entry;
        _read = read;
    }

    internal ConfigEntryBase Entry { get; }
    internal SuiteRuntimeConfiguration Read(SuiteRuntimeConfiguration configuration) => _read(configuration);
}

/// <summary>Mutable BepInEx entries stay in the host; only projected records cross to workers.</summary>
internal sealed class BoundAutomationFeatureSettings
{
    private readonly BoundAutomationFeatureSetting[] _settings;
    private readonly ConfigEntryBase _mode;

    internal BoundAutomationFeatureSettings(string featureId, BoundAutomationFeatureSetting[] settings)
    {
        FeatureId = featureId;
        _settings = settings;
        _mode = settings.Single(setting => setting.Entry.Definition.Key == "Mode").Entry;
    }

    internal string FeatureId { get; }
    internal IEnumerable<ConfigEntryBase> Entries => _settings.Select(setting => setting.Entry);
    internal void SetMode(bool active) => _mode.SetSerializedValue(active ? "Active" : "Disabled");

    internal SuiteRuntimeConfiguration Read(SuiteRuntimeConfiguration configuration)
    {
        foreach (var setting in _settings) configuration = setting.Read(configuration);
        return configuration;
    }
}
