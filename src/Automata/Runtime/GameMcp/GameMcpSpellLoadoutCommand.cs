#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLoadoutCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.SpellLoadout,
        static (command, scope) => ExecuteSpellLoadout(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectSpellLoadoutDelta(state, command)),
        nativeType: "Spell",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteSpellLoadout(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var spellLoadout = gameActions.Find<SpellLoadoutGameAction>();
        if (spellLoadout is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell loadout GameAction was not composed",
                lifecycle,
                configurationGeneration);
        return SubmitSpellLoadout(spellLoadout, command, lifecycle, configurationGeneration);
    }

    internal static GameMcpCommandResult SubmitSpellLoadout(
        SpellLoadoutGameAction spellLoadout,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var kind = command.Mode switch
        {
            "remove" => SpellLoadoutActionKind.Remove,
            "move" => SpellLoadoutActionKind.Move,
            _ => throw new ArgumentException("unsupported spell loadout mode " + command.Mode),
        };
        var action = new SpellLoadoutAction(
            kind,
            command.TargetId,
            command.Amount - 1,
            command.ExpectedLifecycleGeneration);
        var submission = spellLoadout.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpSpellLoadoutProjection.Project(in submission));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.Amount = request.Mode == "move" ? request.SlotIndex : 1;
        if (context.World is null)
            draft.Failure = GameMcpCommandResult.Rejected(
                "world_not_published", context.RuntimeNotAvailableReason);
        else if (!GameMcpWorldQuery.TryEquippedSpellSlot(
                     context.World.Snapshot, request.Amount,
                     out draft.TargetId, out var spellSlotReason))
            draft.Failure = GameMcpCommandResult.Rejected(
                "slot_unavailable", spellSlotReason);
    }
}
#endif
