#if SERVICE_CYCLE_PROFILE
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
}
#endif
