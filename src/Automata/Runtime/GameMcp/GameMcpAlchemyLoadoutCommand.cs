#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpAlchemyLoadoutCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.AlchemyLoadout,
        static (command, scope) => ExecuteAlchemyLoadout(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectAlchemyLoadoutDelta(state, command)));

    private static GameMcpCommandResult ExecuteAlchemyLoadout(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var alchemyLoadout = gameActions.Find<AlchemyLoadoutGameAction>();
        if (alchemyLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared ordinary Alchemy GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "AlchemyRecipeSO");
        var kind = command.Mode switch
        {
            "add" => AlchemyLoadoutActionKind.Add,
            "remove" => AlchemyLoadoutActionKind.Remove,
            _ => throw new ArgumentException("unsupported Alchemy mode " + command.Mode),
        };
        var action = new AlchemyLoadoutAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = alchemyLoadout.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpAlchemyLoadoutProjection.Project(in submission));
    }
}
#endif
