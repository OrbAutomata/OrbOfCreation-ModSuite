#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellCompositionCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.SpellComposition,
        static (command, scope) => ExecuteSpellComposition(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpSpellCompositionProjection.ProjectCastingDialDelta(state, command),
            DialReached,
            DialTimedOut),
        nativeType: "IntVariable",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteSpellComposition(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var spellComposition = gameActions.Find<SpellCompositionGameAction>();
        if (spellComposition is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell composition GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var dial = command.Mode switch
        {
            "set_output_level" => LevelDial.Output,
            "set_reserve_level" => LevelDial.Reserve,
            "set_druidry_level" => LevelDial.Druidry,
            "set_alchemy_level" => LevelDial.Alchemy,
            _ => (LevelDial?)null,
        };
        if (dial is null)
            return GameMcpCommandResult.Failed(
                "unsupported_control",
                "The suite sent a control this screen has no button for; nothing was applied.",
                lifecycle,
                configurationGeneration);
        var action = new SpellCompositionAction(
            dial.Value,
            command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = spellComposition.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpSpellCompositionProjection.Project(in submission, command.PayloadKey));
    }

    private static bool DialReached(GameMcpFrameContext state, GameMcpCommand command)
    {
        var workbench = state.World!.Snapshot.SpellWorkbench;
        return command.Mode switch
        {
            "set_output_level" => workbench.OutputLevel == command.Amount,
            "set_reserve_level" => workbench.ReserveLevel == command.Amount,
            "set_druidry_level" => workbench.DruidryLevel == command.Amount,
            "set_alchemy_level" => workbench.AlchemyLevel == command.Amount,
            _ => false,
        };
    }

    private static GameMcpValue DialTimedOut(GameMcpCommand command, GameMcpFrameContext? latest)
    {
        if (latest?.World is not null)
        {
            var workbench = latest.World.Snapshot.SpellWorkbench;
            var observed = GameMcpSpellCompositionProjection.DialLevel(workbench, command.Mode).Current;
            return GameMcpRowKit.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled " + command.PayloadKey + " dial is " + observed +
                ", not the requested " + command.Amount);
        }
        return GameMcpPostStateSettlement.PostStateTimeout();
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        // Four dials share one action, so the committed response names the one it moved.
        draft.PayloadKey = request.Key;
    }
}
#endif
