using System;
using System.Collections.Generic;

namespace OrbModding.Common;

/// <summary>
/// The one place a composed GameAction is registered. Registration owns it: every registered
/// action is rebound on each lifecycle in creation order and disposed in reverse, so no second
/// list can forget one.
/// </summary>
internal sealed class LifecycleGameActions : IDisposable
{
    private readonly List<KeyValuePair<string, ILifecycleGameAction>> _actions = new();

    /// <param name="verb">The MCP verb a caller presses this action through, as status names it.</param>
    internal T Add<T>(string verb, T action) where T : class, ILifecycleGameAction
    {
        if (string.IsNullOrEmpty(verb)) throw new ArgumentException("A verb is required.", nameof(verb));
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (Find(action.GetType()) is not null)
            throw new InvalidOperationException(
                "A second " + action.GetType().Name + " was composed; each GameAction exists once.");
        _actions.Add(new KeyValuePair<string, ILifecycleGameAction>(verb, action));
        return action;
    }

    internal T? Find<T>() where T : class, ILifecycleGameAction => Find(typeof(T)) as T;

    /// <summary>
    /// Each verb whose binding set failed this lifecycle, with the reason of the first of its
    /// actions that failed. A verb two actions answer for is one verb to a caller, so it is one row.
    /// </summary>
    internal UnavailableGameAction[] Unavailable()
    {
        var rows = new List<UnavailableGameAction>();
        foreach (var (verb, action) in _actions)
        {
            if (action.BindingsAvailable || rows.Exists(row => row.Verb == verb)) continue;
            rows.Add(new UnavailableGameAction(verb, action.BindingFailure));
        }
        return rows.ToArray();
    }

    internal void InvalidateLifecycle()
    {
        for (var index = 0; index < _actions.Count; index++)
            _actions[index].Value.InvalidateLifecycle();
    }

    public void Dispose()
    {
        var actions = _actions.ToArray();
        _actions.Clear();
        DisposeFrom(actions, actions.Length - 1);
    }

    private ILifecycleGameAction? Find(Type type)
    {
        for (var index = 0; index < _actions.Count; index++)
        {
            if (_actions[index].Value.GetType() == type) return _actions[index].Value;
        }
        return null;
    }

    private static void DisposeFrom(KeyValuePair<string, ILifecycleGameAction>[] actions, int index)
    {
        if (index < 0) return;
        try
        {
            actions[index].Value.Dispose();
        }
        finally
        {
            DisposeFrom(actions, index - 1);
        }
    }
}

internal readonly struct UnavailableGameAction
{
    internal UnavailableGameAction(string verb, string reason)
    {
        Verb = verb;
        Reason = reason;
    }

    internal string Verb { get; }
    internal string Reason { get; }
}
