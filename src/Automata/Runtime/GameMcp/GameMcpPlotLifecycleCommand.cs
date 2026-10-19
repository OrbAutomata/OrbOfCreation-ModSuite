#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPlotLifecycleCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Harvest,
        static (command, scope) => ExecutePlotLifecycle(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, committed) =>
                GameMcpWorldQuery.ProjectHarvestDelta(state, command, committed)));

    private static GameMcpCommandResult ExecutePlotLifecycle(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var plotLifecycle = gameActions.Find<PlotLifecycleGameAction>();
        if (plotLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared plot-action GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "PlotNodeSO");
        var kind = command.Mode switch
        {
            "add_plot_action" => PlotLifecycleActionKind.Add,
            "remove_plot_action" => PlotLifecycleActionKind.Remove,
            _ => throw new ArgumentException("unsupported Agromancy plot mode " + command.Mode),
        };
        var action = new PlotLifecycleAction(kind, command.TargetId,
            command.SecondaryId, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = plotLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        GameMcpValue? details = submission.Outcome.Verified
            ? new GameMcpObjectBuilder
            {
                ["active"] = new GameMcpObjectBuilder
                {
                    ["before"] = submission.BeforeQuantity,
                    ["after"] = submission.AfterQuantity,
                },
            }.Freeze()
            : submission.MaximumAmount >= 0
                ? new GameMcpObjectBuilder
                {
                    ["maximumAmount"] = submission.MaximumAmount,
                }.Freeze()
                : null;
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration, details);
    }
}
#endif
