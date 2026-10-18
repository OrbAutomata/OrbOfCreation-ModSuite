using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for the visible Ritual controls.</summary>
internal sealed class RitualLifecycleGameAction : NativeGameAction<RitualLifecycleNativeBindings>
{
    private const string Screen = "Rituals";

    private static readonly GameActionCodes Codes = new(
        RitualLifecycleActionResultCodes.WrongThread,
        RitualLifecycleActionResultCodes.ContractUnavailable,
        RitualLifecycleActionResultCodes.MutationPermitUnavailable,
        RitualLifecycleActionResultCodes.PostCommitFault,
        RitualLifecycleActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal RitualLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Ritual", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out RitualLifecycleNativeBindings? bindings, out string reason) =>
                RitualLifecycleNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal RitualLifecycleSubmission Submit(in RitualLifecycleAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new RitualLifecycleSubmission(outcome),
            native => Press(in request, native));
    }

    private RitualLifecycleSubmission Press(in RitualLifecycleAction action, RitualLifecycleNativeBindings native)
    {
        var resolution = _registry.Resolve(action.RitualId, native.RitualType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return Reject(RitualLifecycleActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("ritual") : resolution.Reason);
        var ritual = resolution.Value!;
        if (!native.IsDiscovered(ritual))
            return Reject(RitualLifecycleActionResultCodes.NotDiscovered,
                EntityIdentityFormatter.PlayerName(action.RitualId) + " has not been discovered.");

        var manager = native.Manager();
        var battle = native.BattleManager();
        if (manager is null || manager.GetType() != native.ManagerType ||
            battle is null || battle.GetType() != native.BattleManagerType)
            return Reject(RitualLifecycleActionResultCodes.ContractUnavailable,
                "The Ritual or battle manager is not available in this scene.");
        var selected = native.SelectedVariable(manager);
        if (selected is null)
            return Reject(RitualLifecycleActionResultCodes.ContractUnavailable,
                "The Ritual selection is not available in this scene.");
        var inCombat = native.IsInCombat(battle);
        if (action.Kind == RitualLifecycleActionKind.EndBattle)
        {
            if (!inCombat)
                return Reject(RitualLifecycleActionResultCodes.NoBattleActive,
                    "There is no active ritual battle to end.");
            var activeRitual = native.ActiveRitual(battle);
            if (activeRitual is null || !native.IsSelected(activeRitual, ritual))
                return Reject(RitualLifecycleActionResultCodes.WrongActiveRitual,
                    EntityIdentityFormatter.PlayerName(action.RitualId) +
                    " is not the ritual currently in battle.");
        }
        else if (inCombat)
        {
            return Reject(RitualLifecycleActionResultCodes.BattleAlreadyActive,
                "Ritual controls are unavailable while a ritual battle is active.");
        }
        if (action.Kind is not RitualLifecycleActionKind.Select and
            not RitualLifecycleActionKind.Deselect and
            not RitualLifecycleActionKind.SetLevel and
            not RitualLifecycleActionKind.Activate and
            not RitualLifecycleActionKind.CancelDuration and
            not RitualLifecycleActionKind.EndBattle)
            return Reject(RitualLifecycleActionResultCodes.ContractUnavailable,
                "That ritual control is not available.");

        object? cost = null;
        var isSelected = native.IsSelected(selected, ritual);
        switch (action.Kind)
        {
            case RitualLifecycleActionKind.Select when isSelected:
                return Reject(RitualLifecycleActionResultCodes.AlreadyInRequestedState,
                    "This ritual is already selected.");
            case RitualLifecycleActionKind.Deselect when !isSelected:
                return Reject(RitualLifecycleActionResultCodes.AlreadyInRequestedState,
                    "This ritual is not selected.");
            case RitualLifecycleActionKind.SetLevel:
                if (native.ForceLevel(ritual))
                    return Reject(RitualLifecycleActionResultCodes.LevelLocked,
                        LevelLockedReason(native.ForceLevelValue(ritual)));
                var minimum = WorldRitualDecision.NativeMinimumStartingLevel;
                var maximum = Math.Max(native.MaximumSelectedLevel(ritual), minimum);
                if (action.Level < minimum || action.Level > maximum)
                    return RitualLifecycleSubmission.RejectLevelOutOfRange(
                        "The ritual starting level must be between " + minimum + " and " +
                        maximum + ".",
                        minimum,
                        maximum);
                if (native.SelectedLevel(ritual) == action.Level)
                    return Reject(RitualLifecycleActionResultCodes.AlreadyInRequestedState,
                        "The ritual starting level is already " + action.Level + ".");
                break;
            case RitualLifecycleActionKind.Activate:
                cost = native.ActivationCost(ritual);
                if (cost is null)
                    return Reject(RitualLifecycleActionResultCodes.ContractUnavailable,
                        "The ritual activation price is unavailable.");
                if (!native.HasEnough(cost))
                    return new(NativeCostShortfall.Refuse(native, cost, "ritual activation",
                        RitualLifecycleActionResultCodes.Unaffordable,
                        RitualLifecycleActionResultCodes.ContractUnavailable));
                break;
            case RitualLifecycleActionKind.CancelDuration:
                if (!native.IsDurationRitual(ritual) || !native.IsDurationActive(ritual))
                    return Reject(RitualLifecycleActionResultCodes.NoDurationEffect,
                        "This ritual has no active duration reward to cancel.");
                break;
        }
        if (!TryPermit(out var refusal)) return new(refusal);

        var kind = action.Kind;
        var level = action.Level;
        return new(Execute(EntityIdentityFormatter.PlayerName(action.RitualId),
            "The requested Ritual transition is visible.", false,
            () => OutcomeObserved(kind, level, native, battle, selected, ritual),
            () => Call(kind, level, native, manager, battle, selected, ritual, cost),
            static (_, landed) => landed));
    }

    private static NativeStop? Call(
        RitualLifecycleActionKind kind,
        int level,
        RitualLifecycleNativeBindings native,
        object manager,
        object battle,
        object selected,
        object ritual,
        object? cost)
    {
        switch (kind)
        {
            case RitualLifecycleActionKind.Select:
            case RitualLifecycleActionKind.Deselect:
                native.ToggleSelected(selected, ritual);
                return null;
            case RitualLifecycleActionKind.SetLevel:
                if (SelectFirst(native, selected, ritual) is { } levelStop) return levelStop;
                native.ChangeStartingLevel(ritual, level);
                return null;
            case RitualLifecycleActionKind.Activate:
                if (SelectFirst(native, selected, ritual) is { } activateStop) return activateStop;
                native.PerformCost(cost!);
                native.ActivateSelected(manager);
                return null;
            case RitualLifecycleActionKind.CancelDuration:
                native.Cancel(ritual);
                return null;
            case RitualLifecycleActionKind.EndBattle:
                native.EndRitual(battle);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>
    /// The screen's own first press, made by the tool rather than demanded of the caller.
    /// </summary>
    /// <remarks>
    /// Both the starting level and the activation act on the selected ritual, and the game's
    /// selection variable holds whichever ritual its toggle was last pressed with — one press, no
    /// matter what was selected before. A caller that asked for a level or an activation was being
    /// refused for a step it could not see and could not have known the order of. The press
    /// overwrites the previous selection and the game keeps no undo for it, so a press that did not
    /// select this ritual stops as an attempted fault rather than a refusal.
    /// </remarks>
    private static NativeStop? SelectFirst(
        RitualLifecycleNativeBindings native,
        object selected,
        object ritual)
    {
        if (native.IsSelected(selected, ritual)) return null;
        native.ToggleSelected(selected, ritual);
        return native.IsSelected(selected, ritual)
            ? null
            : NativeStop.Fault(RitualLifecycleActionResultCodes.VerificationFailed,
                "The ritual did not become the selected one when its selection was pressed.");
    }

    private static bool OutcomeObserved(
        RitualLifecycleActionKind kind,
        int level,
        RitualLifecycleNativeBindings native,
        object battle,
        object selected,
        object ritual) =>
        kind switch
        {
            RitualLifecycleActionKind.Select => native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.Deselect => !native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.SetLevel => native.SelectedLevel(ritual) == level,
            RitualLifecycleActionKind.Activate => native.InBattle(ritual),
            RitualLifecycleActionKind.CancelDuration => !native.IsDurationActive(ritual),
            RitualLifecycleActionKind.EndBattle => !native.IsInCombat(battle),
            _ => false,
        };

    /// <summary>
    /// The one sentence for a ritual that forces its level, naming the level its run is fought at:
    /// <c>RitualSO.GetCurrentLevel()</c> answers <c>forceLevelValue</c> there, and the Ritual panel
    /// draws that number on a disabled selector.
    /// </summary>
    internal static string LevelLockedReason(int level) =>
        "The game fixes this ritual's starting level at " + level + ", so it cannot be set.";

    private static RitualLifecycleSubmission Reject(
        ServiceActionResultCode code,
        string reason) => new(NativeActionOutcome.Reject(code, reason));
}
