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



namespace OrbAutomata.GameMcp;

internal static class GameMcpScreenState
{

    /// <summary>
    /// Which screen's upgrade panel shows this row, from the authored list it is a member of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The panel a player opens is one <c>UIUpgradeList</c> whose contents swap with the active
    /// screen, so membership in an authored list <em>is</em> the screen fact. Every upgrade is on
    /// the catch-all list, which is what makes the catch-all worthless as an answer wherever a
    /// screen list also carries the row — and exactly right where none does.
    /// </para>
    /// <para>
    /// Three things fail closed to <see cref="GameMcpListColumns.Unreadable"/> rather than to a
    /// plausible word: a withheld membership publication, a row the publication does not mention,
    /// and a row two screen panels both claim. The last is impossible on the pinned build — the
    /// eight screen lists are disjoint — which is the point: if it ever stops being impossible, the
    /// column says so instead of picking a winner.
    /// </para>
    /// </remarks>
    internal static string UpgradeScreen(GameWorldState world, Guid upgradeId)
    {
        if (!WorldUpgradeListMembershipLookup.TryFindRange(
                world.UpgradeListMemberships, upgradeId, out var start, out var count))
        {
            return GameMcpListColumns.Unreadable;
        }

        var screen = string.Empty;
        var everyUpgradeList = false;
        for (var index = 0; index < count; index++)
        {
            var listId = world.UpgradeListMemberships[start + index].ListId;
            if (listId == GameMcpListColumns.EveryUpgradeList)
            {
                everyUpgradeList = true;
                continue;
            }

            var word = string.Empty;
            for (var screenIndex = 0; screenIndex < GameMcpListColumns.Screens.Length; screenIndex++)
            {
                if (GameMcpListColumns.Screens[screenIndex].ListId != listId) continue;
                word = GameMcpListColumns.Screens[screenIndex].Word;
                break;
            }
            if (word.Length == 0 || (screen.Length > 0 && screen != word))
                return GameMcpListColumns.Unreadable;
            screen = word;
        }

        return screen.Length > 0
            ? screen
            : everyUpgradeList
                ? GameMcpListColumns.ScreenAll
                : GameMcpListColumns.Unreadable;
    }

    /// <summary>
    /// The screen the published world says draws this entity, where it says one at all.
    /// </summary>
    /// <remarks>
    /// One category publishes a <c>screen</c> column — <c>upgrades</c> — and for an id in it a
    /// refusal can name where to go instead of only saying "not here". The evasive words are not
    /// answers and do not pass: <c>no_page</c> is the game drawing it nowhere, <c>unreadable</c> is
    /// the suite failing to read the membership, and <c>all</c> is every upgrade list at once. Each
    /// of those leaves the caller with the screen catalog, which is what the refusal falls back to.
    /// </remarks>
    internal static bool TryPublishedScreen(GameWorldState world, Guid uuid, out string screen)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        screen = WorldLookup.TryFind(world.Upgrades, uuid, out _)
            ? UpgradeScreen(world, uuid)
            : string.Empty;
        if (screen.Length == 0 ||
            string.Equals(screen, GameMcpListColumns.ScreenNoPage, StringComparison.Ordinal) ||
            string.Equals(screen, GameMcpListColumns.Unreadable, StringComparison.Ordinal) ||
            string.Equals(screen, GameMcpListColumns.ScreenAll, StringComparison.Ordinal))
        {
            screen = string.Empty;
            return false;
        }
        return true;
    }

    /// <summary>
    /// How far the player has come with this purchase, in the one vocabulary every purchasable
    /// row shares.
    /// </summary>
    /// <remarks>
    /// The game's own members answer this and the suite already captures both of them.
    /// <c>UpgradeSO.IsAvailable()</c> — which the binder reads, and which is exactly
    /// <c>!IsMaxLevel() &amp;&amp; prerequisites.Check()</c> — is the middle state together with
    /// the screen gate <see cref="IsUpgradeOffered"/> adds, and it is also what
    /// <c>UIUpgradeButton</c> renders its row on, so a row the player can see and a row that is
    /// <see cref="GameMcpListColumns.Available"/> are the same set. Exhaustion is asked first
    /// because a finished upgrade is finished whatever its prerequisites went on to do, and
    /// because that is the order the game's own predicate uses. Nothing here reads
    /// <c>UpgradeSO.IsVisible()</c>: despite the name it is the prerequisite gate alone and stays
    /// true for a maxed upgrade, so it would call a completed row locked.
    /// </remarks>
    internal static string UpgradeState(GameWorldState world, in WorldUpgrade upgrade) =>
        upgrade.IsExhausted ? GameMcpListColumns.Completed :
        IsUpgradeOffered(world, in upgrade) ? GameMcpListColumns.Available :
        upgrade.Reading.Available && !WorldUpgradeScreenLists.TryReadScreenGate(world, upgrade.EntityId, out _)
            ? GameMcpListColumns.Unreadable
            : GameMcpListColumns.Locked;

    /// <summary>
    /// Whether the game draws this upgrade's row: its own prerequisites hold and the screen its
    /// list is swapped into is unlocked.
    /// </summary>
    /// <remarks>
    /// <c>UpgradeSO.IsAvailable()</c> alone is not the row. A row on a screen the game has not
    /// unlocked is drawn nowhere, so Aspect: Workshop, which has no prerequisites, read as available
    /// in a run with no World > Aspects pedestal and was refused by the purchase it invited. Every
    /// read that offers an upgrade asks this, so the list, the search, the page and its purchase
    /// verdict cannot disagree about one row.
    /// </remarks>
    internal static bool IsUpgradeOffered(GameWorldState world, in WorldUpgrade upgrade) =>
        WorldUpgradeScreenLists.IsOffered(world, in upgrade);

    /// <summary>
    /// Whether the screen this upgrade is drawn on is still locked, and which view gates it. A row
    /// on no screen panel, or one whose membership could not be read, has no screen gate to ask:
    /// its <c>screen</c> cell already says which of the two it is.
    /// </summary>
    internal static bool IsUpgradeScreenLocked(GameWorldState world, Guid upgradeId, out Guid viewId) =>
        WorldUpgradeScreenLists.TryReadScreenGate(world, upgradeId, out viewId) &&
        viewId != Guid.Empty &&
        !IsScreenUnlocked(world, viewId);

    /// <summary>
    /// Whether the game draws the screen an action lives on. <c>ViewSO.IsAvailable()</c> is the
    /// game's own question, and the world publishes its answer for every view, so a locked screen
    /// is read here rather than guessed from what else happens to be empty.
    /// </summary>
    internal static bool IsScreenUnlocked(GameWorldState world, Guid viewId) =>
        WorldLookup.TryFind(world.Views, viewId, out var view) && view.Available;
}
#endif
