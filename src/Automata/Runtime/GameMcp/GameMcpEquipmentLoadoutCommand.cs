#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEquipmentLoadoutCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.EquipmentLoadout,
        static (command, scope) => ExecuteEquipmentLoadout(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpEquipmentLoadoutProjection.ProjectEquipmentDelta(state, command)),
        nativeType: "EquipmentSO");

    private static GameMcpCommandResult ExecuteEquipmentLoadout(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var equipmentLoadout = gameActions.Find<EquipmentLoadoutGameAction>();
        if (equipmentLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared equipment loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode == "equip"
            ? EquipmentLoadoutActionKind.Equip
            : EquipmentLoadoutActionKind.Unequip;
        var action = new EquipmentLoadoutAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = equipmentLoadout.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpEquipmentLoadoutProjection.Project(in submission));
    }
}
#endif
