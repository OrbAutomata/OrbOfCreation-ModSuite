using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameMcpServiceCycleRuntimeCollection
{
    public const string Name = "Game MCP ServiceCycle runtime";
}

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpReturnToMenuTests
{
    [Fact]
    public void ToolHasNoCallerSelectedSaveOrNativeSurfaceAndUsesGameplayAdmission()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_return_to_menu");

        Assert.Null(tool["inputSchema"]!["required"]);
        Assert.Empty((JObject)tool["inputSchema"]!["properties"]!);
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);

        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_return_to_menu", new JObject());
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.Equal(GameMcpFrameData.World | GameMcpFrameData.Configuration,
            operation.RequiredData);
        Assert.Equal(GameMcpCommandKind.ReturnToMenu,
            GameMcpCommandKinds.FromToolName("game_return_to_menu"));
        Assert.Equal("game_return_to_menu",
            GameMcpCommandKinds.ToolName(GameMcpCommandKind.ReturnToMenu));

        Assert.True(Plugin.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(GameWorldStateDefaults.Empty),
            out var command,
            out var failure), failure?.Reason);
        Assert.Equal(GameMcpCommandKind.ReturnToMenu, command.Kind);
        Assert.Equal(Guid.Empty, command.TargetId);
        Assert.Equal("UIBackToMenuButton", command.DerivedNativeType);
    }

    [Fact]
    public void SuccessIsAcknowledgedBeforeLifecycleTeardownWithOnlyTheNativeDestination()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.ReturnToMenu, 12, 34, "return_to_menu",
            System.Guid.Empty, System.Guid.Empty, "UIBackToMenuButton",
            1, string.Empty, string.Empty, false, false);
        var action = ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
        var result = GameMcpCommandResult.FromAction(
            in action, command.Kind, 12, 34,
            "The game accepted the return to its Start screen.",
            new GameMcpObjectBuilder { ["scene"] = "Start" }.Freeze());

        var json = GameMcpTestHarness.Json(result.Project(command));

        Assert.Equal("committed", (string?)json["status"]);
        Assert.Equal("Start", (string?)json["scene"]);
        Assert.Equal(new[] { "status", "scene" },
            json.Properties().Select(property => property.Name));
        Assert.False(GameMcpCommandKinds.RequiresPostStateSettlement(command.Kind));
        Assert.True(GameMcpCommandKinds.RequiresPostStateSettlement(
            GameMcpCommandKind.StructureLifecycle));
    }

    [Fact]
    public void MainThreadRuntimeInvokesTheSharedActionAndReturnsBeforeTeardown()
    {
        UIScreenFlash.ResetForTests();
        var button = new UIBackToMenuButton();
        var lifecycle = 7L;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => null);
        var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady,
                    "waiting"),
                lifecycle));
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: status));

        using var runtime = AutomataServiceCycleComposition.Create(
            configuration,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => 1,
                () => lifecycle,
                new ServiceActionOutcomeWindowRegistry()),
            new IAutomataServiceCycleFeature[] { feature },
            new ManualLogSource(),
            createReturnToMenu: () => new ReturnToMenuGameAction(
                () => lifecycle,
                () => true,
                () => string.Empty,
                () => "Main",
                type => type == typeof(UIBackToMenuButton)
                    ? new object[] { button }
                    : Array.Empty<object>()));
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.ReturnToMenu, lifecycle, 1, "return_to_menu",
            Guid.Empty, Guid.Empty, "UIBackToMenuButton",
            1, string.Empty, string.Empty, false, false);

        var result = runtime.ExecuteGameMcp(command);
        var json = GameMcpTestHarness.Json(result.Project(command));

        Assert.Equal("committed", result.Status);
        Assert.Equal(1, button.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);
        // Which control the suite pressed to get here is how it drove the UI, not a fact about
        // the game; the scene the caller landed in is the whole answer.
        Assert.Equal(new[] { "status", "scene" },
            json.Properties().Select(property => property.Name));
        Assert.Equal("Start", (string?)json["scene"]);
    }
}
