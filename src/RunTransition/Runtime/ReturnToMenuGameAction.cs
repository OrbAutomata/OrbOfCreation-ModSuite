using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the native Back to Main Menu control.</summary>
internal sealed class ReturnToMenuGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string> _readScene;
    private readonly Func<Type, object[]> _findLoadedObjects;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private ReturnToMenuNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal ReturnToMenuGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string> readScene,
        Func<Type, object[]> findLoadedObjects,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _readScene = readScene ?? throw new ArgumentNullException(nameof(readScene));
        _findLoadedObjects = findLoadedObjects ?? throw new ArgumentNullException(nameof(findLoadedObjects));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal ReturnToMenuSubmission Submit(in ReturnToMenuAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(ReturnToMenuActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(ReturnToMenuActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("the game's own screen", _bindingFailure));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("the game's own screen", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var scene = _readScene();
            if (!string.Equals(scene, "Main", StringComparison.Ordinal))
                return Reject(ReturnToMenuActionResultCodes.WrongScene,
                    "Back to Main Menu is available while playing; the current scene is " +
                    scene + ".");
            var flash = native.ScreenFlash();
            if (flash is null)
                return Reject(ReturnToMenuActionResultCodes.ContractUnavailable,
                    "The game's scene-transition screen is unavailable.");
            if (native.FlashActive(flash))
                return Reject(ReturnToMenuActionResultCodes.TransitionInProgress,
                    "The game is already changing screens.");
            var buttons = _findLoadedObjects(native.ButtonType) ?? Array.Empty<object>();
            var loaded = new List<object>(buttons.Length);
            for (var index = 0; index < buttons.Length; index++)
            {
                var candidate = buttons[index];
                if (candidate is not null && native.ButtonType.IsInstanceOfType(candidate))
                    loaded.Add(candidate);
            }
            var live = LiveControls(native, loaded);
            if (live.Count > 1) return Ambiguous(native, live);
            if (live.Count == 0)
            {
                // The player does not reach this control from the board: they open the panel that
                // holds it and then press it. Refusing because the panel is shut would refuse the
                // ordinary case, so the action performs the same two steps.
                if (!TryFindClosedPanel(native, loaded, out var panel, out var panelReason))
                    return Reject(ReturnToMenuActionResultCodes.ControlUnavailable, panelReason);
                if (!_tryCaptureMutationPermit())
                    return Reject(ReturnToMenuActionResultCodes.MutationPermitUnavailable,
                        _readOwnershipFailure());
                return OpenThenExecute(native, panel!, loaded, flash);
            }
            if (!_tryCaptureMutationPermit())
                return Reject(ReturnToMenuActionResultCodes.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(native, live[0], flash, string.Empty);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(ReturnToMenuActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("the game's own screen", exception));
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
    /// Opening the panel is the first change this verb makes to the game, so every outcome after it
    /// is an attempt, never a refusal a caller could retry as if nothing had happened.
    /// </summary>
    private static ReturnToMenuSubmission OpenThenExecute(
        ReturnToMenuNativeBindings native,
        object panel,
        List<object> loaded,
        object flash)
    {
        object control;
        string openedName;
        try
        {
            native.OpenPanel(panel);
            if (native.PanelModal(panel) is not { } opened || !native.PanelOpen(opened))
                return Opened(
                    "The game's panel did not open, so its Back to Main Menu control is still " +
                    "out of reach.");
            var live = LiveControls(native, loaded);
            if (live.Count > 1)
                return Opened(AmbiguousReason(native, live) + " The panel it opened is still open.");
            if (live.Count == 0)
                return Opened(
                    "The game's panel is now open and was not closed again, and it shows no " +
                    "interactable Back to Main Menu control.");
            control = live[0];
            openedName = native.ControlName(panel);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Fault(ReturnToMenuActionResultCodes.PostCommitFault,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("the game's own screen", exception));
        }
        return Execute(native, control, flash, openedName);
    }

    private static ReturnToMenuSubmission Opened(string reason) =>
        Fault(ReturnToMenuActionResultCodes.ControlUnavailable,
            NativeMutationOutcome.PostconditionFailed, reason);

    private static ReturnToMenuSubmission Execute(
        ReturnToMenuNativeBindings native,
        object button,
        object flash,
        string openedPanel)
    {
        var pressed = native.ControlName(button);
        try
        {
            native.BackToMenu(button);
            return native.FlashActive(flash)
                ? Verified(pressed, openedPanel)
                : Fault(ReturnToMenuActionResultCodes.VerificationFailed,
                    NativeMutationOutcome.PostconditionFailed,
                    "The game did not start its screen transition.");
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            if (NativeMutationVerifier.TryReadAfterCall(() => native.FlashActive(flash),
                    out var transitioning) && transitioning) return Verified(pressed, openedPanel);
            return Fault(ReturnToMenuActionResultCodes.PostCommitFault,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("the game's own screen", exception));
        }
    }

    private static List<object> LiveControls(
        ReturnToMenuNativeBindings native,
        List<object> loaded)
    {
        var live = new List<object>(loaded.Count);
        for (var index = 0; index < loaded.Count; index++)
            if (native.ControlLive(loaded[index])) live.Add(loaded[index]);
        return live;
    }

    private static ReturnToMenuSubmission Ambiguous(
        ReturnToMenuNativeBindings native,
        List<object> live) =>
        Reject(ReturnToMenuActionResultCodes.ControlUnavailable, AmbiguousReason(native, live));

    private static string AmbiguousReason(ReturnToMenuNativeBindings native, List<object> live) =>
        "The game has more than one visible, interactable Back to Main Menu control: " +
        string.Join(", ", live.ConvertAll(value => native.ControlName(value))) + ".";

    /// <summary>
    /// The one shut panel whose own button the player can press and whose content holds a Back to
    /// Main Menu control. Identity, not a label: the panel is the one that actually contains the
    /// control, so no authored caption can move it out of reach.
    /// </summary>
    private bool TryFindClosedPanel(
        ReturnToMenuNativeBindings native,
        List<object> buttons,
        out object? panel,
        out string reason)
    {
        panel = null;
        var loaded = _findLoadedObjects(native.ActivatorType) ?? Array.Empty<object>();
        var candidates = new List<object>();
        for (var index = 0; index < loaded.Length; index++)
        {
            var candidate = loaded[index];
            if (candidate is null || !native.ActivatorType.IsInstanceOfType(candidate)) continue;
            if (!native.PanelPrepared(candidate)) continue;
            if (native.PanelModal(candidate) is not { } modal) continue;
            if (native.PanelOpen(modal) || !native.PanelControlLive(candidate)) continue;
            for (var button = 0; button < buttons.Count; button++)
            {
                if (!native.PanelContains(modal, buttons[button])) continue;
                candidates.Add(candidate);
                break;
            }
        }
        if (candidates.Count == 1)
        {
            panel = candidates[0];
            reason = string.Empty;
            return true;
        }
        reason = candidates.Count == 0
            ? "The game has no visible, interactable Back to Main Menu control, and no closed " +
              "panel the player can open to reach one."
            : "More than one closed panel offers a Back to Main Menu control: " +
              string.Join(", ", candidates.ConvertAll(value => native.ControlName(value))) + ".";
        return false;
    }

    private static ReturnToMenuSubmission Reject(
        ServiceActionResultCode code,
        string reason) => new(NativeActionOutcome.Reject(code, reason));

    private static ReturnToMenuSubmission Verified(string pressedControl, string openedPanel) =>
        new(NativeActionOutcome.Commit(new NativeMutationCallOutcome(1, 1, 1),
                "The game is back at its main menu."),
            pressedControl,
            openedPanel);

    private static ReturnToMenuSubmission Fault(
        ServiceActionResultCode code,
        NativeMutationOutcome outcome,
        string reason) =>
        new(NativeActionOutcome.Fault(code, outcome, new NativeMutationCallOutcome(1, 1, 0), reason));

    private void BindLifecycle()
    {
        if (ReturnToMenuNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

}
