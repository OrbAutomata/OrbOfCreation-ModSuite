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

    internal static GameMcpValue ProjectPricePreview(
        in SpellWorkbenchPricePreview preview)
    {
        if (!preview.Available)
        {
            return new JObject
            {
                ["status"] = "unavailable",
                ["reasonCode"] = GameMcpActionResultCodeNames.Name(
                    SpellWorkbenchActionResultMapper.Code(preview.Preflight),
                    GameMcpCommandKind.SpellWorkbench),
                ["reason"] = preview.Reason,
            }.Freeze();
        }

        var costs = new JArray();
        for (var index = 0; index < preview.Costs.Length; index++)
        {
            var cost = preview.Costs[index];
            costs.Add(new JObject
            {
                ["resourceId"] = cost.ResourceId,
                ["cost"] = new GameMcpDomainValue(cost.Cost),
            });
        }
        // What the live layout resolves to is the answer's first fact, because it is the one the
        // price cannot carry. The recipe uuid is the caller's own argument read back; the spell the
        // game reads out of the staged glyphs is a live fact, and it is what an add will act on.
        var result = new JObject
        {
            ["status"] = "available",
            ["resolvesTo"] = preview.ResolvedRecipeId,
            ["costs"] = costs,
        };

        // `affordable` answers whether a price can be paid, so a layout with no price does not
        // carry it. An empty augment layout prices an empty cost list, and the bare `affordable:
        // yes` that produced read as "this add will work" on a call that then refused.
        if (preview.Costs.Length > 0)
        {
            result["affordable"] = preview.Affordable;
            if (!preview.Affordable) result["shortResourceId"] = preview.ShortResourceId;
        }
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
