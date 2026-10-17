using BepInEx.Configuration;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuiteConfigurationSchemaSixTests
{
    private static readonly (string Section, string Key)[] Retired =
    {
        ("AutoBuy", "EvaluationIntervalSeconds"),
        ("AutoCast", "EvaluationIntervalSeconds"),
        ("AutoHarvest", "EvaluationIntervalSeconds"),
        ("AutoConcept", "FallbackEvaluationIntervalSeconds"),
        ("AutoBuy", "AllowedUuids"),
        ("AutoBuy", "BlockedUuids"),
        ("AutoConcept", "AllowedUuids"),
        ("AutoConcept", "BlockedUuids"),
        ("AutoBuy", "PurchaseGrouping"),
        ("AutoBuy", "FixedGroupSize"),
        ("AutoBuy", "BatchSizingMode"),
        ("AutoBuy", "MaxPurchasesPerBatch"),
        ("AutoBuy", "PrioritizeCostAndQualityStructures"),
        ("AutoConcept", "PerConceptQuantityCap"),
    };

    [Fact]
    public void SchemaSixDeletesEveryRetiredKeyAndMapsFormerDefaultTrainingPeriod()
    {
        var file = VersionFiveFile("300");
        foreach (var (section, key) in Retired)
            file.SeedSerialized(section, key, "player-value");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(5, result.Status.FromVersion);
        Assert.Equal(7, result.Status.ToVersion);
        Assert.Equal(30, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        Assert.Equal(15, result.Diagnostics.Count);
        foreach (var (section, key) in Retired)
        {
            Assert.False(file.TryGetPersisted(section, key, out _));
            Assert.Contains(
                result.Diagnostics,
                diagnostic =>
                    diagnostic.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete &&
                    diagnostic.Source.Section == section &&
                    diagnostic.Source.Key == key &&
                    !string.IsNullOrWhiteSpace(diagnostic.Detail));
        }
        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Kind == ConfigurationMigrationDiagnosticKind.Mapped &&
                diagnostic.Source.Section == "AutoConcept" &&
                diagnostic.Source.Key == "TrainingPeriodSeconds");
        Assert.True(file.TryGetPersisted("AutoConcept", "TrainingPeriodSeconds", out var persisted));
        Assert.Equal("30", persisted);
    }

    [Fact]
    public void SchemaSixMapsExplicitlySavedFormerDefaultTrainingPeriodToThirtySeconds()
    {
        // The file format has no provenance. A deliberately saved 300 is serialized exactly like
        // the former default and therefore follows the same documented schema-6 mapping.
        var result = SuiteConfiguration.TryBind(VersionFiveFile("300"));

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(30, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.Mapped, diagnostic.Kind);
    }

    [Fact]
    public void SchemaSixPreservesCustomizedTrainingPeriod()
    {
        var result = SuiteConfiguration.TryBind(VersionFiveFile("60"));

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(60, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        Assert.Empty(result.Diagnostics);
    }

    private static ConfigFile VersionFiveFile(string trainingPeriodSeconds)
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "5");
        file.SeedSerialized("AutoConcept", "TrainingPeriodSeconds", trainingPeriodSeconds);
        return file;
    }
}
