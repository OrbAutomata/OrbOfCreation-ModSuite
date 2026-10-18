using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct ReturnToMenuSubmission
{
    internal ReturnToMenuSubmission(
        NativeActionOutcome outcome,
        string pressedControl = "",
        string openedPanel = "")
    {
        Outcome = outcome;
        PressedControl = pressedControl ?? string.Empty;
        OpenedPanel = openedPanel ?? string.Empty;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>The native control this action pressed on the player's behalf.</summary>
    internal string PressedControl { get; }

    /// <summary>
    /// The panel this action opened to reach that control, empty when the control was already on
    /// screen. This is the one verb that operates a second control to reach its own, and the
    /// caller it operates for had no record of which one.
    /// </summary>
    internal string OpenedPanel { get; }
}
