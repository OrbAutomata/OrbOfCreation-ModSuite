using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct SpellWorkbenchStagedLayout
{
    private SpellWorkbenchStagedLayout(
        ServiceActionResultCode code,
        SpellWorkbenchGlyphStack[] core,
        SpellWorkbenchGlyphStack[] augments,
        string reason)
    {
        Code = code;
        Core = Copy(core);
        Augments = Copy(augments);
        Reason = reason ?? string.Empty;
    }

    internal ServiceActionResultCode Code { get; }
    internal SpellWorkbenchGlyphStack[] Core { get; }
    internal SpellWorkbenchGlyphStack[] Augments { get; }
    internal string Reason { get; }
    internal bool Available => Code == CommonActionResultCodes.Committed;

    internal static SpellWorkbenchStagedLayout Captured(
        SpellWorkbenchGlyphStack[] core,
        SpellWorkbenchGlyphStack[] augments) =>
        new(CommonActionResultCodes.Committed, core, augments, string.Empty);

    internal static SpellWorkbenchStagedLayout Unavailable(
        ServiceActionResultCode code,
        GameActionReason reason) =>
        new(code, Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>(), reason.Text);

    private static SpellWorkbenchGlyphStack[] Copy(SpellWorkbenchGlyphStack[] source)
    {
        if (source.Length == 0) return Array.Empty<SpellWorkbenchGlyphStack>();
        var result = new SpellWorkbenchGlyphStack[source.Length];
        Array.Copy(source, result, source.Length);
        return result;
    }
}
