using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

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
            return Reject(ReturnToMenuPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(ReturnToMenuPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("the game's own screen"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(ReturnToMenuPreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("the game's own screen", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(ReturnToMenuPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var scene = _readScene();
            if (!string.Equals(scene, "Main", StringComparison.Ordinal))
                return Reject(ReturnToMenuPreflight.WrongScene,
                    "Back to Main Menu is available while playing; the current scene is " +
                    scene + ".");
            var flash = native.ScreenFlash();
            if (flash is null)
                return Reject(ReturnToMenuPreflight.ContractUnavailable,
                    "The game's scene-transition screen is unavailable.");
            if (native.FlashActive(flash))
                return Reject(ReturnToMenuPreflight.TransitionInProgress,
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
                    return Reject(ReturnToMenuPreflight.ControlUnavailable, panelReason);
                if (!_tryCaptureMutationPermit())
                    return Reject(ReturnToMenuPreflight.MutationPermitUnavailable,
                        _readOwnershipFailure());
                native.OpenPanel(panel!);
                if (native.PanelModal(panel!) is not { } opened || !native.PanelOpen(opened))
                {
                    return Reject(ReturnToMenuPreflight.ControlUnavailable,
                        "The game's panel did not open, so its Back to Main Menu control is still " +
                        "out of reach.");
                }
                live = LiveControls(native, loaded);
                if (live.Count > 1) return Ambiguous(native, live);
                if (live.Count == 0)
                {
                    return Reject(ReturnToMenuPreflight.ControlUnavailable,
                        "The game's panel is now open and was not closed again, and it shows no " +
                        "interactable Back to Main Menu control.");
                }
                return Execute(native, live[0], flash, native.ControlName(panel!));
            }
            if (!_tryCaptureMutationPermit())
                return Reject(ReturnToMenuPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(native, live[0], flash, string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(ReturnToMenuPreflight.ContractUnavailable,
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

    private static ReturnToMenuSubmission Execute(
        ReturnToMenuNativeBindings native,
        object button,
        object flash,
        string openedPanel)
    {
        var stage = ReturnToMenuNativeStage.NativeCallback;
        var pressed = native.ControlName(button);
        try
        {
            native.BackToMenu(button);
            stage = ReturnToMenuNativeStage.Verification;
            return native.FlashActive(flash)
                ? Verified(pressed, openedPanel)
                : Fault(ReturnToMenuPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The game did not start its screen transition.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (native.FlashActive(flash)) return Verified(pressed, openedPanel);
            return Fault(ReturnToMenuPreflight.PostCommitFault, stage,
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
        Reject(ReturnToMenuPreflight.ControlUnavailable,
            "The game has more than one visible, interactable Back to Main Menu control: " +
            string.Join(", ", live.ConvertAll(value => native.ControlName(value))) + ".");

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
        ReturnToMenuPreflight preflight,
        string reason) => ReturnToMenuSubmission.Reject(preflight, reason);

    private static ReturnToMenuSubmission Verified(string pressedControl, string openedPanel) =>
        new(ReturnToMenuPreflight.Proceeded, ReturnToMenuNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The game is back at its main menu.",
            pressedControl,
            openedPanel);

    private static ReturnToMenuSubmission Fault(
        ReturnToMenuPreflight preflight,
        ReturnToMenuNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0), reason);

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

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}
