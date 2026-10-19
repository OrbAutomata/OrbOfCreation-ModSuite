#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Every gameplay command kind's descriptor, registered once and keyed by kind. The listener checks
/// it before it binds, so a kind a GameAction family names cannot reach the main thread without an
/// executor and a post-state rule.
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
    });

    internal IReadOnlyList<GameMcpCommandDescriptor> Commands { get; }

    internal GameMcpCommandExecutor Executor(GameMcpCommandKind kind) =>
        _commands.TryGetValue(kind, out var descriptor) && descriptor.Execute is { } execute
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
            if (!GameMcpCommandKinds.IsGameplayAction(command.Kind))
                problems.Add("command " + command.Kind + " is registered, but no gameplay action " +
                    "family names it");
            if (command.Execute is null)
                problems.Add("command " + command.Kind + " has no executor");
            if (command.PostState is null)
                problems.Add("command " + command.Kind + " has no post-state rule");
        }

        var families = AutomataActionFamilyOwnership.GameMcpFamilies;
        for (var index = 0; index < families.Length; index++)
        {
            var kind = families[index].Kind;
            if (registered.Add(kind))
                problems.Add("a gameplay action family names " + kind + ", but no command " +
                    "descriptor registers it");
        }
        return problems;
    }
}
#endif
