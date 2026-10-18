using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct ReturnToMenuSubmission
{
    internal ReturnToMenuSubmission(NativeActionOutcome outcome)
    {
        Outcome = outcome;
    }

    internal NativeActionOutcome Outcome { get; }
}
