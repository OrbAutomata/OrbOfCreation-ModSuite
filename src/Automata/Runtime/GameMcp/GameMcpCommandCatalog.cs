#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Every command kind's descriptor, registered once and keyed by kind. The listener checks it
/// before it binds, so no kind can reach the main thread without an executor, and a kind a
/// GameAction family names not without a post-state rule.
/// </summary>
internal sealed class GameMcpCommandCatalog
{
    private readonly Dictionary<GameMcpCommandKind, GameMcpCommandDescriptor> _commands = new();

    internal GameMcpCommandCatalog(IReadOnlyList<GameMcpCommandDescriptor> commands)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        for (var index = 0; index < commands.Count; index++)
            if (!_commands.ContainsKey(commands[index].Kind))
                _commands.Add(commands[index].Kind, commands[index]);
    }

    internal static GameMcpCommandCatalog Shipping { get; } = new(new[]
    {
        GameMcpPurchaseCommand.Descriptor,
        GameMcpCastCommand.Descriptor,
        GameMcpConceptCommand.Descriptor,
        GameMcpSpellLevelCommand.Descriptor,
        GameMcpPlotLifecycleCommand.Descriptor,
        GameMcpDiscoveryTreeOfferCommand.Descriptor,
        GameMcpSpellWorkbenchCommand.Descriptor,
        GameMcpSpellCompositionCommand.Descriptor,
        GameMcpSpellLoadoutCommand.Descriptor,
        GameMcpTargetingCommand.Descriptor,
        GameMcpConsumableCommand.Descriptor,
        GameMcpCraftingCommand.Descriptor,
        GameMcpEquipmentLoadoutCommand.Descriptor,
        GameMcpChallengeCommand.Descriptor,
        GameMcpPrestigeCommand.Descriptor,
        GameMcpResearchCommand.Descriptor,
        GameMcpAlchemyLoadoutCommand.Descriptor,
        GameMcpRitualLifecycleCommand.Descriptor,
        GameMcpGenericLevelCommand.Descriptor,
        GameMcpLoadoutCommand.Descriptor,
        GameMcpHarvestLifecycleCommand.Descriptor,
        GameMcpStructureLifecycleCommand.Descriptor,
        GameMcpReturnToMenuCommand.Descriptor,
        GameMcpSuiteCommands.ConfigurationSet,
        GameMcpSuiteCommands.AutomationSet,
        GameMcpSuiteCommands.EmergencyStop,
        GameMcpGadgetCommands.Screenshot,
        GameMcpGadgetCommands.Navigation,
        GameMcpGadgetCommands.Probe,
        GameMcpGadgetCommands.TooltipCatalog,
        GameMcpGadgetCommands.TooltipRead,
        GameMcpGadgetCommands.ContinueRun,
        GameMcpGadgetCommands.Modal,
    });

    internal IReadOnlyList<GameMcpCommandDescriptor> Commands { get; }

    internal GameMcpCommandDescriptor Descriptor(GameMcpCommandKind kind) =>
        _commands.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException("no command descriptor registers " + kind);

    internal GameMcpCommandExecutor Executor(GameMcpCommandKind kind) =>
        _commands.TryGetValue(kind, out var descriptor) && descriptor.Execute is { } execute
            ? execute
            : throw new InvalidOperationException("command " + kind + " has no executor");

    internal GameMcpFrameCommandExecutor FrameExecutor(GameMcpCommandKind kind) =>
        _commands.TryGetValue(kind, out var descriptor) && descriptor.ExecuteInFrame is { } execute
            ? execute
            : throw new InvalidOperationException("command " + kind + " has no executor");

    internal GameMcpPostStateRule PostState(GameMcpCommandKind kind) =>
        _commands.TryGetValue(kind, out var descriptor) && descriptor.PostState is { } rule
            ? rule
            : throw new InvalidOperationException("command " + kind + " has no post-state rule");

    internal IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var registered = new HashSet<GameMcpCommandKind>();
        for (var index = 0; index < Commands.Count; index++)
        {
            var command = Commands[index];
            if (!registered.Add(command.Kind))
                problems.Add("command " + command.Kind + " is registered twice");
            var gameplay = GameMcpCommandKinds.IsGameplayAction(command.Kind);
            if (gameplay ? command.Execute is null : command.ExecuteInFrame is null)
                problems.Add("command " + command.Kind + " has no executor");
            if (gameplay && command.PostState is null)
                problems.Add("command " + command.Kind + " has no post-state rule");
            if (gameplay && command.NativeType.Length == 0 && command.Prepare is null)
                problems.Add("command " + command.Kind + " names no native type and prepares " +
                    "none");
            if (!gameplay && command.PostState is not null)
                problems.Add("command " + command.Kind + " is no gameplay action, but declares a " +
                    "post-state rule");
        }

        foreach (GameMcpCommandKind kind in Enum.GetValues(typeof(GameMcpCommandKind)))
        {
            if (registered.Add(kind))
                problems.Add("no command descriptor registers " + kind);
        }
        return problems;
    }
}
#endif
