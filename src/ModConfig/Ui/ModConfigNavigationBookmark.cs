using System;
using System.Linq;

namespace OrbModConfig;

internal readonly record struct ModConfigNavigationBookmark(
    string PluginGuid,
    string SectionName,
    float ScrollOffset,
    ModConfigTopPageKind Kind = ModConfigTopPageKind.PluginSettings,
    float RailScrollOffset = 0f)
{
    public bool IsRuntime => Kind == ModConfigTopPageKind.Runtime ||
        Kind == ModConfigTopPageKind.PluginSettings && string.IsNullOrEmpty(PluginGuid);
    public bool IsStrategist => Kind == ModConfigTopPageKind.Strategist;

    public static ModConfigNavigationBookmark Runtime => new(string.Empty, string.Empty, 0f, ModConfigTopPageKind.Runtime);
    public static ModConfigNavigationBookmark Strategist =>
        new(string.Empty, string.Empty, 0f, ModConfigTopPageKind.Strategist);
}

internal static class ModConfigNavigationBookmarkPolicy
{
    public static int ResolveTopPageIndex(
        ConfigCatalogSnapshot catalog,
        ModConfigNavigationBookmark bookmark)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (bookmark.IsRuntime) return 0;
        var pages = ModConfigTopNavigation.Build(catalog, attentionCount: 0);
        for (var index = 1; index < pages.Count; index++)
        {
            var page = pages[index];
            if (bookmark.IsStrategist && page.Kind == ModConfigTopPageKind.Strategist) return index;
            if (page.Kind != ModConfigTopPageKind.PluginSettings || bookmark.IsStrategist) continue;
            var mod = catalog.Mods[page.PluginIndex];
            if (string.Equals(mod.Guid, bookmark.PluginGuid, StringComparison.Ordinal) &&
                (page.SectionIndex < 0 && string.IsNullOrEmpty(bookmark.SectionName) ||
                 page.SectionIndex >= 0 && string.Equals(
                     mod.Sections[page.SectionIndex].Name,
                     bookmark.SectionName,
                     StringComparison.Ordinal)))
                return index;
        }
        return 0;
    }

    public static int ResolveSectionIndex(
        ModConfigDescriptor mod,
        ModConfigNavigationBookmark bookmark)
    {
        if (mod is null) throw new ArgumentNullException(nameof(mod));
        var sectionIndex = Array.FindIndex(
            mod.Sections.ToArray(),
            section => string.Equals(section.Name, bookmark.SectionName, StringComparison.Ordinal));
        return Math.Max(0, sectionIndex);
    }
}
