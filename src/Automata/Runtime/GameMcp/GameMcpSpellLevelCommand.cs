#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpSpellLevelCommand : IGameMcpServiceCommand
{
    private readonly SpellLevelFeatureRuntime _runtime;

    internal GameMcpSpellLevelCommand(SpellLevelFeatureRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public GameMcpCommandKind Kind => GameMcpCommandKind.SpellLevel;
    public ServiceId Service => SpellLevelServicePolicies.ServiceId;

    public GameMcpCommandResult Execute(
        GameMcpCommand command, GameMcpCommandScope scope, in ServiceActionContext context)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var action = new SpellLevelCycleAction(
            command.Mode == "all" ? SpellLevelActionKind.All : SpellLevelActionKind.Single,
            command.TargetId, scope.World.Snapshot.CollectedAtEpoch);
        var config = scope.Configuration.Snapshot;
        var result = _runtime.TryExecuteGameMcp(in action, in config, in context);
        var world = scope.World.Snapshot;
        var reason = GameMcpCastProjection.ExactGameMcpReason(command, world, in result);
        if (command.Mode != "all" && result.Code == SpellLevelActionResultCodes.LevelNotAffordable)
            reason = GameMcpSpellLevelProjection.MasteryRefusalReason(world, command.TargetId);
        if (result.Code == SpellLevelActionResultCodes.LevelAllLocked)
            reason = GameMcpSpellLevelProjection.LevelAllLockedReason(world);
        var details = GameMcpSpellLevelProjection.Project(_runtime.LastGameMcpSubmission, world);
        return GameMcpCommandResult.FromAction(
            result.WithReason(reason), scope.Lifecycle, scope.ConfigurationGeneration, details);
    }

    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.SpellLevel,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.Settled(
            static (state, command, _) => string.Equals(
                command.Mode,
                "all",
                StringComparison.Ordinal)
                ? GameMcpSpellLevelProjection.ProjectSpellLevelAllPostState(state, command)
                : GameMcpSpellLevelProjection.ProjectSpellLevelDelta(state, command)),
        nativeType: "SpellRecipeSO");
}
#endif
