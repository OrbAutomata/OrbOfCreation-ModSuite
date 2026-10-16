using System;
using System.Diagnostics;
using System.Text;

namespace OrbModding.Common;

/// <summary>
/// One bounded log line naming what threw: the base exception's type, its message, and the top
/// frames of its own stack. A player log is often the only evidence a fault ever leaves, and a
/// message alone ("Object reference not set to an instance of an object") cannot say where.
/// </summary>
public static class ExceptionDetail
{
    public const int MaxFrames = 4;
    public const int MaxLength = 600;
    private const int MaxMessageLength = 200;

    public static string Describe(Exception exception)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        var thrower = exception.GetBaseException();
        var line = new StringBuilder(MaxLength);
        line.Append(thrower.GetType().FullName).Append(": ");
        AppendSingleLine(line, thrower.Message, MaxMessageLength);
        AppendFrames(line, thrower);
        if (line.Length > MaxLength)
        {
            line.Length = MaxLength - 1;
            line.Append('…');
        }
        return line.ToString();
    }

    private static void AppendFrames(StringBuilder line, Exception thrower)
    {
        StackFrame[]? frames;
        try { frames = new StackTrace(thrower, false).GetFrames(); }
        catch { frames = null; }
        if (frames is null || frames.Length == 0)
        {
            line.Append(" at <no frames>");
            return;
        }
        var written = 0;
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null) continue;
            line.Append(written == 0 ? " at " : " < ");
            if (method.DeclaringType is { } owner) line.Append(owner.FullName).Append('.');
            line.Append(method.Name);
            if (++written == MaxFrames) break;
        }
        if (written == 0) line.Append(" at <no frames>");
    }

    private static void AppendSingleLine(StringBuilder line, string text, int limit)
    {
        var length = Math.Min(text.Length, limit);
        for (var index = 0; index < length; index++)
        {
            var character = text[index];
            line.Append(character is '\r' or '\n' ? ' ' : character);
        }
        if (text.Length > limit) line.Append('…');
    }
}
