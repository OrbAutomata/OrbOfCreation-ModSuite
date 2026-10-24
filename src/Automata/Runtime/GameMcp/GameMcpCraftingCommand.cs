#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Crafting,
        static (command, scope) => ExecuteCrafting(
            scope.GameActions,
            command,
            scope.World.Snapshot,
            scope.Lifecycle,
            scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, committed) =>
                GameMcpCraftingProjection.ProjectCraftingDelta(state, command, committed)),
        nativeType: "CraftingRecipeSO",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteCrafting(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        if (command.Mode != "craft")
        {
            var craftingInstances = gameActions.Find<CraftingInstanceLifecycleGameAction>();
            if (craftingInstances is null)
                return GameMcpCommandResult.Rejected(
                    "contract_unavailable",
                    "the shared crafting-instance GameAction was not composed",
                    lifecycle,
                    configurationGeneration);
            var kind = command.Mode switch
            {
                "automate" => CraftingInstanceLifecycleActionKind.Automate,
                "cancel_manual" => CraftingInstanceLifecycleActionKind.CancelManual,
                "cancel_automation" => CraftingInstanceLifecycleActionKind.CancelAutomation,
                _ => throw new ArgumentException("unsupported crafting mode " + command.Mode),
            };
            var instanceAction = new CraftingInstanceLifecycleAction(
                kind, command.TargetId, command.ExpectedLifecycleGeneration);
            var instanceSubmission = craftingInstances.Submit(in instanceAction);
            var instanceResult = instanceSubmission.Outcome.ToResult();
            return GameMcpCommandResult.FromAction(
                in instanceResult,
                lifecycle,
                configurationGeneration,
                GameMcpCraftingProjection.Project(in instanceSubmission));
        }
        var crafting = gameActions.Find<AutoScribeOneShotCraftPlayerGameAction>();
        if (crafting is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared player crafting GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var max = command.PayloadValue == "max";
        return GameMcpCraftingProjection.Press(
            command,
            lifecycle,
            configurationGeneration,
            max,
            () => GameMcpCraftingProjection.CraftShortfallReason(world, command.TargetId),
            remaining =>
            {
                var action = new CraftingPlayerAction(
                    command.TargetId,
                    command.ExpectedLifecycleGeneration,
                    remaining,
                    max);
                return crafting.Submit(in action);
            });
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.PayloadValue = request.SerializedValue;
    }
}
#endif
