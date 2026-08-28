using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>Lifecycle-bound mutation boundary for both global Casting-screen dials.</summary>
internal sealed class SpellCompositionGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellCompositionNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellCompositionGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal SpellCompositionSubmission Submit(in SpellCompositionAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Magic > Casting"));

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try { return SetDial(in action, native); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Magic > Casting"));
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private SpellCompositionSubmission SetDial(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        var player = native.ReadPlayer();
        var variable = ReadVariable(native, action.Dial);
        if (player is null || variable is null)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "The game has not set up " + Name(action.Dial) + " yet.");
        var current = native.ReadInt(variable);
        var maximum = native.ReadInt(ReadMaximumVariable(native, player, action.Dial));
        var minimum = WorldSpellWorkbench.MinimumDialLevel;
        if (action.Value < minimum || action.Value > maximum)
            return SpellCompositionSubmission.OutOfRange(
                Name(action.Dial) + " " + action.Value + " is outside the range the game allows, " +
                minimum + " to " + maximum + ".",
                minimum,
                maximum);
        if (current == action.Value)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.AlreadyInRequestedState,
                "The global " + Name(action.Dial) + " is already " + current + ".");
        if (!TryCapturePermit(out var reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.SetInt(variable, action.Value);
            var observed = native.ReadInt(variable);
            return observed == action.Value
                ? Verified(
                    "The global " + Name(action.Dial) + " is now " + action.Value + ".")
                : Fault(
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    "Expected " + action.Value + ", observed " + observed + ".");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (ReadCurrentBestEffort(native, variable, current) == action.Value)
                return Verified(
                    "The " + Name(action.Dial) + " setter threw after the requested value became observable.");
            return Fault(
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.Dial,
                NativeMutationOutcome.ExecutionThrew,
                "The " + Name(action.Dial) + " setter threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static int ReadCurrentBestEffort(
        SpellCompositionNativeBindings native,
        object variable,
        int fallback)
    {
        try { return native.ReadInt(variable); }
        catch (Exception ex) when (IsExpected(ex)) { return fallback; }
    }

    private static SpellCompositionSubmission Verified(
        string reason)
    {
        return new SpellCompositionSubmission(
            SpellCompositionPreflight.Proceeded,
            SpellCompositionNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            reason);
    }

    private static SpellCompositionSubmission Fault(
        SpellCompositionPreflight preflight,
        SpellCompositionNativeStage stage,
        NativeMutationOutcome outcome,
        string reason)
    {
        return new SpellCompositionSubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(1, 1, 0),
            "Casting dial faulted after " + stage + ": " + reason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit())
        {
            reason = string.Empty;
            return true;
        }
        reason = _readOwnershipFailure();
        if (reason.Length == 0)
            reason = "The suite does not own the Casting-dial action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!SpellCompositionNativeBindings.TryCreate(
                resolve,
                include,
                out _bindings,
                out _bindingFailure))
            _bindings = null;
    }

    private static bool IsExpected(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or OverflowException or
            TargetInvocationException or MemberAccessException;

    private static object? ReadVariable(
        SpellCompositionNativeBindings native,
        CastingDial dial) => dial == CastingDial.Output
            ? native.ReadOutputVariable()
            : native.ReadReserveVariable();

    private static object ReadMaximumVariable(
        SpellCompositionNativeBindings native,
        object player,
        CastingDial dial) => dial == CastingDial.Output
            ? native.ReadMaximumOutputVariable(player)
            : native.ReadMaximumReserveVariable(player);

    private static string Name(CastingDial dial) => dial == CastingDial.Output
        ? "Output Level"
        : "Reserve Level";
}
