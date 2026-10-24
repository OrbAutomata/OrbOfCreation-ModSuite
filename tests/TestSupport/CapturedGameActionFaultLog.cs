using System;
using System.Collections.Generic;
using OrbModding.Common;
using Xunit;

namespace OrbModding.TestSupport;

/// <summary>Checks a public reason against the complete exception recorded behind its reference.</summary>
internal sealed class CapturedGameActionFaultLog : IDisposable
{
    private readonly List<string> _entries = new();

    internal CapturedGameActionFaultLog() => GameActionFaultLog.ConfigureLog(_entries.Add);

    internal void AssertLogged(GameActionReason reason, string nativeDetail)
    {
        Assert.NotEmpty(reason.Reference);
        Assert.DoesNotContain(nativeDetail, reason.Text, StringComparison.Ordinal);
        var entry = Assert.Single(_entries, line => line.Contains(reason.Reference, StringComparison.Ordinal));
        Assert.Contains(nativeDetail, entry, StringComparison.Ordinal);
        Assert.Contains("Exception", entry, StringComparison.Ordinal);
    }

    internal void AssertLogged(GameActionReason reason, Exception nativeFault)
    {
        AssertLogged(reason, nativeFault.ToString());
        Assert.DoesNotContain(nativeFault.Message, reason.Text, StringComparison.Ordinal);
    }

    public void Dispose() => GameActionFaultLog.ConfigureLog(null);
}
