using OrbModding.Common;

namespace OrbAutomata;

internal enum ReturnToMenuPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    WrongScene,
    TransitionInProgress,
    ControlUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum ReturnToMenuNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct ReturnToMenuSubmission
{
    internal ReturnToMenuSubmission(
        ReturnToMenuPreflight preflight,
        ReturnToMenuNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        string pressedControl = "",
        string openedPanel = "")
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        PressedControl = pressedControl ?? string.Empty;
        OpenedPanel = openedPanel ?? string.Empty;
    }

    internal ReturnToMenuPreflight Preflight { get; }
    internal ReturnToMenuNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }

    /// <summary>The native control this action pressed on the player's behalf.</summary>
    internal string PressedControl { get; }

    /// <summary>
    /// The panel this action opened to reach that control, empty when the control was already on
    /// screen. This is the one verb that operates a second control to reach its own, and the
    /// caller it operates for had no record of which one.
    /// </summary>
    internal string OpenedPanel { get; }
    internal bool Verified => Preflight == ReturnToMenuPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ReturnToMenuSubmission Reject(
        ReturnToMenuPreflight preflight,
        string reason) =>
        new(preflight, ReturnToMenuNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
