using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for the visible Ritual controls.</summary>
internal sealed class RitualLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private RitualLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal RitualLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
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

    internal RitualLifecycleSubmission Submit(in RitualLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(RitualLifecyclePreflight.WrongThread,
                "Ritual controls are bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return Reject(RitualLifecyclePreflight.ContractUnavailable, _bindingFailure);

        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(RitualLifecyclePreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(RitualLifecyclePreflight.LifecycleReplaced,
                "The submitted game lifecycle is stale.");

        try
        {
            var resolution = _registry.Resolve(action.RitualId, native.RitualType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(RitualLifecyclePreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? "The ritual resolution became stale."
                        : resolution.Reason);
            var ritual = resolution.Value!;
            if (!native.IsDiscovered(ritual))
                return Reject(RitualLifecyclePreflight.NotDiscovered,
                    EntityIdentityFormatter.PlayerName(action.RitualId) +
                    " has not been discovered.");

            var manager = native.Manager();
            var battle = native.BattleManager();
            if (manager is null || manager.GetType() != native.ManagerType ||
                battle is null || battle.GetType() != native.BattleManagerType)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "The Ritual or battle manager is not available in this scene.");
            var selected = native.SelectedVariable(manager);
            if (selected is null)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "The Ritual selection is not available in this scene.");
            var inCombat = native.IsInCombat(battle);
            if (action.Kind == RitualLifecycleActionKind.EndBattle)
            {
                if (!inCombat)
                    return Reject(RitualLifecyclePreflight.NoBattleActive,
                        "There is no active ritual battle to end.");
                var activeRitual = native.ActiveRitual(battle);
                if (activeRitual is null || !native.IsSelected(activeRitual, ritual))
                    return Reject(RitualLifecyclePreflight.WrongActiveRitual,
                        EntityIdentityFormatter.PlayerName(action.RitualId) +
                        " is not the ritual currently in battle.");
            }
            else if (inCombat)
            {
                return Reject(RitualLifecyclePreflight.BattleAlreadyActive,
                    "Ritual controls are unavailable while a ritual battle is active.");
            }
            if (action.Kind is not RitualLifecycleActionKind.Select and
                not RitualLifecycleActionKind.Deselect and
                not RitualLifecycleActionKind.SetLevel and
                not RitualLifecycleActionKind.Activate and
                not RitualLifecycleActionKind.CancelDuration and
                not RitualLifecycleActionKind.EndBattle)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "That ritual control is not available.");

            var isSelected = native.IsSelected(selected, ritual);
            switch (action.Kind)
            {
                case RitualLifecycleActionKind.Select when isSelected:
                    return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                        "This ritual is already selected.");
                case RitualLifecycleActionKind.Deselect when !isSelected:
                    return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                        "This ritual is not selected.");
                case RitualLifecycleActionKind.SetLevel:
                    if (native.ForceLevel(ritual))
                        return Reject(RitualLifecyclePreflight.LevelLocked,
                            "This ritual fixes its starting level at " +
                            native.ForceLevelValue(ritual) + ".");
                    var minimum = WorldRitualDecision.NativeMinimumStartingLevel;
                    var maximum = Math.Max(native.MaximumSelectedLevel(ritual), minimum);
                    if (action.Level < minimum || action.Level > maximum)
                        return RitualLifecycleSubmission.RejectLevelOutOfRange(
                            "The ritual starting level must be between " + minimum + " and " +
                            maximum + ".",
                            minimum,
                            maximum);
                    if (native.SelectedLevel(ritual) == action.Level)
                        return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                            "The ritual starting level is already " + action.Level + ".");
                    break;
                case RitualLifecycleActionKind.Activate:
                    var cost = native.ActivationCost(ritual);
                    if (cost is null)
                        return Reject(RitualLifecyclePreflight.ContractUnavailable,
                            "The ritual activation price is unavailable.");
                    if (!native.HasEnough(cost))
                        return Unaffordable(native, cost);
                    break;
                case RitualLifecycleActionKind.CancelDuration:
                    if (!native.IsDurationRitual(ritual) || !native.IsDurationActive(ritual))
                        return Reject(RitualLifecyclePreflight.NoDurationEffect,
                            "This ritual has no active duration reward to cancel.");
                    break;
            }

            if (!_tryCaptureMutationPermit())
                return Reject(RitualLifecyclePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, manager, battle, selected, ritual);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(RitualLifecyclePreflight.ContractUnavailable,
                "Ritual preflight failed before mutation: " +
                exception.GetBaseException().Message);
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

    /// <summary>
    /// The screen's own first press, made by the tool rather than demanded of the caller.
    /// </summary>
    /// <remarks>
    /// Both the starting level and the activation act on the selected ritual, and the game's
    /// selection variable holds whichever ritual its toggle was last pressed with — one press, no
    /// matter what was selected before. A caller that asked for a level or an activation was being
    /// refused for a step it could not see and could not have known the order of.
    /// </remarks>
    private static bool TrySelectFirst(
        RitualLifecycleNativeBindings native,
        object selected,
        object ritual,
        out RitualLifecycleSubmission failure)
    {
        failure = default;
        if (native.IsSelected(selected, ritual)) return true;
        native.ToggleSelected(selected, ritual);
        if (native.IsSelected(selected, ritual)) return true;
        failure = Reject(RitualLifecyclePreflight.VerificationFailed,
            "The ritual did not become the selected one when its selection was pressed.");
        return false;
    }

    private static RitualLifecycleSubmission Execute(
        in RitualLifecycleAction action,
        RitualLifecycleNativeBindings native,
        object manager,
        object battle,
        object selected,
        object ritual)
    {
        var stage = RitualLifecycleNativeStage.NativeCallback;
        try
        {
            if (action.Kind is RitualLifecycleActionKind.Select or
                RitualLifecycleActionKind.Deselect)
            {
                native.ToggleSelected(selected, ritual);
            }
            else if (action.Kind == RitualLifecycleActionKind.SetLevel)
            {
                if (!TrySelectFirst(native, selected, ritual, out var levelFailure))
                    return levelFailure;
                native.ChangeStartingLevel(ritual, action.Level);
            }
            else if (action.Kind == RitualLifecycleActionKind.Activate)
            {
                var cost = native.ActivationCost(ritual) ??
                    throw new InvalidOperationException(
                        "RitualSO.GetActivationCost returned null before payment");
                if (!TrySelectFirst(native, selected, ritual, out var activateFailure))
                    return activateFailure;
                if (!native.HasEnough(cost)) return Unaffordable(native, cost);
                stage = RitualLifecycleNativeStage.Payment;
                native.PerformCost(cost);
                stage = RitualLifecycleNativeStage.NativeCallback;
                native.ActivateSelected(manager);
            }
            else if (action.Kind == RitualLifecycleActionKind.CancelDuration)
            {
                native.Cancel(ritual);
            }
            else if (action.Kind == RitualLifecycleActionKind.EndBattle)
            {
                if (!native.IsInCombat(battle))
                    return Reject(RitualLifecyclePreflight.NoBattleActive,
                        "The ritual battle ended before this control ran.");
                var activeRitual = native.ActiveRitual(battle);
                if (activeRitual is null || !native.IsSelected(activeRitual, ritual))
                    return Reject(RitualLifecyclePreflight.WrongActiveRitual,
                        EntityIdentityFormatter.PlayerName(action.RitualId) +
                        " is no longer the ritual in battle.");
                native.EndRitual(battle);
            }
            else
            {
                throw new InvalidOperationException("Unsupported ritual control.");
            }

            stage = RitualLifecycleNativeStage.Verification;
            return OutcomeObserved(in action, native, battle, selected, ritual)
                ? Verified()
                : Fault(in action, RitualLifecyclePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The requested Ritual transition was not observable.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeObserved(in action, native, battle, selected, ritual)) return Verified();
            return Fault(in action, RitualLifecyclePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native Ritual callback threw before the requested transition was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static bool OutcomeObserved(
        in RitualLifecycleAction action,
        RitualLifecycleNativeBindings native,
        object battle,
        object selected,
        object ritual) =>
        action.Kind switch
        {
            RitualLifecycleActionKind.Select => native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.Deselect => !native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.SetLevel => native.SelectedLevel(ritual) == action.Level,
            RitualLifecycleActionKind.Activate => native.InBattle(ritual),
            RitualLifecycleActionKind.CancelDuration => !native.IsDurationActive(ritual),
            RitualLifecycleActionKind.EndBattle => !native.IsInCombat(battle),
            _ => false,
        };

    private static RitualLifecycleSubmission Unaffordable(
        RitualLifecycleNativeBindings native,
        object cost)
    {
        var rows = native.CostEntries(cost);
        if (rows is null)
            return Reject(RitualLifecyclePreflight.ContractUnavailable,
                "The ritual activation price has no readable resource rows.");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.CostResource(row);
            if (resource is null) continue;
            var amount = native.CostValue(row);
            if (!native.HasResourceAmount(resource, amount))
                return Reject(RitualLifecyclePreflight.Unaffordable,
                    EntityIdentityFormatter.PlayerName(native.ResourceGuid(resource)) +
                    " is short for this ritual activation.");
        }
        return Reject(RitualLifecyclePreflight.ContractUnavailable,
            "The game refused this ritual price without identifying a short resource.");
    }

    private static RitualLifecycleSubmission Reject(
        RitualLifecyclePreflight preflight,
        string reason) => RitualLifecycleSubmission.Reject(preflight, reason);

    private static RitualLifecycleSubmission Verified() =>
        new(RitualLifecyclePreflight.Proceeded,
            RitualLifecycleNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested Ritual transition is visible.");

    private static RitualLifecycleSubmission Fault(
        in RitualLifecycleAction action,
        RitualLifecyclePreflight preflight,
        RitualLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Ritual " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.RitualId) + ": " + reason);

    private void BindLifecycle()
    {
        if (RitualLifecycleNativeBindings.TryCreate(
                out var bindings,
                out var reason,
                _resolveType,
                _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}
