using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the native Back to Main Menu control.</summary>
internal sealed class ReturnToMenuGameAction : NativeGameAction<ReturnToMenuNativeBindings>
{
    private const string Screen = "the game's own screen";
    private const string Control = "Back to Main Menu";

    private static readonly GameActionCodes Codes = new(
        ReturnToMenuActionResultCodes.WrongThread,
        ReturnToMenuActionResultCodes.ContractUnavailable,
        ReturnToMenuActionResultCodes.MutationPermitUnavailable,
        ReturnToMenuActionResultCodes.PostCommitFault,
        ReturnToMenuActionResultCodes.VerificationFailed);

    private readonly Func<string> _readScene;
    private readonly Func<Type, object[]> _findLoadedObjects;

    internal ReturnToMenuGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string> readScene,
        Func<Type, object[]> findLoadedObjects,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base("Return to menu", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out ReturnToMenuNativeBindings? bindings, out string reason) =>
                ReturnToMenuNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _readScene = readScene ?? throw new ArgumentNullException(nameof(readScene));
        _findLoadedObjects = findLoadedObjects ?? throw new ArgumentNullException(nameof(findLoadedObjects));
    }

    internal ReturnToMenuSubmission Submit(in ReturnToMenuAction action) =>
        Admit(action.LifecycleEpoch, static outcome => new ReturnToMenuSubmission(outcome), Press);

    private ReturnToMenuSubmission Press(ReturnToMenuNativeBindings native)
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
        if (live.Count == 1)
        {
            if (!TryPermit(out var refusal)) return new ReturnToMenuSubmission(refusal);
            var button = live[0];
            return Run(native, flash, () =>
            {
                native.BackToMenu(button);
                return null;
            });
        }
        // The player does not reach this control from the board: they open the panel that
        // holds it and then press it. Refusing because the panel is shut would refuse the
        // ordinary case, so the action performs the same two steps.
        if (!TryFindClosedPanel(native, loaded, out var panel, out var panelReason))
            return Reject(ReturnToMenuActionResultCodes.ControlUnavailable, panelReason);
        if (!TryPermit(out var panelRefusal)) return new ReturnToMenuSubmission(panelRefusal);
        return Run(native, flash, () => OpenThenPress(native, panel!, loaded));
    }

    /// <summary>
    /// Opening the panel is the first change this verb makes to the game, so every outcome after it
    /// is an attempt, never a refusal a caller could retry as if nothing had happened. Nothing
    /// closes the panel again, so a stop here is an attempted fault.
    /// </summary>
    private static NativeStop? OpenThenPress(
        ReturnToMenuNativeBindings native,
        object panel,
        List<object> loaded)
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
        native.BackToMenu(live[0]);
        return null;
    }

    private static NativeStop Opened(string reason) =>
        NativeStop.Fault(ReturnToMenuActionResultCodes.ControlUnavailable, reason);

    private ReturnToMenuSubmission Run(
        ReturnToMenuNativeBindings native,
        object flash,
        Func<NativeStop?> call) =>
        new(Execute(Control, "The game is back at its main menu.", false,
            () => native.FlashActive(flash), call, static (_, transitioning) => transitioning));

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
}
