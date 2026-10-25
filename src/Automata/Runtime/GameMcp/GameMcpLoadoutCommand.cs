#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpLoadoutCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Loadout,
        static (command, scope) => ExecuteLoadout(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpLoadoutProjection.ProjectLoadoutDelta(state, command)),
        prepare: Prepare);

    internal const string PlayerLoadoutType = "PlayerLoadout";

    internal static string SnapshotListType(bool alchemy) =>
        alchemy ? "AlchemySnapshotListVariable" : "EquipmentSnapshotListVariable";

    private static GameMcpCommandResult ExecuteLoadout(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var loadouts = gameActions.Find<LoadoutGameAction>();
        if (loadouts is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared player-loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var kind = command.Mode switch
        {
            "select" => LoadoutActionKind.Select,
            "set_equipment" => LoadoutActionKind.SetEquipmentSection,
            "set_alchemy" => LoadoutActionKind.SetAlchemySection,
            "rename" => LoadoutActionKind.Rename,
            "next_icon" => LoadoutActionKind.NextIcon,
            "next_color" => LoadoutActionKind.NextColor,
            "snapshot_save" => LoadoutActionKind.SnapshotSave,
            "snapshot_load" => LoadoutActionKind.SnapshotLoad,
            "snapshot_clear" => LoadoutActionKind.SnapshotClear,
            _ => throw new ArgumentException("unsupported loadout mode " + command.Mode),
        };
        var enabled = string.Equals(command.PayloadValue, "true", StringComparison.Ordinal);
        var action = new LoadoutAction(kind, command.TargetId, command.Amount - 1,
            enabled, command.PayloadValue, command.ExpectedLifecycleGeneration);
        var submission = loadouts.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpLoadoutProjection.Project(in submission));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        var snapshotMode = request.Mode.StartsWith("snapshot_", StringComparison.Ordinal);
        if (context.World is null)
        {
            draft.Failure = GameMcpCommandResult.Rejected(
                "world_not_published", context.RuntimeNotAvailableReason);
        }
        else if (snapshotMode)
        {
            if (GameMcpLoadoutProjection.TrySnapshotList(
                    context.World.Snapshot, request.Key, out draft.TargetId, out var listReason))
                draft.NativeType = SnapshotListType(request.Key == "alchemy");
            else
                draft.Failure = GameMcpCommandResult.Rejected(
                    "loadout_unavailable", listReason);
        }
        else if (GameMcpLoadoutProjection.TryPlayerLoadout(
                     context.World.Snapshot, request.Amount,
                     out draft.TargetId, out var loadoutReason))
        {
            draft.NativeType = PlayerLoadoutType;
        }
        else
        {
            draft.Failure = GameMcpCommandResult.Rejected(
                "loadout_unavailable", loadoutReason);
        }
        draft.Mode = request.Mode == "set_section"
            ? request.Key == "equipment" ? "set_equipment" : "set_alchemy"
            : request.Mode;
        draft.PayloadKey = request.Key;
        draft.PayloadValue = request.SerializedValue;
        // Only a snapshot mode addresses a slot. A loadout position is resolved here into the
        // identity the boundary acts on, so it must not also ride along as a slot number.
        draft.Amount = snapshotMode ? request.SlotIndex : 1;
    }
}
#endif
