using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-bound mutation boundary for every level dial: Output Lv and Reserve Lv on Magic >
/// Casting, Druidry Lv on World > Agromancy and Alchemy Lv on Alchemy > Alchemy > Loadout. All four are a
/// <c>Player</c> <c>IntVariable</c> beside the cap a Raise upgrade lifts, worked by the same
/// <c>UINumberVariableAdjuster</c>.
/// </summary>
internal sealed class SpellCompositionGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private SpellCompositionNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellCompositionGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
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
                GameActionAnswer.NotAttached(Screen(action.Dial), _bindingFailure));

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead(Screen(action.Dial), ex));
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try { return SetDial(in action, native); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead(Screen(action.Dial), ex));
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
        if (!TryAdmitControl(native, action.Dial, out var lockedPreflight, out var lockedReason))
            return SpellCompositionSubmission.Reject(lockedPreflight, lockedReason);
        var player = native.ReadPlayer();
        var variable = player is null ? null : ReadVariable(native, player, action.Dial);
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
                "The " + Name(action.Dial) + " is already " + current + ".");
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
                    "The " + Name(action.Dial) + " is now " + action.Value + ".")
                : Fault(
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    "Expected " + action.Value + ", observed " + observed + ".");
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            if (ReadCurrentBestEffort(native, variable, current) == action.Value)
                return Verified(
                    "The " + Name(action.Dial) + " setter threw after the requested value became observable.");
            return Fault(
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.Dial,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored(Screen(action.Dial), ex));
        }
    }

    private static int ReadCurrentBestEffort(
        SpellCompositionNativeBindings native,
        object variable,
        int fallback)
    {
        try { return native.ReadInt(variable); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex)) { return fallback; }
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
            "Level dial faulted after " + stage + ": " + reason);
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
            reason = "The suite does not own the level-dial action family.";
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

    /// <summary>
    /// Druidry Lv and Alchemy Lv are drawn only once their unlock is bought: each adjuster sits in
    /// a view whose one prerequisite is that upgrade. <c>ViewSO.IsAvailable()</c> is the game's
    /// own question, so the lock is read, never inferred. The Casting dials have no such view.
    /// </summary>
    private bool TryAdmitControl(
        SpellCompositionNativeBindings native,
        LevelDial dial,
        out SpellCompositionPreflight refusal,
        out string reason)
    {
        refusal = SpellCompositionPreflight.Proceeded;
        reason = string.Empty;
        var (viewId, unlock) = dial switch
        {
            LevelDial.Druidry => (KnownEntities.WorldAgromancyDruidryLvs.Uuid, "Unlock Druidry Levels"),
            LevelDial.Alchemy => (KnownEntities.AlchAlchemyLevel.Uuid, "Level Alchemy"),
            _ => (Guid.Empty, string.Empty),
        };
        if (viewId == Guid.Empty) return true;
        var resolution = _registry.Resolve(viewId, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
        {
            refusal = SpellCompositionPreflight.ContractUnavailable;
            reason = "The game's " + Name(dial) + " adjuster could not be read, so whether " +
                Screen(dial) + " draws it at all is unknown.";
            return false;
        }
        if (native.IsViewAvailable(view)) return true;
        refusal = SpellCompositionPreflight.ControlLocked;
        reason = Name(dial) + " is locked: " + Screen(dial) + " draws no " + Name(dial) +
            " adjuster until " + unlock + " is bought.";
        return false;
    }

    private static object? ReadVariable(
        SpellCompositionNativeBindings native,
        object player,
        LevelDial dial) => dial switch
        {
            LevelDial.Output => native.ReadOutputVariable(),
            LevelDial.Reserve => native.ReadReserveVariable(),
            LevelDial.Druidry => native.ReadDruidryVariable(player),
            _ => native.ReadAlchemyVariable(player),
        };

    private static object ReadMaximumVariable(
        SpellCompositionNativeBindings native,
        object player,
        LevelDial dial) => dial switch
        {
            LevelDial.Output => native.ReadMaximumOutputVariable(player),
            LevelDial.Reserve => native.ReadMaximumReserveVariable(player),
            LevelDial.Druidry => native.ReadMaximumDruidryVariable(player),
            _ => native.ReadMaximumAlchemyVariable(player),
        };

    private static string Name(LevelDial dial) => dial switch
    {
        LevelDial.Output => "Output Level",
        LevelDial.Reserve => "Reserve Level",
        LevelDial.Druidry => "Druidry Lv",
        _ => "Alchemy Lv",
    };

    private static string Screen(LevelDial dial) => dial switch
    {
        LevelDial.Druidry => "World > Agromancy",
        LevelDial.Alchemy => "Alchemy > Alchemy > Loadout",
        _ => "Magic > Casting",
    };
}
