using System;

namespace OrbModding.Common;

/// <summary>
/// The player's route to a screen, for every view an answer of the suite's can be blocked on.
/// </summary>
/// <remarks>
/// <para>
/// "The screen this action lives on is not unlocked yet" is a true sentence a reader cannot act
/// on: a live round met it on a ritual, on an artifact and on a spell's own loadout row, and each
/// time had to go and work out which page that was. Every producer of that answer already holds
/// the view's identity, so the door has a name at every one of them; this is where the name lives,
/// so the read side and the verb cannot spell one screen two ways.
/// </para>
/// <para>
/// The breadcrumbs are the game's own tab labels, joined the way the game's own headers read them.
/// <c>docs/game-systems/screens.md</c> is coarser on purpose — it maps a page to the doc that owns
/// its mechanic — so the leaf pages here are finer than its rows, not in conflict with them.
/// </para>
/// <para>
/// An unpinned view answers with an empty string rather than a plausible path. A guessed route is
/// worse than no route: the reader would go there and find nothing, and have no way to tell which
/// half was wrong.
/// </para>
/// </remarks>
internal static class GameScreenPath
{
    internal static string For(Guid viewId)
    {
        if (viewId == KnownEntities.MagicSpellbookLearn.Uuid) return "Magic > Spellbook > Unlock";
        if (viewId == KnownEntities.MagicSpellbookLoadout.Uuid) return "Magic > Spellbook > Loadout";
        if (viewId == KnownEntities.MagicGlyphsDiscover.Uuid) return "Magic > Augments > Glyphcraft";
        if (viewId == KnownEntities.MagicGlyphsUpgrade.Uuid) return "Magic > Augments > Upgrade";
        if (viewId == KnownEntities.RitualsDiscover.Uuid) return "Rituals > Discover";
        if (viewId == KnownEntities.WorkshopArtifactCreate.Uuid) return "Workshop > Artifacts > Create";
        if (viewId == KnownEntities.TimeTimeRuneCreate.Uuid) return "Time > Time Runes > Create";
        if (viewId == KnownEntities.AlchAlchemyDiscover.Uuid) return "Alchemy > Alchemy > Learn";
        if (viewId == KnownEntities.ScholarConceptDiscover.Uuid) return "Scholar > Concepts > Discover";
        return string.Empty;
    }
}
