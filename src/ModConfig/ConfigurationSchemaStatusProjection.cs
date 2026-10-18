using OrbModding.Common;

namespace OrbModConfig;

internal readonly struct ConfigurationSchemaStatusProjection
{
    private ConfigurationSchemaStatusProjection(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public static ConfigurationSchemaStatusProjection Build(
        string pluginId,
        IConfigurationSchemaStatusSource statuses)
    {
        if (!statuses.TryGet(pluginId, out var status))
        {
            return new ConfigurationSchemaStatusProjection(
                "Configuration schema: Not reported; saved: Unknown; loaded: Unknown.");
        }

        var saved = status.Saved ? "Yes" : "No";
        var loaded = status.Loaded ? "Yes" : "No";
        var backup = status.BackupCreated ? "; backup created: Yes" : string.Empty;
        var state = status.State switch
        {
            ConfigurationSchemaState.Current => $"Current {status.ToVersion}",
            ConfigurationSchemaState.Migrated => $"Migrated {status.FromVersion} to {status.ToVersion}",
            ConfigurationSchemaState.Failed => $"Failed {status.FromVersion} to {status.ToVersion}",
            ConfigurationSchemaState.Future => $"Future {status.FromVersion}; supported: {status.ToVersion}",
            _ => "Unknown",
        };
        var reason = status.State == ConfigurationSchemaState.Current
            ? string.Empty
            : " " + status.Reason;
        return new ConfigurationSchemaStatusProjection(
            $"Configuration schema: {state}; saved: {saved}; loaded: {loaded}{backup}.{reason}");
    }
}
