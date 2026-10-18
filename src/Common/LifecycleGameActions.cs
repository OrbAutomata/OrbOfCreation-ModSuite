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
    private readonly List<ILifecycleGameAction> _actions = new();

    internal T Add<T>(T action) where T : class, ILifecycleGameAction
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (Find(action.GetType()) is not null)
            throw new InvalidOperationException(
                "A second " + action.GetType().Name + " was composed; each GameAction exists once.");
        _actions.Add(action);
        return action;
    }

    internal T? Find<T>() where T : class, ILifecycleGameAction => Find(typeof(T)) as T;

    internal void InvalidateLifecycle()
    {
        for (var index = 0; index < _actions.Count; index++)
            _actions[index].InvalidateLifecycle();
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
            if (_actions[index].GetType() == type) return _actions[index];
        }
        return null;
    }

    private static void DisposeFrom(ILifecycleGameAction[] actions, int index)
    {
        if (index < 0) return;
        try
        {
            actions[index].Dispose();
        }
        finally
        {
            DisposeFrom(actions, index - 1);
        }
    }
}
