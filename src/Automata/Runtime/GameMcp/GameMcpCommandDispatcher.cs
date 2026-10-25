#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpCommandDispatcher
{
    private readonly AutomataServiceCycleHost _host;
    private readonly Dictionary<GameMcpCommandKind, IGameMcpServiceCommand> _commands = new();
    private readonly ServiceConfigurationPublisher _configurationPublication;
    private readonly LifecycleGameActions _gameActions;
    private bool _disposed;
    private ulong _nextGameMcpActionIdentity;

    internal GameMcpCommandDispatcher(
        AutomataServiceCycleHost host,
        ServiceConfigurationPublisher configurationPublication,
        LifecycleGameActions gameActions,
        IAutomataServiceCycleFeatureRuntime[] features)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _configurationPublication = configurationPublication ??
            throw new ArgumentNullException(nameof(configurationPublication));
        _gameActions = gameActions ?? throw new ArgumentNullException(nameof(gameActions));
        if (features is null) throw new ArgumentNullException(nameof(features));
        foreach (var feature in features)
        {
            if (feature is null) throw new ArgumentException("A feature runtime is required.", nameof(features));
            if (feature.GameMcpCommand is not { } command) continue;
            if (!_commands.TryAdd(command.Kind, command))
                throw new InvalidOperationException(
                    "The " + command.Kind + " command is claimed by more than one feature runtime.");
        }
    }

    internal void Dispose() => _disposed = true;

    public GameMcpCommandResult ExecuteGameMcp(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (_disposed)
            return GameMcpCommandResult.Rejected(
                "runtime_disposed",
                "the ServiceCycle runtime has been disposed");

        var registry = _host.Pump.DiagnosticsRegistry;
        var world = registry.World.ReadLatest();
        var configuration = _configurationPublication.ReadLatest();
        var lifecycle = checked((long)_host.CurrentLifecycle.Value);
        if (GameMcpNativeActionAdmission.TryReject(
                command,
                lifecycle,
                configuration.Generation.Value,
                _host.EmergencyStopEngaged,
                out var rejection))
            return rejection;

        try
        {
            return GameMcpCommandCatalog.Shipping.Executor(command.Kind)(
                command,
                new GameMcpCommandScope(
                    this, _gameActions, registry, world, configuration, lifecycle));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                OverflowException or ObjectDisposedException)
        {
            return GameMcpCommandResult.Faulted(
                "main_thread_execution_fault",
                GameActionFaultLog.Reason(
                    "The " + command.Kind + " command did not finish on the main thread.",
                    exception, command.Kind + " command").Text,
                lifecycle,
                configuration.Generation.Value);
        }
    }

    public SpellWorkbenchLoadPreview PreviewSpellWorkbench(
        in SpellWorkbenchLoadPreviewRequest request)
    {
        if (_disposed)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        var spellWorkbench = _gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The shared spell workbench boundary is unavailable.");
        return spellWorkbench.Preview(in request);
    }

    public SpellWorkbenchStagedLayout ReadStagedSpellWorkbench()
    {
        if (_disposed)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        var spellWorkbench = _gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The shared Spellcraft boundary is unavailable.");
        return spellWorkbench.ReadStagedLayout();
    }

    internal static readonly GameMcpCommandExecutor FeatureExecutor =
        static (command, scope) => scope.Dispatcher.ExecuteFeatureCommand(command, scope);

    private GameMcpCommandResult ExecuteFeatureCommand(
        GameMcpCommand command,
        GameMcpCommandScope scope)
    {
        if (!_commands.TryGetValue(command.Kind, out var handler))
            throw new InvalidOperationException(
                "The " + command.Kind + " command has no registered feature hook.");
        var context = CreateGameMcpContext(
            scope.Registry, handler.Service, scope.World.Generation);
        return handler.Execute(command, scope, in context);
    }

    private ServiceActionContext CreateGameMcpContext(
        OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry registry,
        ServiceId service,
        WorldGeneration world)
    {
        var serviceOrdinal = -1;
        for (var ordinal = 0; ordinal < registry.OrdinalCount; ordinal++)
        {
            if (registry.GetServiceId(ordinal) != service) continue;
            serviceOrdinal = ordinal;
            break;
        }
        if (serviceOrdinal < 0)
            throw new InvalidOperationException(
                "service " + service.Value + " is not registered");

        var sequence = checked(++_nextGameMcpActionIdentity);
        var identity = new ServiceCycleIdentity(
            service,
            _host.CurrentLifecycle,
            _configurationPublication.ReadLatest().Generation,
            registry.Strategy.ReadLatest().Generation,
            world,
            new CycleId(sequence));
        var coordinates = new ServiceCycleProfileCoordinates(
            serviceOrdinal,
            Math.Max(0, _host.Pump.LastAcceptedFrameIdentity));
        return new ServiceActionContext(
            identity,
            new BatchId(sequence),
            new ActionId(sequence),
            0,
            registry.Clock.Now,
            in coordinates);
    }

}
#endif
