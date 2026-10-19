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
            static (state, command, _) => GameMcpWorldQuery.ProjectLoadoutDelta(state, command)));

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
}
#endif
