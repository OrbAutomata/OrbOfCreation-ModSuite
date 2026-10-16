using System;

namespace OrbAutomata;

internal readonly struct SpellWorkbenchStagedLayout
{
    private SpellWorkbenchStagedLayout(
        SpellWorkbenchPreflight preflight,
        SpellWorkbenchGlyphStack[] core,
        SpellWorkbenchGlyphStack[] augments,
        string reason)
    {
        Preflight = preflight;
        Core = Copy(core);
        Augments = Copy(augments);
        Reason = reason ?? string.Empty;
    }

    internal SpellWorkbenchPreflight Preflight { get; }
    internal SpellWorkbenchGlyphStack[] Core { get; }
    internal SpellWorkbenchGlyphStack[] Augments { get; }
    internal string Reason { get; }
    internal bool Available => Preflight == SpellWorkbenchPreflight.Proceeded;

    internal static SpellWorkbenchStagedLayout Captured(
        SpellWorkbenchGlyphStack[] core,
        SpellWorkbenchGlyphStack[] augments) =>
        new(SpellWorkbenchPreflight.Proceeded, core, augments, string.Empty);

    internal static SpellWorkbenchStagedLayout Unavailable(
        SpellWorkbenchPreflight preflight,
        string reason) =>
        new(preflight, Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>(), reason);

    private static SpellWorkbenchGlyphStack[] Copy(SpellWorkbenchGlyphStack[] source)
    {
        if (source.Length == 0) return Array.Empty<SpellWorkbenchGlyphStack>();
        var result = new SpellWorkbenchGlyphStack[source.Length];
        Array.Copy(source, result, source.Length);
        return result;
    }
}
