using System.Linq;
using BepInEx.Configuration;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorConfigurationSchemaTests
{
    [Fact]
    public void CurrentSchemaDiscardsLegacyMentorAdmissionControls()
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "3");
        file.SeedSerialized("Performance", "OperationsPerFrame", "8");
        file.SeedSerialized("Performance", "CpuBudgetMilliseconds", "0.75");

        var result = MentorConfig.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(3, result.Status.FromVersion);
        Assert.Equal(7, result.Status.ToVersion);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(
                ConfigurationMigrationDiagnosticKind.DiscardedObsolete,
                diagnostic.Kind));
        Assert.DoesNotContain(
            file,
            pair => pair.Key.Section == "Performance" &&
                    pair.Key.Key is "OperationsPerFrame" or "CpuBudgetMilliseconds");
    }

    [Fact]
    public void CurrentMentorCatalogHasNoLegacyAdmissionControls()
    {
        var file = new ConfigFile();

        Assert.True(MentorConfig.TryBind(file).Success);
        Assert.DoesNotContain(file, pair => pair.Key.Section == "Performance");
    }
}
