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
        // The recipe is the one argument this preview takes, so naming it back is the caller's own
        // input read aloud; the answer is the price and whether it can be paid.
        var result = new JObject
        {
            ["status"] = "available",
            ["costs"] = costs,
            ["affordable"] = preview.Affordable,
        };
        if (!preview.Affordable) result["shortResourceId"] = preview.ShortResourceId;
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
