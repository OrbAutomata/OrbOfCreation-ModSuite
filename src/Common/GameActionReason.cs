namespace OrbModding.Common;

/// <summary>
/// A GameAction's authored sentence and the suite-log reference behind it, kept apart.
/// </summary>
/// <remarks>
/// Every recorded cause gets a fresh reference, so two attempts that failed for the same reason
/// never read alike in full. Anything asking whether a failure repeated compares
/// <see cref="Sentence"/>; everything a person reads gets <see cref="Text"/>, the sentence followed by
/// the reference. A plain sentence converts to a reason with no reference.
/// </remarks>
public readonly struct GameActionReason
{
    private readonly string? _sentence;
    private readonly string? _reference;

    public GameActionReason(string sentence, string reference = "")
    {
        _sentence = sentence;
        _reference = reference;
    }

    public string Sentence => _sentence ?? string.Empty;
    public string Reference => _reference ?? string.Empty;
    public string Text => Sentence + GameActionFaultLog.Sentence(Reference);

    /// <summary>The same reason inside a longer sentence, still under its own reference.</summary>
    public GameActionReason Around(string before, string after = "") =>
        new(before + Sentence + after, Reference);

    public static implicit operator GameActionReason(string sentence) => new(sentence);
}
