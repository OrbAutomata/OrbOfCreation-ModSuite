#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpReturnToMenuCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.ReturnToMenu,
        static (command, scope) => ExecuteReturnToMenu(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.FromPress);

    private static GameMcpCommandResult ExecuteReturnToMenu(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var returnToMenu = gameActions.Find<ReturnToMenuGameAction>();
        if (returnToMenu is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Back to Menu GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "UIBackToMenuButton");
        var action = new ReturnToMenuAction(command.ExpectedLifecycleGeneration);
        var submission = returnToMenu.Submit(in action);
        var result = submission.Outcome.ToResult();
        var details = submission.Outcome.Verified
            // Which control the verb pressed, and whether it had to open a panel to reach it, are
            // how the suite drove the UI. The caller asked to be back at the main menu; the scene
            // it landed in is the whole answer.
            ? new GameMcpObjectBuilder { ["scene"] = "Start" }.Freeze()
            : new GameMcpObjectBuilder().Freeze();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration, details);
    }
}
#endif
