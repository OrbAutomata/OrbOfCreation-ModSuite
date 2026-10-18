using System;
using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct GenericLevelSubmission
{
    private readonly ResourceCharge[]? _charges;

    internal GenericLevelSubmission(
        NativeActionOutcome outcome,
        int levelsCharged = 0,
        ResourceCharge[]? charges = null)
    {
        Outcome = outcome;
        LevelsCharged = levelsCharged;
        _charges = charges;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// How many levels this press bought, and what the game asked for them.
    /// </summary>
    /// <remarks>
    /// A ladder prices every rung separately, so the first rung's price answers for the whole ask
    /// only when the ask is one. A round bought five rune levels, read <c>free: yes</c> off the
    /// ladder's free first rung, and watched Time Advancements fall anyway. These are the prices
    /// the game itself named immediately before each payment, on the frame that made it — not a
    /// balance comparison, and not evidence: the postcondition is still the one level sentinel.
    /// </remarks>
    internal int LevelsCharged { get; }
    internal ReadOnlySpan<ResourceCharge> Charges =>
        _charges is null ? default : _charges.AsSpan(0, _charges.Length);

    /// <summary>Whether every level this press bought asked for nothing.</summary>
    internal bool ChargedNothing => ResourceCharges.Nothing(Charges);
}
