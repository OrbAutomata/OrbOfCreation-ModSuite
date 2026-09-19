#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellWorkbenchProjection
{
    internal static GameMcpValue ProjectStagedLayout(
        in SpellWorkbenchStagedLayout layout)
    {
        if (!layout.Available)
        {
            return new JObject
            {
                ["status"] = "unavailable",
                ["reasonCode"] = GameMcpActionResultCodeNames.Name(
                    SpellWorkbenchActionResultMapper.Code(layout.Preflight),
                    GameMcpCommandKind.SpellWorkbench),
                ["reason"] = layout.Reason,
            }.Freeze();
        }
        return new JObject
        {
            ["status"] = "available",
            ["core"] = ProjectGlyphs(layout.Core),
            ["augments"] = ProjectGlyphs(layout.Augments),
        }.Freeze();
    }

    /// <summary>What loading this spell with this layout would hold, or why it will not load.</summary>
    /// <remarks>
    /// There is no creation price, so there is nothing to be short of and no <c>affordable</c> to
    /// answer: an available preview means the game's own Loadout row is pressable for exactly
    /// these arguments. What it carries instead is the one budget a load is weighed against — the
    /// usage allocation the loaded spell holds.
    /// </remarks>
    internal static GameMcpValue ProjectLoadPreview(
        in SpellWorkbenchLoadPreview preview)
    {
        if (!preview.Available)
        {
            return new JObject
            {
                ["status"] = "refused",
                ["reasonCode"] = GameMcpActionResultCodeNames.Name(
                    SpellWorkbenchActionResultMapper.Code(preview.Preflight),
                    GameMcpCommandKind.SpellWorkbench),
                ["reason"] = preview.Reason,
            }.Freeze();
        }

        var usage = new JArray();
        for (var index = 0; index < preview.Usage.Length; index++)
        {
            var row = preview.Usage[index];
            usage.Add(new JObject
            {
                ["resourceId"] = row.ResourceId,
                ["amount"] = new GameMcpDomainValue(row.Amount),
            });
        }
        var result = new JObject { ["status"] = "available" };
        if (usage.Count > 0) result["usageAllocation"] = usage;
        return result.Freeze();
    }

    internal static GameMcpValue Project(in SpellWorkbenchSubmission submission)
    {
        if (submission.Verified || submission.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested spell workbench transition",
        }.Freeze();
    }

    private static GameMcpValue ProjectGlyphs(SpellWorkbenchGlyphStack[] glyphs)
    {
        var rows = new JArray();
        for (var index = 0; index < glyphs.Length; index++)
        {
            rows.Add(new JObject
            {
                ["glyphId"] = glyphs[index].GlyphId,
                ["count"] = glyphs[index].Count,
            });
        }
        return rows.Freeze();
    }
}
#endif
