using System;

namespace OrbModding.Common;

/// <summary>
/// What the owner of a GameAction drives: one rebind per lifecycle, one teardown, and the
/// binding failure a status surface reports.
/// </summary>
internal interface ILifecycleGameAction : IDisposable
{
    bool BindingsAvailable { get; }
    string BindingFailure { get; }
    void InvalidateLifecycle();
}
