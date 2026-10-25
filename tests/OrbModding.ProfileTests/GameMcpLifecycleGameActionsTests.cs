using System;
using BepInEx.Logging;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpLifecycleGameActionsTests
{
    private static readonly Guid RecipeId = Guid.Parse("7b0c1c55-3f8e-4c1e-9a55-2d5f0c6f1a01");

    [Fact]
    public void A_craft_press_with_no_player_crafting_action_composed_is_refused_as_not_composed()
    {
        var lifecycle = 7L;
        using var runtime = AutomataServiceCycleComposition.Create(
            Configuration(),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[]
            {
                HarvestFeature(lifecycle),
            },
            new ManualLogSource());
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, lifecycle, 1, "craft", RecipeId, Guid.Empty,
            "CraftingRecipeSO", 1, string.Empty, string.Empty, false);

        var result = runtime.GameMcp.ExecuteGameMcp(command);

        Assert.Equal("refused", result.Status);
        Assert.Equal("contract_unavailable", result.Code);
        Assert.Equal("the shared player crafting GameAction was not composed", result.Reason);
    }

    [Fact]
    public void The_player_halves_a_feature_registers_are_the_ones_the_runtime_reaches()
    {
        var lifecycle = 7L;
        using var runtime = AutomataServiceCycleComposition.Create(
            Configuration(),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionTimelineRegistry()),
            PlayerFeatures(lifecycle),
            new ManualLogSource());
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Consumable, lifecycle, 1, "discard", RecipeId, Guid.Empty,
            "ConsumableSO", 1, string.Empty, string.Empty, false);

        var consumable = runtime.GameMcp.ExecuteGameMcp(command);
        var facts = runtime.CaptureFrameFacts(includeServices: false);

        Assert.Equal("refused", consumable.Status);
        Assert.Equal("item_unavailable", consumable.Code);
        Assert.Empty(facts.UnavailableGameActions);
    }

    [Fact]
    public void Suite_health_names_each_verb_whose_bindings_failed_and_no_healthy_one()
    {
        var lifecycle = 7L;
        var missing = ResearchNativeBindings.ContractIds[0];
        using var runtime = AutomataServiceCycleComposition.Create(
            Configuration(),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionTimelineRegistry()),
            PlayerFeatures(lifecycle),
            new ManualLogSource(),
            actions => actions.Add("game_research", new ResearchGameAction(
                () => lifecycle, () => true, () => string.Empty,
                includeContract: id => id != missing)));
        var research = runtime.CaptureFrameFacts(includeServices: false);

        var health = GameMcpHealthProjection.ProjectGameMcpHealthText(Context(research, modalFailure: string.Empty));

        Assert.Contains("\ngame_research: unavailable\n", health, StringComparison.Ordinal);
        Assert.Matches(
            "(?m)^game_research reason: .*" + System.Text.RegularExpressions.Regex.Escape(missing),
            health);
        Assert.DoesNotContain("game_craft", health, StringComparison.Ordinal);
        Assert.DoesNotContain("game_consumable", health, StringComparison.Ordinal);
        Assert.DoesNotContain("game_modal", health, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_verb_reads_through_the_same_lines_as_the_runtime_verbs()
    {
        var lifecycle = 7L;
        var missing = ResearchNativeBindings.ContractIds[0];
        using var runtime = AutomataServiceCycleComposition.Create(
            Configuration(),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { HarvestFeature(lifecycle) },
            new ManualLogSource(),
            actions => actions.Add("game_research", new ResearchGameAction(
                () => lifecycle, () => true, () => string.Empty,
                includeContract: id => id != missing)));
        var research = runtime.CaptureFrameFacts(includeServices: false);

        var health = GameMcpHealthProjection.ProjectGameMcpHealthText(
            Context(research, modalFailure: "the panel controls did not bind"));

        Assert.Contains(
            "\ngame_research: unavailable\ngame_modal: unavailable\n", health, StringComparison.Ordinal);
        Assert.Matches(
            "(?m)^game_research reason: .*\n" +
            "game_modal reason: the panel controls did not bind$",
            health);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_player_half_composed_twice_fails_composition_because_one_collection_owns_both(
        bool consumable)
    {
        var lifecycle = 7L;
        var resolver = Resolver(lifecycle);
        var log = new ManualLogSource();

        var runtime = AutomataServiceCycleComposition.TryCreate(
            Configuration(),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionTimelineRegistry()),
            PlayerFeatures(lifecycle),
            log,
            actions =>
            {
                if (consumable)
                    actions.Add("game_consumable", new AutoItemsConsumablePlayerGameAction(
                        resolver, () => lifecycle, () => true, () => string.Empty));
                else
                    actions.Add("game_craft", new AutoScribeOneShotCraftPlayerGameAction(
                        resolver, () => lifecycle, () => true, () => string.Empty));
            });

        Assert.Null(runtime);
        Assert.Contains(log.Entries, entry => entry.ToString()!.Contains(
            consumable
                ? "A second AutoItemsConsumablePlayerGameAction was composed"
                : "A second AutoScribeOneShotCraftPlayerGameAction was composed",
            StringComparison.Ordinal));
    }

    private static GameMcpFrameContext Context(AutomataRuntimeFrameFacts runtime, string modalFailure) =>
        new(
            runtime.World,
            runtime,
            runtime.Configuration,
            lifecycleGeneration: 7,
            sceneName: "Main",
            nativeContractsAvailable: true,
            Array.Empty<FeatureStatusSnapshot>(),
            OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status.DecisionJournalStatus.Unavailable,
            traceWriterRevision: 0,
            Array.Empty<GameMcpWritableSettingDescriptor>(),
            modalDismissAvailable: modalFailure.Length == 0,
            modalDismissUnavailableReason: modalFailure);

    private static IAutomataServiceCycleFeature[] PlayerFeatures(long lifecycle)
    {
        var resolver = Resolver(lifecycle);
        return new IAutomataServiceCycleFeature[]
        {
            new AutoItemsServiceCycleFeature(new AutoItemsFeatureDependencies(
                resolver,
                () => lifecycle,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => true,
                readOwnershipFailure: () => string.Empty,
                featureStatus: Status(
                    AutoItemsFeatureContribution.FeatureId, "Auto Items", lifecycle),
                warn: static _ => { })),
            new AutoScribeServiceCycleFeature(new AutoScribeFeatureDependencies(
                resolver,
                AutoScribeIdentityCatalog.Audited,
                () => lifecycle,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => true,
                readOwnershipFailure: () => string.Empty,
                featureStatus: Status(
                    AutoScribeFeatureContribution.FeatureId, "Auto Scribe", lifecycle),
                warn: static _ => { })),
        };
    }

    private static TypedRegistryResolver Resolver(long lifecycle) =>
        new(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => Guid.Empty);

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
    };

    private static IAutomataServiceCycleFeature HarvestFeature(long lifecycle) =>
        new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                Resolver(lifecycle),
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: Status(
                    AutoHarvestFeatureContribution.FeatureId, "Auto Harvest", lifecycle)));

    private static AutomataFeatureStatusReporter Status(
        string featureId,
        string displayName,
        long lifecycle) =>
        new(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, featureId),
                displayName,
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycle));
}
