using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// The last failure a surface reported, by what it failed on, its authored sentence and its code,
/// so a failure that keeps repeating is reported once until it changes or is cleared.
/// </summary>
/// <remarks>
/// It compares the sentence, not the whole reason, because each attempt logs its cause under a
/// fresh reference: a comparison of the whole reason never sees a repeat.
/// </remarks>
internal sealed class RepeatedFailure
{
    private bool _latched;
    private ServiceActionResultCode? _code;
    private string _subject = string.Empty;
    private string _sentence = string.Empty;

    /// <summary>
    /// Whether <paramref name="outcome"/> on <paramref name="subject"/> says what was last reported.
    /// When it does not, it becomes what was last reported.
    /// </summary>
    internal bool IsRepeat(in NativeActionOutcome outcome, string subject = "") =>
        IsRepeat(outcome.Sentence, outcome.Code, subject);

    /// <summary>Compares a private exception cause before its log entry is written.</summary>
    internal bool IsRepeat(Exception exception, string subject = "") =>
        IsRepeat(ExceptionDetail.Describe(exception), subject: subject);

    /// <summary>The same, for a failure with no result code, such as a probe of the game.</summary>
    internal bool IsRepeat(string sentence, ServiceActionResultCode? code = null, string subject = "")
    {
        if (_latched &&
            _code == code &&
            string.Equals(_subject, subject, StringComparison.Ordinal) &&
            string.Equals(_sentence, sentence, StringComparison.Ordinal))
            return true;
        _latched = true;
        _code = code;
        _subject = subject;
        _sentence = sentence;
        return false;
    }

    internal void Clear()
    {
        _latched = false;
        _code = null;
        _subject = string.Empty;
        _sentence = string.Empty;
    }
}
