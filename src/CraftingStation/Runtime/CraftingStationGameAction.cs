using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for visible Brewing Station controls.</summary>
internal sealed class CraftingStationGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private CraftingStationNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal CraftingStationGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
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

    internal CraftingStationSubmission Submit(in CraftingStationAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(CraftingStationPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(CraftingStationPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Workshop > Crafting"));

        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(CraftingStationPreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(CraftingStationPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            if (!TryFindStation(native, action.StationId, out var structure, out var station, out var reason))
                return Reject(CraftingStationPreflight.IdentityUnavailable, reason);
            if (action.Kind is not CraftingStationActionKind.SetIngredient and
                not CraftingStationActionKind.SetOutput and
                not CraftingStationActionKind.SetLevel and
                not CraftingStationActionKind.Start and
                not CraftingStationActionKind.Stop)
                return Reject(CraftingStationPreflight.ContractUnavailable,
                    "That Brewing Station control is not available.");

            object? selection = null;
            switch (action.Kind)
            {
                case CraftingStationActionKind.SetIngredient:
                    if (action.Value is not 0 and not 1)
                        return Reject(CraftingStationPreflight.SelectionUnavailable,
                            "A Brewing Station ingredient slot must be 0 or 1.");
                    if (!TryFindIngredient(native, structure!, action.Value, action.SelectionId,
                            out selection))
                        return Reject(CraftingStationPreflight.SelectionUnavailable,
                            "That ingredient is not offered for slot " + action.Value + ".");
                    if (!native.ElementAvailable(selection!))
                        return Reject(CraftingStationPreflight.SelectionHidden,
                            "That ingredient is not currently available.");
                    if (ReadElementId(native, native.Ingredient(station!, action.Value)) ==
                        action.SelectionId)
                        return Reject(CraftingStationPreflight.AlreadyInRequestedState,
                            "That ingredient is already selected in slot " + action.Value + ".");
                    break;
                case CraftingStationActionKind.SetOutput:
                    if (!TryFindOutput(native, station!, action.SelectionId, out selection))
                        return Reject(CraftingStationPreflight.SelectionUnavailable,
                            "That output is not offered by this Brewing Station.");
                    if (!native.OutputVisible(station!, selection!))
                        return Reject(CraftingStationPreflight.SelectionHidden,
                            "That output is not currently visible.");
                    if (ReadElementId(native, native.Output(station!)) == action.SelectionId)
                        return Reject(CraftingStationPreflight.AlreadyInRequestedState,
                            "That output is already selected.");
                    break;
                case CraftingStationActionKind.SetLevel:
                    var minimum = native.MinimumLevel(station!);
                    var maximum = native.MaximumLevel(station!);
                    if (action.Value < minimum || action.Value > maximum)
                        return Reject(CraftingStationPreflight.LevelOutOfRange,
                            "The Brewing Station level must be between " + minimum +
                            " and " + maximum + ".");
                    if (native.Level(station!) == action.Value)
                        return Reject(CraftingStationPreflight.AlreadyInRequestedState,
                            "The Brewing Station level is already " + action.Value + ".");
                    break;
                case CraftingStationActionKind.Start:
                    if (!native.Loaded(station!))
                        return Reject(CraftingStationPreflight.NotLoaded,
                            "Choose a complete Brewing Station recipe before starting it.");
                    if (native.Active(station!))
                        return Reject(CraftingStationPreflight.AlreadyInRequestedState,
                            "This Brewing Station is already brewing.");
                    break;
                case CraftingStationActionKind.Stop:
                    if (!native.Active(station!))
                        return Reject(CraftingStationPreflight.AlreadyInRequestedState,
                            "This Brewing Station is already stopped.");
                    break;
            }

            if (!_tryCaptureMutationPermit())
                return Reject(CraftingStationPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, station!, selection);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(CraftingStationPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Workshop > Crafting"));
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

    private static CraftingStationSubmission Execute(
        in CraftingStationAction action,
        CraftingStationNativeBindings native,
        object station,
        object? selection)
    {
        var stage = CraftingStationNativeStage.NativeCallback;
        try
        {
            switch (action.Kind)
            {
                case CraftingStationActionKind.SetIngredient:
                    native.SetIngredient(station, action.Value, selection!);
                    break;
                case CraftingStationActionKind.SetOutput:
                    native.SetOutput(station, selection!);
                    break;
                case CraftingStationActionKind.SetLevel:
                    native.SetLevel(station, action.Value);
                    break;
                case CraftingStationActionKind.Start:
                    native.SetActive(station, true);
                    break;
                case CraftingStationActionKind.Stop:
                    native.SetActive(station, false);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Brewing Station control.");
            }

            stage = CraftingStationNativeStage.Verification;
            return OutcomeObserved(in action, native, station)
                ? Verified()
                : Fault(in action, CraftingStationPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("Workshop > Crafting"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeObserved(in action, native, station)) return Verified();
            return Fault(in action, CraftingStationPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("Workshop > Crafting"));
        }
    }

    private static bool OutcomeObserved(
        in CraftingStationAction action,
        CraftingStationNativeBindings native,
        object station) =>
        action.Kind switch
        {
            CraftingStationActionKind.SetIngredient =>
                ReadElementId(native, native.Ingredient(station, action.Value)) == action.SelectionId,
            CraftingStationActionKind.SetOutput =>
                ReadElementId(native, native.Output(station)) == action.SelectionId,
            CraftingStationActionKind.SetLevel => native.Level(station) == action.Value,
            CraftingStationActionKind.Start => native.Active(station),
            CraftingStationActionKind.Stop => !native.Active(station),
            _ => false,
        };

    private static bool TryFindStation(
        CraftingStationNativeBindings native,
        Guid stationId,
        out object? structure,
        out object? station,
        out string reason)
    {
        structure = null;
        station = null;
        var structures = native.Structures();
        if (structures is null)
        {
            reason = "Brewing Stations are not available in this scene.";
            return false;
        }
        for (var structureIndex = 0; structureIndex < structures.Count; structureIndex++)
        {
            var owner = structures[structureIndex];
            if (owner is null || owner.GetType() != native.StructureType) continue;
            var instances = native.Instances(owner);
            for (var stationIndex = 0; stationIndex < (instances?.Count ?? 0); stationIndex++)
            {
                var candidate = instances![stationIndex];
                if (candidate is null || candidate.GetType() != native.StationType ||
                    native.StationId(candidate) != stationId) continue;
                if (!ReferenceEquals(native.StationReference(candidate), owner))
                {
                    reason = "The game moved this Brewing Station since it was read; open " +
                        "Workshop > Crafting and read it again.";
                    return false;
                }
                structure = owner;
                station = candidate;
                reason = string.Empty;
                return true;
            }
        }
        reason = "Brewing Station " + stationId + " is not present in the current game.";
        return false;
    }

    private static bool TryFindIngredient(
        CraftingStationNativeBindings native,
        object structure,
        int slot,
        Guid selectionId,
        out object? selection)
    {
        selection = null;
        var lists = native.IngredientLists(structure);
        if (lists is null || slot >= lists.Count || lists[slot] is not { } list) return false;
        return TryFindElement(native, native.Elements(list), selectionId, out selection);
    }

    private static bool TryFindOutput(
        CraftingStationNativeBindings native,
        object station,
        Guid selectionId,
        out object? selection) =>
        TryFindElement(native, native.OutputList(station), selectionId, out selection);

    private static bool TryFindElement(
        CraftingStationNativeBindings native,
        IList? elements,
        Guid selectionId,
        out object? selection)
    {
        selection = null;
        for (var index = 0; index < (elements?.Count ?? 0); index++)
        {
            var element = elements![index];
            if (element is null || element.GetType() != native.ElementType ||
                native.ElementId(element) != selectionId) continue;
            selection = element;
            return true;
        }
        return false;
    }

    private static Guid ReadElementId(CraftingStationNativeBindings native, object? element) =>
        element is null || element.GetType() != native.ElementType
            ? Guid.Empty
            : native.ElementId(element);

    private static CraftingStationSubmission Reject(
        CraftingStationPreflight preflight,
        string reason) => CraftingStationSubmission.Reject(preflight, reason);

    private static CraftingStationSubmission Verified() =>
        new(CraftingStationPreflight.Proceeded,
            CraftingStationNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested Brewing Station transition is visible.");

    private static CraftingStationSubmission Fault(
        in CraftingStationAction action,
        CraftingStationPreflight preflight,
        CraftingStationNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Brewing Station " + stage + " failed on " + action.StationId + ": " + reason);

    private void BindLifecycle()
    {
        if (CraftingStationNativeBindings.TryCreate(
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
