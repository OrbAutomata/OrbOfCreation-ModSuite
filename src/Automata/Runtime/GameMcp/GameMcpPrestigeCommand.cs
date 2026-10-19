#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPrestigeCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Prestige,
        static (command, scope) => ExecutePrestige(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, _, _) => GameMcpWorldQuery.ProjectPrestigePostState(state),
            timedOut: TimedOut,
            waitSeconds: GameMcpPostStateSettlement.LifecycleWaitSeconds,
            data: GameMcpFrameData.World | GameMcpFrameData.Scene));

    private static GameMcpCommandResult ExecutePrestige(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var prestige = gameActions.Find<PrestigeGameAction>();
        if (prestige is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared prestige GameAction was not composed", lifecycle,
                configurationGeneration);
        var action = new PrestigeAction(command.ExpectedLifecycleGeneration);
        var submission = prestige.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpPrestigeProjection.Project(in submission));
    }

    private static GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest) =>
        GameMcpWorldQuery.PrestigeSettlementPending(
            command.ExpectedLifecycleGeneration,
            latest?.LifecycleGeneration,
            latest?.SceneName);
}
#endif
