using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrbModding.Common;

public enum GameLifecycleState
{
    NoGame,
    Initializing,
    Playing,
    Resetting,
    SceneExit
}

public enum GameLifecycleTransitionKind
{
    SceneEntered,
    SceneExited,
    RuntimeReady,
    SaveLoadStarted,
    SaveLoaded,
    ResetStarted,
    ResetCompleted,
    NewGamePlusStarted,
    RegistryRebuilt
}

public readonly struct GameLifecycleObservation
{
    public GameLifecycleObservation(
        GameLifecycleTransitionKind kind,
        long frame,
        string sceneName,
        string source,
        object? nativeIdentity = null)
    {
        Kind = kind;
        Frame = frame;
        SceneName = sceneName ?? string.Empty;
        Source = source ?? string.Empty;
        NativeIdentity = nativeIdentity;
    }

    public GameLifecycleTransitionKind Kind { get; }
    public long Frame { get; }
    public string SceneName { get; }
    public string Source { get; }
    public object? NativeIdentity { get; }
}

public readonly struct GameLifecycleSnapshot
{
    public GameLifecycleSnapshot(
        GameLifecycleState state,
        long generation,
        string sceneName,
        GameLifecycleTransitionKind? lastTransition,
        long lastFrame)
    {
        State = state;
        Generation = generation;
        SceneName = sceneName;
        LastTransition = lastTransition;
        LastFrame = lastFrame;
    }

    public GameLifecycleState State { get; }
    public long Generation { get; }
    public string SceneName { get; }
    public GameLifecycleTransitionKind? LastTransition { get; }
    public long LastFrame { get; }
    public bool IsGameplayReady => State == GameLifecycleState.Playing;
}

public readonly struct GameLifecycleTransition
{
    public GameLifecycleTransition(GameLifecycleSnapshot previous, GameLifecycleSnapshot current, string source)
    {
        Previous = previous;
        Current = current;
        Source = source;
    }

    public GameLifecycleSnapshot Previous { get; }
    public GameLifecycleSnapshot Current { get; }
    public string Source { get; }

    /// <summary>
    /// One line saying which epoch this is and why it happened.
    /// </summary>
    /// <remarks>
    /// Every reference that invalidates at a lifecycle boundary invalidates on the number alone, and
    /// the number is all the trace can carry: its records are numeric, and a scene name and a source
    /// are strings. So the reason has to reach the log or it reaches nobody — an epoch changed
    /// mid-session and naming the prestige behind it took a purchase-topology line, two clock
    /// anchors, and a file timestamp to establish. An unnamed fact is spelled out rather than left
    /// blank, because a blank field reads as a formatting bug rather than as an absent fact.
    /// </remarks>
    public string Describe() =>
        "Game lifecycle " +
        Previous.Generation.ToString(CultureInfo.InvariantCulture) + " -> " +
        Current.Generation.ToString(CultureInfo.InvariantCulture) + ": " +
        (Current.LastTransition?.ToString() ?? "unrecorded") +
        " | state=" + Current.State +
        " | scene=" + Named(Current.SceneName) +
        " | source=" + Named(Source) +
        " | frame=" + Current.LastFrame.ToString(CultureInfo.InvariantCulture) + ".";

    private static string Named(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unnamed" : value;
}

public readonly struct GameLifecycleDiagnostic
{
    public GameLifecycleDiagnostic(
        long generation,
        GameLifecycleState state,
        GameLifecycleTransitionKind kind,
        long frame,
        string sceneName,
        string source)
    {
        Generation = generation;
        State = state;
        Kind = kind;
        Frame = frame;
        SceneName = sceneName;
        Source = source;
    }

    public long Generation { get; }
    public GameLifecycleState State { get; }
    public GameLifecycleTransitionKind Kind { get; }
    public long Frame { get; }
    public string SceneName { get; }
    public string Source { get; }
}

public readonly struct GameLifecycleLease
{
    public GameLifecycleLease(long generation)
    {
        Generation = generation;
    }

    public long Generation { get; }
}

public readonly struct GameLifecycleDispatchFailure
{
    public GameLifecycleDispatchFailure(long generation, string subscriber, string exceptionType)
    {
        Generation = generation;
        Subscriber = subscriber;
        ExceptionType = exceptionType;
    }

    public long Generation { get; }
    public string Subscriber { get; }
    public string ExceptionType { get; }
}

public sealed class GameLifecycleMonitor
{
    private const int DiagnosticCapacity = 32;
    private readonly Func<int> _readThreadId;
    private readonly Queue<GameLifecycleDiagnostic> _diagnostics = new(DiagnosticCapacity);
    private readonly Queue<GameLifecycleDispatchFailure> _dispatchFailures = new(DiagnosticCapacity);
    private readonly List<GameLifecycleObservation> _observationsThisFrame = new(8);
    private GameLifecycleSnapshot _current = new(
        GameLifecycleState.NoGame,
        0,
        string.Empty,
        null,
        -1);
    private int? _mainThreadId;
    private long _observationFrame = -1;
    private GameLifecycleState _stateBeforeReset = GameLifecycleState.NoGame;

    public GameLifecycleMonitor(Func<int>? readThreadId = null)
    {
        _readThreadId = readThreadId ?? (() => Environment.CurrentManagedThreadId);
    }

    public static GameLifecycleMonitor Shared { get; } = new();

    public event Action<GameLifecycleTransition>? Transitioned;

    public GameLifecycleSnapshot Current => _current;

    public IReadOnlyList<GameLifecycleDiagnostic> Diagnostics => _diagnostics.ToArray();

    public IReadOnlyList<GameLifecycleDispatchFailure> DispatchFailures => _dispatchFailures.ToArray();

    public GameLifecycleLease CaptureLease() => new(_current.Generation);

    public bool IsCurrent(GameLifecycleLease lease) => lease.Generation == _current.Generation;

    public bool IsCurrent(long generation) => generation == _current.Generation;

    public bool TryObserve(
        GameLifecycleObservation observation,
        out GameLifecycleTransition transition,
        out string reason)
    {
        transition = default;
        var threadId = _readThreadId();
        _mainThreadId ??= threadId;
        if (_mainThreadId.Value != threadId)
        {
            reason = $"lifecycle transition rejected off the main thread; expected={_mainThreadId.Value}; actual={threadId}";
            return false;
        }

        if (observation.Frame < 0)
        {
            reason = "lifecycle transition frame must be non-negative";
            return false;
        }

        if (IsDuplicateInFrame(observation) || IsRedundantStateObservation(observation))
        {
            reason = "equivalent lifecycle transition already observed in this frame";
            return false;
        }

        var previous = _current;
        if (observation.Kind is GameLifecycleTransitionKind.SaveLoadStarted or
            GameLifecycleTransitionKind.ResetStarted or
            GameLifecycleTransitionKind.NewGamePlusStarted)
        {
            if (previous.State != GameLifecycleState.Resetting)
                _stateBeforeReset = previous.State;
        }
        var state = NextState(observation);
        var sceneName = observation.Kind == GameLifecycleTransitionKind.SceneExited
            ? observation.SceneName
            : string.IsNullOrWhiteSpace(observation.SceneName) ? previous.SceneName : observation.SceneName;
        _current = new GameLifecycleSnapshot(
            state,
            checked(previous.Generation + 1),
            sceneName,
            observation.Kind,
            observation.Frame);
        RecordObservation(observation);
        var diagnostic = new GameLifecycleDiagnostic(
            _current.Generation,
            _current.State,
            observation.Kind,
            observation.Frame,
            _current.SceneName,
            observation.Source);
        if (_diagnostics.Count == DiagnosticCapacity) _diagnostics.Dequeue();
        _diagnostics.Enqueue(diagnostic);
        transition = new GameLifecycleTransition(previous, _current, observation.Source);
        reason = string.Empty;
        DispatchTransition(transition);
        return true;
    }

    private void DispatchTransition(GameLifecycleTransition transition)
    {
        var handlers = Transitioned;
        if (handlers is null) return;
        foreach (Action<GameLifecycleTransition> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(transition);
            }
            catch (Exception ex)
            {
                if (_dispatchFailures.Count == DiagnosticCapacity) _dispatchFailures.Dequeue();
                _dispatchFailures.Enqueue(new GameLifecycleDispatchFailure(
                    transition.Current.Generation,
                    handler.Method.DeclaringType?.FullName ?? handler.Method.Name,
                    ex.GetType().FullName ?? ex.GetType().Name));
            }
        }
    }

    private bool IsDuplicateInFrame(GameLifecycleObservation observation)
    {
        if (_observationFrame != observation.Frame)
        {
            return false;
        }

        foreach (var previous in _observationsThisFrame)
        {
            if (previous.Kind != observation.Kind ||
                !string.Equals(previous.SceneName, observation.SceneName, StringComparison.Ordinal))
            {
                continue;
            }

            if (observation.Kind != GameLifecycleTransitionKind.RegistryRebuilt ||
                ReferenceEquals(previous.NativeIdentity, observation.NativeIdentity))
            {
                return true;
            }
        }

        return false;
    }

    private void RecordObservation(GameLifecycleObservation observation)
    {
        if (_observationFrame != observation.Frame)
        {
            _observationFrame = observation.Frame;
            _observationsThisFrame.Clear();
        }

        if (_observationsThisFrame.Count == DiagnosticCapacity)
            _observationsThisFrame.RemoveAt(0);
        _observationsThisFrame.Add(observation);
    }

    private bool IsRedundantStateObservation(GameLifecycleObservation observation)
    {
        return observation.Kind == GameLifecycleTransitionKind.SceneEntered &&
               _current.State is GameLifecycleState.NoGame or GameLifecycleState.Initializing or GameLifecycleState.Playing &&
               string.Equals(_current.SceneName, observation.SceneName, StringComparison.Ordinal);
    }

    private GameLifecycleState NextState(GameLifecycleObservation observation)
    {
        return observation.Kind switch
        {
            GameLifecycleTransitionKind.SceneEntered =>
                string.Equals(observation.SceneName, "Main", StringComparison.Ordinal)
                    ? GameLifecycleState.Initializing
                    : GameLifecycleState.NoGame,
            GameLifecycleTransitionKind.SceneExited => GameLifecycleState.SceneExit,
            GameLifecycleTransitionKind.SaveLoadStarted or
            GameLifecycleTransitionKind.ResetStarted or
            GameLifecycleTransitionKind.NewGamePlusStarted => GameLifecycleState.Resetting,
            GameLifecycleTransitionKind.RuntimeReady or
            GameLifecycleTransitionKind.ResetCompleted => GameLifecycleState.Playing,
            GameLifecycleTransitionKind.SaveLoaded =>
                _stateBeforeReset == GameLifecycleState.Playing
                    ? GameLifecycleState.Playing
                    : GameLifecycleState.Initializing,
            GameLifecycleTransitionKind.RegistryRebuilt => _current.State,
            _ => _current.State,
        };
    }
}
