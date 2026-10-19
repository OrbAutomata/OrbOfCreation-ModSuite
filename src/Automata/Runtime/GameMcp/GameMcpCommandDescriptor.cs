#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal delegate GameMcpCommandResult GameMcpCommandExecutor(
    GameMcpCommand command,
    GameMcpCommandScope scope);

/// <summary>
/// One gameplay command kind: how it executes on the main thread and how its committed answer
/// settles against a later world.
/// </summary>
internal sealed class GameMcpCommandDescriptor
{
    internal GameMcpCommandDescriptor(
        GameMcpCommandKind kind,
        GameMcpCommandExecutor? execute,
        GameMcpPostStateRule? postState)
    {
        Kind = kind;
        Execute = execute;
        PostState = postState;
    }

    internal GameMcpCommandKind Kind { get; }
    internal GameMcpCommandExecutor? Execute { get; }
    internal GameMcpPostStateRule? PostState { get; }
}

/// <summary>What one command's execution reads, pinned when the dispatcher admits it.</summary>
internal sealed class GameMcpCommandScope
{
    internal GameMcpCommandScope(
        GameMcpCommandDispatcher dispatcher,
        LifecycleGameActions gameActions,
        ServiceCycleRegistry registry,
        WorldPublication<GameWorldState> world,
        ConfigurationPublication configuration,
        long lifecycle)
    {
        Dispatcher = dispatcher;
        GameActions = gameActions;
        Registry = registry;
        World = world;
        Configuration = configuration;
        Lifecycle = lifecycle;
    }

    internal GameMcpCommandDispatcher Dispatcher { get; }
    internal LifecycleGameActions GameActions { get; }
    internal ServiceCycleRegistry Registry { get; }
    internal WorldPublication<GameWorldState> World { get; }
    internal ConfigurationPublication Configuration { get; }
    internal long Lifecycle { get; }
    internal ulong ConfigurationGeneration => Configuration.Generation.Value;
}

/// <summary>How a committed command's answer meets the world captured after it.</summary>
internal sealed class GameMcpPostStateRule
{
    private readonly Func<GameMcpFrameContext, GameMcpCommand, GameMcpCommandResult, GameMcpValue>?
        _project;
    private readonly Func<GameMcpFrameContext, GameMcpCommand, bool>? _reached;
    private readonly Func<GameMcpCommand, GameMcpFrameContext?, GameMcpValue>? _timedOut;

    private GameMcpPostStateRule(
        Func<GameMcpFrameContext, GameMcpCommand, GameMcpCommandResult, GameMcpValue>? project,
        Func<GameMcpFrameContext, GameMcpCommand, bool>? reached,
        Func<GameMcpCommand, GameMcpFrameContext?, GameMcpValue>? timedOut,
        float waitSeconds,
        GameMcpFrameData data)
    {
        _project = project;
        _reached = reached;
        _timedOut = timedOut;
        WaitSeconds = waitSeconds;
        Data = data;
    }

    /// <summary>A commit whose press already holds every fact its answer states.</summary>
    internal static GameMcpPostStateRule FromPress { get; } = new(
        null, null, null, GameMcpPostStateSettlement.MaximumWaitSeconds, GameMcpFrameData.None);

    internal static GameMcpPostStateRule Settled(
        Func<GameMcpFrameContext, GameMcpCommand, GameMcpCommandResult, GameMcpValue> project,
        Func<GameMcpFrameContext, GameMcpCommand, bool>? reached = null,
        Func<GameMcpCommand, GameMcpFrameContext?, GameMcpValue>? timedOut = null,
        float waitSeconds = GameMcpPostStateSettlement.MaximumWaitSeconds,
        GameMcpFrameData data = GameMcpFrameData.World) =>
        new(project ?? throw new ArgumentNullException(nameof(project)),
            reached, timedOut, waitSeconds, data);

    internal bool Settles => _project is not null;
    internal float WaitSeconds { get; }
    internal GameMcpFrameData Data { get; }

    internal GameMcpValue Project(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) =>
        _project is null
            ? throw new InvalidOperationException(
                command.Kind + " answers from its press and projects no settled world")
            : _project(state, command, committed);

    internal bool Reached(GameMcpFrameContext state, GameMcpCommand command) =>
        _reached is null || _reached(state, command);

    internal GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest) =>
        _timedOut is null
            ? GameMcpPostStateSettlement.PostStateTimeout()
            : _timedOut(command, latest);
}
#endif
