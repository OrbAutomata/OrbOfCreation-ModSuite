using BepInEx.Configuration;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuiteConfigurationSchemaSevenTests
{
    [Fact]
    public void SchemaSevenDeletesTheAutoConceptFillFloor()
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "6");
        file.SeedSerialized("AutoConcept", "MinimumResourcePercent", "10");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(6, result.Status.FromVersion);
        Assert.Equal(7, result.Status.ToVersion);
        Assert.False(file.TryGetPersisted("AutoConcept", "MinimumResourcePercent", out _));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.DiscardedObsolete, diagnostic.Kind);
        Assert.Equal("MinimumResourcePercent", diagnostic.Source.Key);
    }
}
