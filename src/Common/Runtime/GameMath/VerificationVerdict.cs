namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// The only four things the game math check ever says about anything it compared.
/// </summary>
/// <remarks>
/// <para>
/// One vocabulary, top to bottom. The check used to answer in two — an upper-case
/// <c>PASSED</c>/<c>FAILED</c> grammar for the ported passes and a prose "all agree" grammar for the
/// collection checks — so a reader tallying verdicts got a different total depending on which words
/// they counted. Two vocabularies in one response is a tally hazard, not a style preference.
/// </para>
/// <para>
/// <c>Agree</c> and <c>Disagree</c> rather than pass and fail because that is the question the verb
/// asks: whether the suite's arithmetic and the game's arithmetic produce the same number. A pass
/// implies a standard the check does not own.
/// </para>
/// <para>
/// The other two are the honest answers to "we could not tell", kept apart because they need
/// different work. <c>Incomplete</c> compared something and agreed on all of it, but could not reach
/// everything — coverage is short. <c>Inconclusive</c> compared nothing at all, so there is no
/// verdict to have, and reporting it as agreement would be the one bug that makes the whole check
/// worthless.
/// </para>
/// </remarks>
internal enum VerificationVerdict
{
    /// <summary>Everything compared agreed, and everything in scope was compared.</summary>
    Agree = 0,

    /// <summary>Everything compared agreed, but something in scope could not be read.</summary>
    Incomplete = 1,

    /// <summary>Nothing could be compared, so there is no verdict about the game.</summary>
    Inconclusive = 2,

    /// <summary>At least one comparison found the two sides genuinely different.</summary>
    Disagree = 3,
}

internal static class VerificationVerdictWords
{
    /// <summary>The one word this verdict is written with, wherever it is written.</summary>
    internal static string Word(this VerificationVerdict verdict) => verdict switch
    {
        VerificationVerdict.Agree => "AGREE",
        VerificationVerdict.Incomplete => "INCOMPLETE",
        VerificationVerdict.Inconclusive => "INCONCLUSIVE",
        _ => "DISAGREE",
    };
}
