#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellWorkbenchCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.SpellWorkbench,
        static (command, scope) => ExecuteSpellWorkbench(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpSpellLoadoutProjection.ProjectSpellLoadoutDelta(state, command)),
        nativeType: "SpellRecipeSO",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteSpellWorkbench(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var spellWorkbench = gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell workbench GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var layout = new SpellWorkbenchGlyphStack[command.UuidCounts.Length];
        for (var index = 0; index < layout.Length; index++)
            layout[index] = new SpellWorkbenchGlyphStack(
                command.UuidCounts[index].Uuid,
                command.UuidCounts[index].Count);
        var action = new SpellWorkbenchAction(
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            layout);
        var submission = spellWorkbench.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpSpellWorkbenchProjection.Project(in submission));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.Mode = "create";
    }
}
#endif
