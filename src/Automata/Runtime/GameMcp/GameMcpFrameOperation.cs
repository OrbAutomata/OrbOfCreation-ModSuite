#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbAutomata.GameMcp;

internal enum GameMcpOperationClass
{
    ReadOnly = 0,
    UiState = 1,
    SuiteAdministration = 2,
    Gameplay = 3,
}

/// <summary>Executable assertion that transport/JSON work never leaks onto Unity frame execution.</summary>
internal static class GameMcpFrameThreadBoundary
{
    [ThreadStatic]
    private static int _executionDepth;

    internal static void Enter() => _executionDepth++;

    internal static void Exit()
    {
        if (_executionDepth <= 0)
            throw new InvalidOperationException("the MCP frame boundary exit was unbalanced");
        _executionDepth--;
    }

    internal static void AssertTransportWorkAllowed(string operation)
    {
        if (_executionDepth > 0)
            throw new InvalidOperationException(
                operation + " is HTTP/worker work and cannot run inside a Unity frame operation");
    }
}

/// <summary>
/// Native-free validated arguments copied from one HTTP request. Fields are deliberately scalar,
/// immutable arrays, or immutable selectors; JSON and game objects cannot cross the inbox seam.
/// </summary>
internal sealed class GameMcpOperationRequest
{
    internal GameMcpOperationRequest(GameMcpOperationRequestBuilder source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(source.ToolName))
            throw new ArgumentException("A tool name is required.", nameof(source));
        ToolName = source.ToolName;
        Classification = source.Classification;
        RequiredData = source.RequiredData;
        Category = source.Category ?? string.Empty;
        Query = source.Query ?? string.Empty;
        Uuid = source.Uuid;
        SecondaryUuid = source.SecondaryUuid;
        Uuids = source.Uuids is null ? Array.Empty<string>() : (string[])source.Uuids.Clone();
        UuidCounts = source.UuidCounts is null
            ? Array.Empty<GameMcpUuidCount>()
            : (GameMcpUuidCount[])source.UuidCounts.Clone();
        Mode = source.Mode ?? string.Empty;
        Offset = source.Offset;
        Limit = source.Limit;
        LimitFromCaller = source.LimitFromCaller;
        Amount = source.Amount;
        SlotIndex = source.SlotIndex;
        Section = source.Section ?? string.Empty;
        Key = source.Key ?? string.Empty;
        SerializedValue = source.SerializedValue ?? string.Empty;
        Path = source.Path ?? string.Empty;
        Probe = source.Probe ?? string.Empty;
        AffordableOnly = source.AffordableOnly;
        StateFilter = source.StateFilter ?? string.Empty;
        SaveCapture = source.SaveCapture;
        ResourceUri = source.ResourceUri ?? string.Empty;
        Tab = source.Tab;
        Subtab = source.Subtab;
    }

    internal string ToolName { get; }
    internal GameMcpOperationClass Classification { get; }
    internal GameMcpFrameData RequiredData { get; }
    internal string Category { get; }
    internal string Query { get; }
    internal Guid Uuid { get; }
    internal Guid SecondaryUuid { get; }
    internal string[] Uuids { get; }
    internal GameMcpUuidCount[] UuidCounts { get; }
    internal string Mode { get; }
    internal int Offset { get; }
    internal int Limit { get; }

    /// <summary>
    /// Whether <see cref="Limit"/> is the caller's own page size or the one the surface picked for
    /// them. A read that asked for no page size is free to be given the whole of a small category;
    /// one that asked for five rows gets five, and the difference is not readable from the number.
    /// </summary>
    internal bool LimitFromCaller { get; }

    internal int Amount { get; }
    internal int SlotIndex { get; }
    internal string Section { get; }
    internal string Key { get; }
    internal string SerializedValue { get; }
    internal string Path { get; }
    internal string Probe { get; }
    internal bool AffordableOnly { get; }

    /// <summary>The one lifecycle word a search was narrowed to, or nothing.</summary>
    internal string StateFilter { get; }
    internal bool SaveCapture { get; }
    internal string ResourceUri { get; }
    internal GameMcpNavigationSelector? Tab { get; }
    internal GameMcpNavigationSelector? Subtab { get; }
}

internal sealed class GameMcpOperationRequestBuilder
{
    internal string ToolName { get; set; } = string.Empty;
    internal GameMcpOperationClass Classification { get; set; }
    internal GameMcpFrameData RequiredData { get; set; }
    internal string Category { get; set; } = string.Empty;
    internal string Query { get; set; } = string.Empty;
    internal Guid Uuid { get; set; }
    internal Guid SecondaryUuid { get; set; }
    internal string[] Uuids { get; set; } = Array.Empty<string>();
    internal GameMcpUuidCount[] UuidCounts { get; set; } = Array.Empty<GameMcpUuidCount>();
    internal string Mode { get; set; } = string.Empty;
    internal int Offset { get; set; }
    internal int Limit { get; set; } = 1;
    internal bool LimitFromCaller { get; set; }
    internal int Amount { get; set; } = 1;
    internal int SlotIndex { get; set; }
    internal string Section { get; set; } = string.Empty;
    internal string Key { get; set; } = string.Empty;
    internal string SerializedValue { get; set; } = string.Empty;
    internal string Path { get; set; } = string.Empty;
    internal string Probe { get; set; } = string.Empty;
    internal bool AffordableOnly { get; set; }
    internal string StateFilter { get; set; } = string.Empty;
    internal bool SaveCapture { get; set; }
    internal string ResourceUri { get; set; } = string.Empty;
    internal GameMcpNavigationSelector? Tab { get; set; }
    internal GameMcpNavigationSelector? Subtab { get; set; }

    internal GameMcpOperationRequest Freeze() => new(this);
}

internal readonly struct GameMcpUuidCount
{
    internal GameMcpUuidCount(Guid uuid, int count)
    {
        if (uuid == Guid.Empty) throw new ArgumentException("A UUID is required.", nameof(uuid));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        Uuid = uuid;
        Count = count;
    }

    internal Guid Uuid { get; }
    internal int Count { get; }
}

internal sealed class GameMcpNavigationSelector
{
    internal GameMcpNavigationSelector(string label)
    {
        Label = label ?? string.Empty;
    }

    internal string Label { get; }
}

internal sealed class GameMcpFrameOperation
{
    internal GameMcpFrameOperation(long sequence, GameMcpOperationRequest request)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Completion = new GameMcpFrameOperationCompletion();
    }

    internal long Sequence { get; }
    internal GameMcpOperationRequest Request { get; }
    internal GameMcpFrameOperationCompletion Completion { get; }
}

internal sealed class GameMcpFrameOperationCompletion
{
    private readonly object _sync = new();
    private GameMcpToolExecution? _result;
    private GameMcpOperationClaimState _state;

    internal bool TryClaim()
    {
        lock (_sync)
        {
            if (_state != GameMcpOperationClaimState.Pending) return false;
            _state = GameMcpOperationClaimState.Claimed;
            return true;
        }
    }

    internal bool TryCancelBeforeClaim(GameMcpToolExecution result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (_sync)
        {
            if (_state != GameMcpOperationClaimState.Pending) return false;
            _state = GameMcpOperationClaimState.Terminal;
            _result = result;
            Monitor.PulseAll(_sync);
            return true;
        }
    }

    internal bool TryWait(TimeSpan timeout, out GameMcpToolExecution result)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var deadline = DateTime.UtcNow + timeout;
        lock (_sync)
        {
            while (_result is null)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_sync, remaining))
                {
                    result = null!;
                    return false;
                }
            }
            result = _result;
            return true;
        }
    }

    internal GameMcpToolExecution WaitForClaimedTerminal()
    {
        lock (_sync)
        {
            while (_result is null) Monitor.Wait(_sync);
            return _result;
        }
    }

    internal void Complete(GameMcpToolExecution result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (_sync)
        {
            if (_result is not null || _state == GameMcpOperationClaimState.Terminal)
                throw new InvalidOperationException("an MCP frame operation completed more than once");
            _state = GameMcpOperationClaimState.Terminal;
            _result = result;
            Monitor.PulseAll(_sync);
        }
    }
}

internal enum GameMcpOperationClaimState
{
    Pending = 0,
    Claimed = 1,
    Terminal = 2,
}

/// <summary>
/// The sole HTTP-to-Unity inbox. ClaimPending atomically detaches every operation visible at the
/// boundary; later arrivals remain for the next frame. There is no capacity, priority, or cadence.
/// </summary>
internal sealed class GameMcpFrameInbox
{
    private readonly object _sync = new();
    private readonly List<GameMcpFrameOperation> _pending = new();
    private long _nextSequence;
    private bool _closed;
    private string _closeCode = string.Empty;
    private string _closeReason = string.Empty;

    internal GameMcpFrameOperation Submit(GameMcpOperationRequest request)
    {
        var operation = new GameMcpFrameOperation(
            Interlocked.Increment(ref _nextSequence),
            request);
        lock (_sync)
        {
            if (_closed)
            {
                operation.Completion.Complete(CloseResult(_closeCode, _closeReason));
                return operation;
            }
            _pending.Add(operation);
            return operation;
        }
    }

    internal GameMcpFrameOperation[] ClaimPending()
    {
        lock (_sync)
        {
            if (_pending.Count == 0) return Array.Empty<GameMcpFrameOperation>();
            var claimed = new GameMcpFrameOperation[_pending.Count];
            var count = 0;
            for (var index = 0; index < _pending.Count; index++)
            {
                var operation = _pending[index];
                if (operation.Completion.TryClaim()) claimed[count++] = operation;
            }
            _pending.Clear();
            if (count == claimed.Length) return claimed;
            if (count == 0) return Array.Empty<GameMcpFrameOperation>();
            Array.Resize(ref claimed, count);
            return claimed;
        }
    }

    internal void Complete(GameMcpFrameOperation operation, GameMcpToolExecution result)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        operation.Completion.Complete(result);
    }

    internal void Close(string code, string reason)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException(nameof(code));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException(nameof(reason));
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            _closeCode = code;
            _closeReason = reason;
            var terminal = CloseResult(code, reason);
            for (var index = 0; index < _pending.Count; index++)
                _pending[index].Completion.TryCancelBeforeClaim(terminal);
            _pending.Clear();
        }
    }

    private static GameMcpToolExecution CloseResult(string code, string reason)
    {
        var payload = new GameMcpObjectBuilder
        {
            ["status"] = "rejected",
            ["code"] = code,
            ["reason"] = reason,
        };
        return GameMcpToolExecution.Error(payload.Freeze());
    }
}

/// <summary>
/// The one Unity-frame claim/capture/execute boundary. An empty inbox returns before the context
/// factory (and therefore every world/configuration/health/native owner) is touched. A non-empty
/// claim receives one context by reference and executes every claimed operation in assigned order;
/// a null execution is an explicitly asynchronous UI gadget whose completion callback already owns
/// the terminal result.
/// </summary>
internal static class GameMcpFrameBatchExecutor
{
    internal static int Drain(
        GameMcpFrameInbox inbox,
        Func<GameMcpFrameData, GameMcpFrameContext> captureContext,
        Func<GameMcpFrameOperation, GameMcpFrameContext, GameMcpToolExecution?> execute,
        Func<GameMcpFrameOperation, GameMcpFrameContext?, Exception, GameMcpToolExecution> fault)
    {
        if (inbox is null) throw new ArgumentNullException(nameof(inbox));
        if (captureContext is null) throw new ArgumentNullException(nameof(captureContext));
        if (execute is null) throw new ArgumentNullException(nameof(execute));
        if (fault is null) throw new ArgumentNullException(nameof(fault));

        var operations = inbox.ClaimPending();
        if (operations.Length == 0) return 0;

        GameMcpFrameThreadBoundary.Enter();
        try
        {
            var required = GameMcpFrameData.None;
            for (var index = 0; index < operations.Length; index++)
                required |= operations[index].Request.RequiredData;
            GameMcpFrameContext context;
            try
            {
                context = captureContext(required);
            }
            catch (Exception exception)
            {
                for (var index = 0; index < operations.Length; index++)
                    inbox.Complete(operations[index], fault(operations[index], null, exception));
                return operations.Length;
            }

            for (var index = 0; index < operations.Length; index++)
            {
                var operation = operations[index];
                try
                {
                    var terminal = execute(operation, context);
                    if (terminal is not null) inbox.Complete(operation, terminal);
                }
                catch (Exception exception)
                {
                    inbox.Complete(operation, fault(operation, context, exception));
                }
            }
            return operations.Length;
        }
        finally
        {
            GameMcpFrameThreadBoundary.Exit();
        }
    }
}
#endif
