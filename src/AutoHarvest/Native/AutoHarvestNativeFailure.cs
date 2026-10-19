using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoHarvestRuntimeFailureKind
{
    None,
    Retryable,
    Contract,
}

internal enum AutoHarvestRuntimeFailureScope
{
    Feature,
    Pair,
}

internal readonly struct AutoHarvestNativeFailure
{
    private AutoHarvestNativeFailure(
        AutoHarvestRuntimeFailureKind kind,
        AutoHarvestRuntimeFailureScope scope,
        GameActionReason reason)
    {
        Kind = kind;
        Scope = scope;
        Reason = reason;
    }

    public AutoHarvestRuntimeFailureKind Kind { get; }
    public AutoHarvestRuntimeFailureScope Scope { get; }
    public GameActionReason Reason { get; }

    public bool IsValid =>
        Kind is AutoHarvestRuntimeFailureKind.Retryable or AutoHarvestRuntimeFailureKind.Contract &&
        Scope is AutoHarvestRuntimeFailureScope.Feature or AutoHarvestRuntimeFailureScope.Pair;

    public static AutoHarvestNativeFailure Create(
        AutoHarvestRuntimeFailureKind kind,
        AutoHarvestRuntimeFailureScope scope,
        GameActionReason reason = default)
    {
        if (kind is not AutoHarvestRuntimeFailureKind.Retryable and not AutoHarvestRuntimeFailureKind.Contract)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (scope is not AutoHarvestRuntimeFailureScope.Feature and not AutoHarvestRuntimeFailureScope.Pair)
            throw new ArgumentOutOfRangeException(nameof(scope));
        return new AutoHarvestNativeFailure(kind, scope, reason);
    }
}
