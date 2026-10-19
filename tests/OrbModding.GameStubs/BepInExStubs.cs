using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = new Version(version);
        }

        public string GUID { get; }

        public string Name { get; }

        public Version Version { get; }
    }

    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public Configuration.ConfigFile Config { get; } = new Configuration.ConfigFile();

        public Logging.ManualLogSource Logger { get; } = new Logging.ManualLogSource();
    }

    public static class Paths
    {
        public static string GameRootPath { get; set; } = string.Empty;
        public static string ConfigPath { get; set; } = string.Empty;
    }

    public sealed class PluginInfo
    {
        public BepInPlugin Metadata { get; set; } = new BepInPlugin("test.plugin", "Test Plugin", "1.0.0");

        public BaseUnityPlugin? Instance { get; set; }
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, BepInEx.PluginInfo> PluginInfos { get; } = new Dictionary<string, BepInEx.PluginInfo>();
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>
    {
        private readonly Dictionary<ConfigDefinition, ConfigEntryBase> _entries = new Dictionary<ConfigDefinition, ConfigEntryBase>();
        private readonly Dictionary<ConfigDefinition, string> _orphanedEntries = new Dictionary<ConfigDefinition, string>();
        private readonly Dictionary<ConfigDefinition, string> _persistedEntries = new Dictionary<ConfigDefinition, string>();

        public ConfigFile(string configFilePath = "")
        {
            ConfigFilePath = configFilePath;
        }

        public bool SaveOnConfigSet { get; set; } = true;

        public string ConfigFilePath { get; }

        public int SaveCalls { get; private set; }

        public int ReloadCalls { get; private set; }

        public int? ThrowOnSaveCall { get; set; }

        public bool ThrowOnEverySave { get; set; }

        public int? ThrowOnReloadCall { get; set; }

        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        public bool ThrowOnEveryReload { get; set; }

        public ConfigDefinition? ThrowOnBindDefinition { get; set; }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            var definition = new ConfigDefinition(section, key);
            if (definition.Equals(ThrowOnBindDefinition))
            {
                throw new InvalidOperationException($"simulated config bind failure for [{section}] {key}");
            }
            if (_entries.TryGetValue(definition, out var existing))
            {
                return (ConfigEntry<T>)existing;
            }

            var entry = new ConfigEntry<T>(this, definition, defaultValue, description);
            if (_orphanedEntries.Remove(definition, out var serialized)) entry.SetSerializedValue(serialized);
            _entries.Add(definition, entry);
            return entry;
        }

        public void Save()
        {
            SaveCalls++;
            if (ThrowOnEverySave || ThrowOnSaveCall == SaveCalls)
            {
                throw new InvalidOperationException($"simulated config save failure on call {SaveCalls}");
            }

            _persistedEntries.Clear();
            foreach (var pair in _orphanedEntries) _persistedEntries[pair.Key] = pair.Value;
            foreach (var pair in _entries) _persistedEntries[pair.Key] = pair.Value.GetSerializedValue();
        }

        public void Reload()
        {
            ReloadCalls++;
            if (ThrowOnEveryReload || ThrowOnReloadCall == ReloadCalls)
            {
                throw new InvalidOperationException($"simulated config reload failure on call {ReloadCalls}");
            }

            _orphanedEntries.Clear();
            foreach (var pair in _persistedEntries)
            {
                if (_entries.TryGetValue(pair.Key, out var entry)) entry.SetSerializedValue(pair.Value);
                else _orphanedEntries[pair.Key] = pair.Value;
            }
        }

        public void SeedSerialized(string section, string key, string value)
        {
            var definition = new ConfigDefinition(section, key);
            _orphanedEntries[definition] = value;
            _persistedEntries[definition] = value;
        }

        public bool TryGetPersisted(string section, string key, out string value) =>
            _persistedEntries.TryGetValue(new ConfigDefinition(section, key), out value!);

        public bool Remove(ConfigDefinition definition) => _entries.Remove(definition);

        internal void OnSettingChanged(ConfigEntryBase entry) =>
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(entry));

        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(ConfigEntryBase changedSetting) =>
            ChangedSetting = changedSetting;

        public ConfigEntryBase ChangedSetting { get; }
    }

    public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public ConfigDefinition(string section, string key)
        {
            Section = section;
            Key = key;
        }

        public string Section { get; }

        public string Key { get; }

        public bool Equals(ConfigDefinition? other) => other is not null && Section == other.Section && Key == other.Key;

        public override bool Equals(object? obj) => Equals(obj as ConfigDefinition);

        public override int GetHashCode() => HashCode.Combine(Section, Key);
    }

    public abstract class ConfigEntryBase
    {
        protected ConfigEntryBase(ConfigFile configFile, ConfigDefinition definition, Type settingType, object? defaultValue, ConfigDescription description)
        {
            ConfigFile = configFile;
            Definition = definition;
            SettingType = settingType;
            DefaultValue = defaultValue;
            Description = description;
        }

        public ConfigFile ConfigFile { get; }

        public ConfigDefinition Definition { get; }

        public ConfigDescription Description { get; }

        public Type SettingType { get; }

        public object? DefaultValue { get; }

        public abstract object? BoxedValue { get; set; }

        // BepInEx serializes a boolean the way TOML spells one, so every installed config file
        // under BepInEx/config reads "Enabled = true". Convert.ToString says "True", and that one
        // unfaithful character let the suite's own projection disagree with BepInEx for every
        // boolean setting while the test pinning them equal stayed green.
        public string GetSerializedValue() => BoxedValue is bool boolean
            ? (boolean ? "true" : "false")
            : Convert.ToString(BoxedValue, CultureInfo.InvariantCulture) ?? string.Empty;

        public void SetSerializedValue(string value)
        {
            if (SettingType == typeof(string))
            {
                BoxedValue = value;
                return;
            }

            BoxedValue = TomlTypeConverter.ConvertToValue(value, SettingType);
        }
    }

    internal static class TomlTypeConverter
    {
        internal static object ConvertToValue(string value, Type targetType)
        {
            if (targetType == typeof(KeyboardShortcut))
            {
                var keys = value
                    .Split('+')
                    .Select(part =>
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length == 0)
                            throw new FormatException("A keyboard shortcut key cannot be empty.");
                        return Enum.Parse<UnityEngine.KeyCode>(trimmed, ignoreCase: true);
                    })
                    .ToArray();
                if (keys.Length == 0)
                    throw new FormatException("A keyboard shortcut requires a main key.");
                return new KeyboardShortcut(keys[0], keys.Skip(1).ToArray());
            }

            return targetType.IsEnum
                ? Enum.Parse(targetType, value, true)
                : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture)!;
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;

        public ConfigEntry(T value)
            : this(new ConfigFile(), new ConfigDefinition("Test", "Value"), value, new ConfigDescription(string.Empty))
        {
        }

        internal ConfigEntry(ConfigFile configFile, ConfigDefinition definition, T value, ConfigDescription description)
            : base(configFile, definition, typeof(T), value, description)
        {
            _value = value;
        }

        /// <summary>
        /// Writing the value it already holds is not a change, and BepInEx says so before it
        /// stores anything: <c>set_Value</c> compares with <c>System.Object.Equals(object, object)</c>
        /// and returns on equality, so neither the field nor the event moves. A stub that raised
        /// unconditionally made every repeated write look like a committed change and hid the arm
        /// where an accepted write publishes nothing.
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value)) return;
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
                ConfigFile.OnSettingChanged(this);
            }
        }

        public override object? BoxedValue
        {
            get => Value;
            set => Value = (T)value!;
        }

        public event EventHandler? SettingChanged;

        public void RaiseChanged()
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class ConfigDescription
    {
        public ConfigDescription(string description, AcceptableValueBase? acceptableValues = null, params object[] tags)
        {
            Description = description;
            AcceptableValues = acceptableValues;
            Tags = tags;
        }

        public string Description { get; }

        public AcceptableValueBase? AcceptableValues { get; }

        public object[] Tags { get; }
    }

    public abstract class AcceptableValueBase
    {
        protected AcceptableValueBase(Type valueType)
        {
            ValueType = valueType;
        }

        public Type ValueType { get; }

        public abstract object Clamp(object value);

        public abstract bool IsValid(object value);

        public abstract string ToDescriptionString();
    }

    public sealed class AcceptableValueRange<T> : AcceptableValueBase
    {
        public AcceptableValueRange(T minValue, T maxValue)
            : base(typeof(T))
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }

        public T MaxValue { get; }

        public override object Clamp(object value)
        {
            if (value is not T typed)
            {
                return MinValue!;
            }

            if (Comparer<T>.Default.Compare(typed, MinValue) < 0)
            {
                return MinValue!;
            }

            return Comparer<T>.Default.Compare(typed, MaxValue) > 0 ? MaxValue! : typed!;
        }

        public override bool IsValid(object value)
        {
            return value is T typed &&
                   Comparer<T>.Default.Compare(typed, MinValue) >= 0 &&
                   Comparer<T>.Default.Compare(typed, MaxValue) <= 0;
        }

        public override string ToDescriptionString() => $"Range: {MinValue} - {MaxValue}";
    }

    public sealed class AcceptableValueList<T> : AcceptableValueBase
    {
        public AcceptableValueList(params T[] acceptableValues)
            : base(typeof(T))
        {
            AcceptableValues = acceptableValues;
        }

        public IReadOnlyList<T> AcceptableValues { get; }

        public override object Clamp(object value) => IsValid(value) ? value : AcceptableValues[0]!;

        public override bool IsValid(object value) => value is T typed && AcceptableValues.Contains(typed);

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", AcceptableValues)}";
    }

    /// <summary>
    /// Carries the chord it was built from, because code under test compares a persisted shortcut
    /// against the defaults it may have inherited, and a shortcut that remembers nothing makes every
    /// chord equal to every other one.
    /// </summary>
    public readonly struct KeyboardShortcut : IEquatable<KeyboardShortcut>
    {
        private readonly UnityEngine.KeyCode[]? _modifiers;

        public KeyboardShortcut(UnityEngine.KeyCode mainKey, params UnityEngine.KeyCode[] modifiers)
        {
            MainKey = mainKey;
            _modifiers = modifiers;
        }

        public UnityEngine.KeyCode MainKey { get; }

        public IEnumerable<UnityEngine.KeyCode> Modifiers =>
            _modifiers ?? Array.Empty<UnityEngine.KeyCode>();

        public bool IsDown()
        {
            return false;
        }

        public bool Equals(KeyboardShortcut other) =>
            MainKey == other.MainKey &&
            Modifiers.OrderBy(key => key).SequenceEqual(other.Modifiers.OrderBy(key => key));

        public override bool Equals(object? obj) => obj is KeyboardShortcut other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)MainKey;
            foreach (var modifier in Modifiers.OrderBy(key => key)) hash = (hash * 397) ^ (int)modifier;
            return hash;
        }

        public override string ToString() =>
            string.Join(" + ", new[] { MainKey }.Concat(Modifiers));
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<object> Entries { get; } = new List<object>();

        public void LogDebug(object data) => Entries.Add(data);

        public void LogInfo(object data) => Entries.Add(data);

        public void LogWarning(object data) => Entries.Add(data);

        public void LogError(object data) => Entries.Add(data);
    }
}
