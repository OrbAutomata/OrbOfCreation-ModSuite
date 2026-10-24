#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpGenericLevelProjection;
using static OrbAutomata.GameMcp.GameMcpScreenState;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGlyphProjection
{

    /// <summary>
    /// Whether the glyph picker offers this glyph. <c>UIGlyphListItem.IsVisible()</c> is
    /// <c>GlyphSO.IsAvailable()</c>, which the binder already reads as <c>Learned</c>, and
    /// <c>GlyphSO.IsVisible()</c> is the same method again. A glyph has no ceiling —
    /// <c>CanLevel()</c> is the constant <c>true</c> — so two words are its whole lifecycle.
    /// </summary>
    internal static string GlyphState(in WorldGlyph glyph) =>
        glyph.Learned ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectGlyph(GameWorldState world, in WorldGlyph glyph)
    {
        var result = new JObject
        {
            ["entityId"] = glyph.EntityId.ToString("D"),
            ["category"] = "augment-glyphs",
            ["state"] = GlyphState(in glyph),
            // The game's own two words for what a level buys, off its own level-panel nodes:
            // GetMaxUsages() prints as `[N] Slot` and GetFreeUsages() as `[M] Free Slot`. The old
            // name `usableCount` was the suite's, and it named neither the screen's word nor the
            // thing a caller spends them on.
            ["slots"] = glyph.MaximumUsages,
            ["freeSlots"] = glyph.MaximumFreeUsages,
        };

        // One reason is left. GlyphSO.IsAvailable() returns `discovered` for a discoverable glyph,
        // and every glyph the world publishes is one, so an Augment Glyph the grid does not offer
        // is waiting on its own discovery and on nothing else.
        if (!glyph.Learned) result["reasonCode"] = "undiscovered";
        AddGlyphFactors(world, result, glyph.EntityId);

        // The level offer exists only where the game draws the button. Magic > Augments > Upgrade
        // is gated on the GlyphUpgradesUnlocked link, which the Upgrade Glyphs upgrade satisfies,
        // and while that screen is locked the game instantiates no level panel and there is no
        // press to offer — whatever GlyphSO.CanLevel() says, which is the constant `true`.
        var upgradeScreen = IsScreenUnlocked(world, KnownEntities.MagicGlyphsUpgrade.Uuid);
        AddLevelDecision(world, result, glyph.LevelDecision,
            glyph.Learned && upgradeScreen,
            upgradeScreen ? "not_available" : "screen_locked",
            upgradeScreen
                ? string.Empty
                : "Magic > Augments > Upgrade is not unlocked yet, so the game draws no level " +
                  "button for an augment glyph. Buy the Upgrade Glyphs upgrade first.",
            upgradeScreen);
        AddDiscoveryDecision(
            world,
            result,
            glyph.Discovery,
            KnownEntities.MagicGlyphsDiscover.Uuid,
            glyph.EntityId,
            glyph.Discoverable);
        return result.Freeze();
    }

    /// <summary>What the glyph does, on the glyph's own answer.</summary>
    /// <remarks>
    /// <para>
    /// The node names its edge by carrying it. A glyph fills between one and five of fifteen slots,
    /// so its whole factor set is smaller than the sentence that would point at it, and the read
    /// that asks "should I socket this" is the read that needs it — the round that found this gap
    /// had a glyph's row priced and levelled with nothing on it about what the glyph does. Nothing
    /// is unfurled: a factor has no identity of its own, and the statistic it names stays a
    /// reference for <c>world_get</c> to follow rather than a block copied in here.
    /// </para>
    /// <para>
    /// This is the only place the factors reach the wire. They were a listable table as well and the
    /// maintainer retired it: an extra table for one very specific concept, beside generic reads
    /// that answer for every concept in the world, is a table to remove once the concept has moved
    /// onto them. The cross-glyph question it existed for — which glyphs move Cooldown — is a
    /// <c>world_search</c> query now, because search matches the words these rows carry.
    /// </para>
    /// </remarks>
    /// <summary>
    /// What a glyph does, one authored slot per row.
    /// </summary>
    /// <remarks>
    /// <c>GlyphSO.GetQuantityTooltipNodes</c> prints every non-empty slot through
    /// <c>AttributeSO.CreateNamedNode</c> on the attribute its <c>GlobalVariables.Get…Attr()</c>
    /// accessor hands back, so the word the player reads beside a magnitude is that entity's own
    /// name — the entity this row already points at. Four slots are printed against a player
    /// variable instead and point at that, the same way. The fifteenth is applied straight to a
    /// <c>ResourceCostList</c>, which is not an entity, so it is the only row that says what it
    /// moves rather than naming it.
    /// </remarks>
    internal static void AddGlyphFactors(GameWorldState world, JObject result, Guid glyphId)
    {
        if (!WorldGlyphFactorLookup.TryFindRange(world.GlyphEffects, glyphId, out var start,
                out var count))
        {
            return;
        }

        var factors = new JArray();
        for (var index = 0; index < count; index++)
        {
            var factor = world.GlyphEffects[start + index];
            var row = new JObject
            {
                ["statisticId"] = factor.StatisticId.ToString("D"),

                // The four slots the game prints against a player variable instead of a statistic.
                // They used to carry a blank where an edge belonged, which told a reader the factor
                // moved something the game would not name — and the game does name it, through the
                // accessor its own tooltip calls.
                ["variableId"] = factor.VariableId.ToString("D"),
                ["amount"] = GameMcpNativeVocabulary.ModifierMagnitude(
                    factor.ModifierType, factor.Amount),
                ["order"] = factor.Order,
            };
            // No `property`. It was the slot's field name off `GlyphSO` — a row read `spellPower`
            // while the statistic it points at, and the tooltip that prints it, both read `Power`.
            // The edge already carries the game's own word, so the field name was a second, worse
            // spelling of a fact the row states. The one slot with neither edge says what it is.
            if (factor.StatisticId == Guid.Empty && factor.VariableId == Guid.Empty)
                row["affects"] = "what the spell costs to create";
            factors.Add(row);
        }

        result["effects"] = factors;
    }
    internal static GameMcpValue List(
        GameWorldState world,
        in WorldGlyph glyph,
        int nestedOffset,
        int nestedLimit)
    {
        var projectedGlyph = new JObject
        {
            ["entityId"] = glyph.EntityId.ToString("D"),
            ["state"] = GlyphState(in glyph),
            ["slots"] = glyph.MaximumUsages,
            ["freeSlots"] = glyph.MaximumFreeUsages,
        };
        var drawsLevel = IsScreenUnlocked(world, KnownEntities.MagicGlyphsUpgrade.Uuid);
        projectedGlyph["paidLevel"] = drawsLevel
            ? glyph.LevelDecision.TotalLevel - glyph.LevelDecision.BonusLevels
            : (int?)null;
        projectedGlyph["bonusLevel"] = drawsLevel
            ? glyph.LevelDecision.BonusLevels
            : (int?)null;
        projectedGlyph["totalLevel"] = drawsLevel
            ? glyph.LevelDecision.TotalLevel
            : (int?)null;
        return projectedGlyph.Freeze();
    }

}
#endif
