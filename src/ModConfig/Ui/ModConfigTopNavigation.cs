using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;

namespace OrbModConfig;

internal enum ModConfigTopPageKind
{
    Runtime = 0,
    PluginSettings = 1,
    Strategist = 2,
}

internal readonly struct ModConfigTopPage
{
    private ModConfigTopPage(
        ModConfigTopPageKind kind,
        string label,
        int pluginIndex,
        int sectionIndex,
        NativeFeatureIcon icon)
    {
        Kind = kind;
        Label = label;
        PluginIndex = pluginIndex;
        SectionIndex = sectionIndex;
        Icon = icon;
    }

    public ModConfigTopPageKind Kind { get; }
    public string Label { get; }
    public int PluginIndex { get; }
    public int SectionIndex { get; }
    public NativeFeatureIcon Icon { get; }

    public static ModConfigTopPage Runtime(int attentionCount) => new(
        ModConfigTopPageKind.Runtime,
        attentionCount > 0 ? $"Runtime ({attentionCount})" : "Runtime",
        pluginIndex: -1,
        sectionIndex: -1,
        NativeFeatureIcon.Runtime);

    public static ModConfigTopPage Strategist => new(
        ModConfigTopPageKind.Strategist, "Strategist", -1, -1, NativeFeatureIcon.Strategist);

    public static ModConfigTopPage Plugin(string label, int pluginIndex, int sectionIndex,
        NativeFeatureIcon icon = NativeFeatureIcon.Settings)
    {
        if (pluginIndex < 0) throw new ArgumentOutOfRangeException(nameof(pluginIndex));
        if (sectionIndex < -1) throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A plugin page label is required.", nameof(label));
        return new ModConfigTopPage(
            ModConfigTopPageKind.PluginSettings,
            label,
            pluginIndex,
            sectionIndex,
            icon);
    }
}

internal static class ModConfigTopNavigation
{
    public static string DetailTitle(ConfigCatalogSnapshot catalog, ModConfigTopPage page)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (page.Kind == ModConfigTopPageKind.Runtime) return "Orb Of Creation ModSuite · Runtime";
        if (page.Kind == ModConfigTopPageKind.Strategist) return "Orb Of Creation ModSuite · Strategist";
        var mod = catalog.Mods[page.PluginIndex];
        return page.SectionIndex < 0
            ? mod.Name
            : mod.Name + " · " + mod.Sections[page.SectionIndex].Name;
    }

    public static IReadOnlyList<ModConfigTopPage> Build(ConfigCatalogSnapshot catalog, int attentionCount)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (attentionCount < 0) throw new ArgumentOutOfRangeException(nameof(attentionCount));
        var pages = new List<ModConfigTopPage> { ModConfigTopPage.Runtime(attentionCount), ModConfigTopPage.Strategist };
        for (var index = 0; index < catalog.Mods.Count; index++)
        {
            var mod = catalog.Mods[index];
            if (mod.Sections.Count == 0)
            {
                pages.Add(ModConfigTopPage.Plugin(mod.Name, index, sectionIndex: -1));
                continue;
            }
            for (var sectionIndex = 0; sectionIndex < mod.Sections.Count; sectionIndex++)
            {
                var label = catalog.Mods.Count == 1
                    ? mod.Sections[sectionIndex].Name
                    : mod.Name + " · " + mod.Sections[sectionIndex].Name;
                var icon = string.Equals(mod.Guid, PluginIds.SuiteGuid, StringComparison.Ordinal)
                    ? NativeFeatureIconResolver.ForFeature(mod.Sections[sectionIndex].Name)
                    : NativeFeatureIcon.Settings;
                pages.Add(ModConfigTopPage.Plugin(label, index, sectionIndex, icon));
            }
        }
        return pages;
    }
}
