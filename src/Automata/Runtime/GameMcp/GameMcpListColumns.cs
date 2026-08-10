#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The words a list column uses when the fact it names does not apply to one row.
/// </summary>
/// <remarks>
/// <para>
/// A category's columns are a declared, total set: every row fills every column, in every world
/// state. The alternative — publishing a field only where it has a value — makes the header the
/// union of whatever the page's rows happened to carry, so a page loses exactly the columns whose
/// absence it most needed to explain. An all-uncapped upgrades page dropped <c>maxLevel</c> and
/// <c>remainingLevels</c> entirely, and a reader who had never seen a capped page could not learn
/// from it that caps exist.
/// </para>
/// <para>
/// So absence stops being a value and a word takes its place — one that names the actual fact,
/// never a number that would be read as one. The game's own uncapped marker is a negative maximum;
/// it maps to <see cref="Uncapped"/> and never to <c>0</c> or a large literal. The header hoist
/// keeps this close to free: a page where every row says the same word says it once, in the header.
/// </para>
/// </remarks>
internal static class GameMcpListColumns
{
    /// <summary>No ceiling applies. The game marks this with a negative native maximum.</summary>
    internal const string Uncapped = "uncapped";

    /// <summary>Nothing is left to buy, so there is no next-level price to be short of.</summary>
    internal const string AlreadyMaxed = "already_maxed";

    /// <summary>The publication names no price for this row.</summary>
    internal const string Unpriced = "unpriced";

    /// <summary>
    /// A price is published, but this generation carried no same-generation holding to compare it
    /// against — stated rather than silently treated as zero holdings.
    /// </summary>
    internal const string Unevaluated = "unevaluated";

    /// <summary>The suite could not read this fact from the game this generation.</summary>
    internal const string Unreadable = "unreadable";

    /// <summary>The slot holds nothing.</summary>
    internal const string Empty = "empty";

    /// <summary>The entry is not automated, so it repeats no number of times.</summary>
    internal const string Manual = "manual";

    /// <summary>The loadout does not hold this recipe, so it occupies no position.</summary>
    internal const string Unslotted = "unslotted";

    /// <summary>The game published no value under the member this column names.</summary>
    internal const string Unset = "unset";

    /// <summary>
    /// A refusal explains itself in the grammar every surface shares, so the verdict pair is
    /// present exactly where there is a no and absent where the answer is yes — which the row's own
    /// decision column already states. That absence never means "not applicable".
    /// </summary>
    private static readonly string[] Verdict = { "reasonCode", "reason" };

    /// <summary>
    /// What one row of a hand-written list projection must carry, in the projection's own
    /// vocabulary — the wire renames some of these on the way out, but it renames them for every
    /// row alike, so a set that is total here is total on the page.
    /// </summary>
    private static readonly Dictionary<string, string[]> Columns = Declare();

    /// <summary>Every category that builds its own list rows, and therefore declares them.</summary>
    internal static IReadOnlyCollection<string> Categories => Columns.Keys;

    private static string[] Declared(string category) =>
        Columns.TryGetValue(category, out var declared)
            ? declared
            : throw new InvalidOperationException(
                "world category '" + category +
                "' builds its own list rows but declares no total column set");

    private static Dictionary<string, string[]> Declare() => new(StringComparer.Ordinal)
    {
        ["structures"] =
            new[] { "entityId", "level", "queuedLevels", "enabled", "affordable" },
        ["upgrades"] = new[]
        {
            "entityId", "level", "queuedLevels", "maxLevel", "remainingLevels", "affordable",
            "available",
        },
        ["equipment"] = new[] { "entityId", "created", "equippedCount" },
        ["rituals"] = new[]
        {
            "entityId", "discovered", "selected", "reachedLevel", "selectedLevel", "waveTotal",
            "affordable",
        },
        ["research"] = new[]
        {
            "entityId", "state", "totalLevel", "queuedLevels", "canDevelop", "affordable",
        },
        ["resource-types"] = new[] { "entityId", "level", "hidden" },
        ["glyphs"] = new[]
        {
            "entityId", "discovered", "available", "paidLevel", "bonusLevel", "totalLevel",
        },
        ["plot-nodes"] = new[]
        {
            "entityId", "visible", "masteryLevel", "quantity", "idleQuantity",
            "availableQuantity",
        },
        ["purchase-costs"] =
            new[] { "resourceId", "cost", "spendableAmount", "affordable", "targetId" },
        ["challenges"] = new[] { "entityId", "state", "level" },
        ["crafting-recipes"] = new[] { "entityId", "startingAmount" },
        ["discovery-trees"] = new[] { "entityId", "mode" },
        ["resources"] = new[]
        {
            "entityId", "category", "amount", "capacity", "netRatePerSecond", "atCapacity",
        },
        ["player-loadouts"] = new[] { "name", "selected" },
        ["snapshot-loadouts"] = new[] { "name", "kind", "slots" },
        ["snapshot-slots"] = new[] { "ownerId", "slot", "populated" },
        ["snapshot-entries"] = new[] { "ownerId", "slot", "entryId", "quantity" },
        ["crafting-queue-entries"] =
            new[] { "queueId", "slot", "recipeId", "amount", "repetitions" },
        ["spell-slots"] = new[] { "slot", "spellRecipeId", "casting" },
        ["spell-costs"] = new[] { "slot", "kind", "resourceId", "amount" },
        ["alchemy-instances"] =
            new[] { "recipe", "activeCount", "queuedCount", "settled", "drainRatio" },
        ["alchemy-loadout"] = new[] { "recipeId", "slot", "slotCount", "amount" },
        ["agromancy-processing"] = new[]
        {
            "slot", "capacity", "used", "plot", "action", "amount", "processing",
        },
        ["agromancy-plot-actions"] = new[] { "plot", "action", "active", "add", "remove" },
        ["targeting"] = new[]
        {
            "pending", "owner", "ownerNativeType", "selectionType", "candidates", "randomize",
        },
    };

    /// <summary>
    /// Hold one built row to its category's declaration, so a column cannot be reintroduced as
    /// conditional by an edit that only looked at the row in front of it.
    /// </summary>
    /// <remarks>
    /// This fires on the first row of the first page of the category, which is every test that
    /// reads it — the same loudness <c>ScanFields</c> has for a category with no scan projection,
    /// and for the same reason: the shape a surface promises is not a runtime variable.
    /// </remarks>
    internal static void Verify(string category, GameMcpObject row)
    {
        if (category is null) throw new ArgumentNullException(nameof(category));
        if (row is null) throw new ArgumentNullException(nameof(row));
        var declared = Declared(category);
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < row.Properties.Count; index++)
            present.Add(row.Properties[index].Name);
        for (var index = 0; index < declared.Length; index++)
        {
            if (present.Remove(declared[index])) continue;
            throw new InvalidOperationException(
                "list category '" + category + "' left declared column '" + declared[index] +
                "' off a row; a column that does not apply says which fact does not apply");
        }
        for (var index = 0; index < Verdict.Length; index++) present.Remove(Verdict[index]);
        foreach (var extra in present)
        {
            throw new InvalidOperationException(
                "list category '" + category + "' published undeclared column '" + extra +
                "'; a column is declared for every row or it is not a column");
        }
    }
}
#endif
