#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConsumableCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Consumable,
        static (command, scope) => ExecuteConsumable(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectConsumableDelta(state, command)),
        nativeType: "ConsumableSO",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteConsumable(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var consumables = gameActions.Find<AutoItemsConsumablePlayerGameAction>();
        if (consumables is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared consumable player GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "use" => ConsumablePlayerActionKind.Use,
            "cancel" => ConsumablePlayerActionKind.Cancel,
            "discard" => ConsumablePlayerActionKind.Discard,
            "set_randomization" => ConsumablePlayerActionKind.SetRandomization,
            "move" => ConsumablePlayerActionKind.Move,
            _ => throw new ArgumentException("unsupported consumable mode " + command.Mode),
        };
        var list = command.PayloadKey switch
        {
            "inventory" => ConsumablePlayerListKind.Inventory,
            "hotbar" => ConsumablePlayerListKind.Hotbar,
            _ => ConsumablePlayerListKind.None,
        };
        var randomized = string.Equals(
            command.PayloadValue,
            "true",
            StringComparison.Ordinal);
        var action = new ConsumablePlayerAction(
            kind,
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            command.Amount,
            randomized,
            list,
            kind == ConsumablePlayerActionKind.Move ? command.Amount - 1 : -1);
        var submission = consumables.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpConsumableProjection.Project(in submission));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.PayloadKey = request.Key;
        draft.PayloadValue = request.SerializedValue;
        if (request.Mode == "move") draft.Amount = request.SlotIndex;
    }
}
#endif
