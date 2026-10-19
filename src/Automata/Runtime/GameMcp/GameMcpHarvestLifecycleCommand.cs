#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpHarvestLifecycleCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.HarvestLifecycle,
        static (command, scope) => ExecuteHarvestLifecycle(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectHarvestLifecycleDelta(state, command)));

    private static GameMcpCommandResult ExecuteHarvestLifecycle(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var harvestLifecycle = gameActions.Find<HarvestLifecycleGameAction>();
        if (harvestLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared harvest-list GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "HarvestElementSO");
        var kind = command.Mode switch
        {
            "add_element" => HarvestLifecycleActionKind.AddElement,
            "remove_element" => HarvestLifecycleActionKind.RemoveElement,
            "add_element_action" => HarvestLifecycleActionKind.AddAction,
            "remove_element_action" => HarvestLifecycleActionKind.RemoveAction,
            _ => throw new ArgumentException("unsupported Agromancy element mode " + command.Mode),
        };
        var action = new HarvestLifecycleAction(kind, command.TargetId,
            command.SecondaryId, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = harvestLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpHarvestLifecycleProjection.Project(in submission));
    }
}
#endif
