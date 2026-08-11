#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The words a list cell is allowed to say: one short fact, from one vocabulary, for every row of
/// every category.
/// </summary>
/// <remarks>
/// <para>
/// Every column here answers a planning question, and the test for whether it may exist is whether
/// two reads seconds apart, with nobody touching the game between them, would agree. A column that
/// turns over on its own — casting-now, engaged-this-tick, the queue has-not-landed-yet — fails it,
/// and the answer is to delete the column rather than to smooth it: a page of such a column plans
/// nothing, and it was already false when the caller read it. A live round caught spell-slots'
/// <c>casting</c> present on one page of a scan and absent on the next with nothing about the
/// request changed, which is the shape of the whole problem. Nothing is deleted from the world:
/// the raw-fact scan keeps every one of these, <c>world_get</c> keeps them where a reader asked
/// about one row, and the action responses keep them because an action question is what they answer.
/// </para>
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
/// <para>
/// A blocked row answers the same way. It used to answer with a code and a sentence — the pair a
/// refusal carries — repeated in full on every row: twenty agromancy rows paid for the same 133
/// characters of prose about a prerequisite the game does not evaluate until you press, and a
/// maxed upgrade said "This is already at its maximum level." beside an <c>affordable</c> column
/// that already said <c>already_maxed</c>. A cell now says one word or the column that already
/// names the fact says it alone; the sentence keeps living where a reader asked for one, in
/// <c>get</c> and in refusals. No cell ever carries an <c>ERR_</c> class: those name which kind of
/// no a refusal is, and a table is not refusing anything.
/// </para>
/// <para>
/// Everything the player buys moves through the same three states, so it says them with the same
/// three words: <see cref="Locked"/>, <see cref="Available"/>, <see cref="Completed"/>. That is a
/// lifecycle — how far the player has come with this row — and it is deliberately not the question
/// of whether a purchase would go through right now. Affordability and per-level requirements are
/// a second, independent axis, and they keep their own columns; a row that cannot be paid for is
/// still <see cref="Available"/>, because next week it will be bought with no state having moved.
/// The word <c>purchasable</c> is banned outright for confusing the two.
/// </para>
/// <para>
/// The word belongs to every category in which the player can meet a locked thing, not only the
/// ones that are bought. What the player experiences as lockedness is the fact, and where the game
/// hides a row or swaps a placeholder in front of it rather than greying it, that hiding <em>is</em>
/// the locked state. So alchemy recipes, glyphs, rituals, plot nodes and challenges say it too, each
/// off the member the game's own row renderer asks.
/// </para>
/// <para>
/// How many of the three words a category reaches is a fact about the category rather than a shape
/// imposed on it. Only <see cref="Completed"/> needs a ceiling to exist, and most of these have
/// none: a structure has no <c>maxLevel</c> field, <c>GlyphSO.CanLevel()</c> is the constant
/// <c>true</c>, an alchemy recipe's <c>maxLevel</c> is the level it has reached rather than one it
/// stops at, a ritual is re-run forever, and a plot node grows mastery without end. Two words is
/// the honest whole of those categories; inventing a third would be worse than lacking one. Only
/// upgrades, research and challenges reach all three.
/// </para>
/// </remarks>
internal static class GameMcpListColumns
{
    /// <summary>No ceiling applies. The game marks this with a negative native maximum.</summary>
    internal const string Uncapped = "uncapped";

    /// <summary>
    /// Prerequisites do not hold yet: the player has not reached this far, and the game shows no
    /// row for it.
    /// </summary>
    internal const string Locked = "locked";

    /// <summary>
    /// Prerequisites hold and nothing is finished: the game shows this row, and a purchase is the
    /// next thing that could happen to it. It says nothing about whether the price can be paid —
    /// that is the separate can-purchase axis this column never speaks for.
    /// </summary>
    internal const string Available = "available";

    /// <summary>
    /// Every level is bought. The game's own row disappears at this point — <c>IsAvailable()</c>
    /// is what <c>UIUpgradeButton</c> renders on, and it is false once <c>IsMaxLevel()</c> — so
    /// completion and hiding are one state rather than two words for the same row.
    /// </summary>
    internal const string Completed = "completed";

    /// <summary>The next purchase's own per-level conditions all hold.</summary>
    internal const string Met = "met";

    /// <summary>At least one per-level condition does not hold yet.</summary>
    internal const string Unmet = "unmet";

    /// <summary>
    /// A condition this suite does not model, so no verdict is honest. Distinct from
    /// <see cref="Unevaluated"/> on purpose: that one is a missing holding beside a published
    /// price, this one is the suite's own gap in the requirement grammar, and the two call for
    /// different things — a gap is filed, a missing holding waits for the next generation.
    /// </summary>
    internal const string Unmodelled = "unmodelled";

    /// <summary>The publication names no price for this row.</summary>
    internal const string Unpriced = "unpriced";

    /// <summary>
    /// A price is published, but this generation carried no same-generation holding to compare it
    /// against — stated rather than silently treated as zero holdings.
    /// </summary>
    internal const string Unevaluated = "unevaluated";

    /// <summary>The suite could not read this fact from the game this generation.</summary>
    internal const string Unreadable = "unreadable";

    /// <summary>
    /// Which screen's upgrade panel groups this row, in the player's own word for that screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game's grouping axis for upgrades is the screen, and it spells it as nine hand-authored
    /// <c>UpgradeListVariable</c> assets swapped into one panel by <c>ListViewSwapper</c> on the
    /// active view. Eight of them are disjoint screen panels; the ninth, <c>AllUpgrades</c>, holds
    /// every upgrade in the game, so membership in it is not a screen fact at all. That is why it is
    /// not the word for a row a real screen also carries: it would say the same thing about all 229
    /// rows and so say nothing. It is the word only where it is the whole truth — the four
    /// cap-raisers, which no screen panel carries and which the player therefore finds only on the
    /// Upgrades screen.
    /// </para>
    /// <para>
    /// Each word is the game's own tab label, lowercased — the seven top-bar screens plus the one
    /// subtab that sells upgrades of its own, World &gt; Aspects. Naming that subtab rather than its
    /// screen is the more useful truth: the three aspects are not on the World panel, and a reader
    /// told <c>world</c> would look for them there.
    /// </para>
    /// <para>
    /// The words are pinned against list identities rather than read off a name. Six of the nine
    /// lists carry no authored display name at all, and a name is a diagnostic under the boundary
    /// doctrine either way; the identity is the stable key, and the pinned build is what makes the
    /// pairing a constant rather than a guess. A row whose membership the suite could not read says
    /// <see cref="Unreadable"/> and never a plausible screen: membership is published whole or
    /// withheld whole, so a guess here would be indistinguishable from the truth.
    /// </para>
    /// </remarks>
    internal const string ScreenMagic = "magic";
    internal const string ScreenWorkshop = "workshop";
    internal const string ScreenWorld = "world";
    internal const string ScreenAlchemy = "alchemy";
    internal const string ScreenRituals = "rituals";
    internal const string ScreenScholar = "scholar";
    internal const string ScreenAspects = "aspects";
    internal const string ScreenTime = "time";

    /// <summary>The catch-all Upgrades panel, and the word only for a row no screen panel carries.</summary>
    internal const string ScreenAll = "all";

    /// <summary>
    /// The authored list identity behind each screen word, and the one list that is not a screen.
    /// </summary>
    /// <remarks>
    /// Three of these lists — Scholar's twenty-seven upgrades, the three world aspects, and the
    /// empty Time list — are named by no <c>ViewSO</c> anywhere in the serialized object graph:
    /// their only consumer is prefab swapper data that lives outside both the assembly and the
    /// dump. Pinning the identity is what reaches them, and it is why Scholar's upgrades say
    /// <see cref="ScreenScholar"/> here instead of disappearing into the same blank the four
    /// cap-raisers would have left.
    /// </remarks>
    internal static readonly (Guid ListId, string Word)[] Screens =
    {
        (KnownEntities.UpgradesMagicScreen.Uuid, ScreenMagic),
        (KnownEntities.UpgradesWorkshopScreen.Uuid, ScreenWorkshop),
        (KnownEntities.UpgradesWorldScreen.Uuid, ScreenWorld),
        (KnownEntities.UpgradesAlchemyScreen.Uuid, ScreenAlchemy),
        (KnownEntities.UpgradesRitualScreen.Uuid, ScreenRituals),
        (KnownEntities.UpgradesScholarScreen.Uuid, ScreenScholar),
        (KnownEntities.UpgradesAspectsScreen.Uuid, ScreenAspects),
        (KnownEntities.UpgradesTimeScreen.Uuid, ScreenTime),
    };

    internal static Guid EveryUpgradeList => KnownEntities.UpgradesAll.Uuid;

    /// <summary>
    /// What a challenge's own run is doing, which is not a lifecycle and never wears its word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These five are <c>ChallengeSO.ChallengeState</c>, whose own tooltips the game names
    /// <c>ChallengeInactive</c>, <c>ChallengeQueued</c>, <c>ChallengeActive</c>,
    /// <c>ChallengePassed</c> and <c>ChallengeFailed</c>. They say how one attempt went, and a
    /// challenge passed at level three is neither finished nor further along than a challenge
    /// nobody has entered — it is simply between runs. That is why the column they live in is
    /// <c>run</c> rather than <c>state</c>: the lifecycle word belongs to the same question every
    /// other category answers with it, and two vocabularies cannot share one column name.
    /// </para>
    /// <para>
    /// The words themselves are unchanged from before the rename. They were already right for the
    /// fact; only the column they sat in was wrong.
    /// </para>
    /// </remarks>
    internal const string RunIdle = "idle";
    internal const string RunQueued = "queued";
    internal const string RunActive = "active";
    internal const string RunPassed = "passed";
    internal const string RunFailed = "failed";

    /// <summary>The slot holds nothing.</summary>
    internal const string Empty = "empty";

    /// <summary>The entry is not automated, so it repeats no number of times.</summary>
    internal const string Manual = "manual";

    /// <summary>The loadout does not hold this recipe, so it occupies no position.</summary>
    internal const string Unslotted = "unslotted";

    /// <summary>The game published no value under the member this column names.</summary>
    internal const string Unset = "unset";

    /// <summary>Nothing stands in the way of what this column asks about.</summary>
    internal const string Yes = "yes";

    /// <summary>
    /// No, and the row's other columns are where the why is: the same word a boolean column
    /// renders, so one cell does not answer in a second grammar.
    /// </summary>
    internal const string No = "no";

    /// <summary>
    /// The one word a decision cell says instead of the sentence a refusal would say, for every
    /// producer code a list row can reach.
    /// </summary>
    /// <remarks>
    /// One word per fact and one vocabulary across categories, so a reader learns a word once:
    /// <see cref="Unpriced"/> means the same thing in an <c>affordable</c> column and in an
    /// agromancy <c>add</c> cell, because it is the same fact — the game publishes no price. A code
    /// with no word here is a defect rather than a cell, and it says so at the first row of the
    /// first page, the way a category with no declared column set does.
    /// </remarks>
    internal static string Word(string code)
    {
        if (code is null) throw new ArgumentNullException(nameof(code));
        return code switch
        {
            // The game shows no offer for this pair, or shows it more than once and will not say
            // which one a press would take.
            "not_offered" => "not_offered",
            "ambiguous_offer" => "ambiguous",

            // No published price, so affordability cannot be read — the fact `affordable` already
            // has a word for.
            "cost_unavailable" => Unpriced,

            // The plot itself is what is short: of room, or of what the action consumes.
            "plot_quantity_insufficient" => "plot_short",
            "plot_action_list_full" => "list_full",

            // The game latches this prerequisite only when the action is started, so the read is
            // what is missing rather than the permission.
            "prerequisite_unverified" => "unverified",

            "not_active" => "inactive",

            // A shortfall the row's own cost and holding columns do not already show: the ceiling
            // is bandwidth rather than the amount held.
            "insufficient_bandwidth" => "no_bandwidth",

            _ => throw new InvalidOperationException(
                "a list cell reached decision code '" + code +
                "' with no word for it; a cell says one word from the list vocabulary or the " +
                "column that already names the fact says it alone"),
        };
    }

    /// <summary>
    /// What one row of a hand-written list projection must carry, in the projection's own
    /// vocabulary — the wire renames some of these on the way out, but it renames them for every
    /// row alike, so a set that is total here is total on the page.
    /// </summary>
    private static readonly Dictionary<string, string[]> Columns = Declare();

    /// <summary>Every category that builds its own list rows, and therefore declares them.</summary>
    internal static IReadOnlyCollection<string> Categories => Columns.Keys;

    /// <summary>
    /// This category's declaration, where it has one. A category with no hand-written projection
    /// renders from its declared field list instead, which is total by construction.
    /// </summary>
    internal static bool TryDeclared(string category, out string[] declared) =>
        Columns.TryGetValue(category, out declared!);

    private static string[] Declared(string category) =>
        Columns.TryGetValue(category, out var declared)
            ? declared
            : throw new InvalidOperationException(
                "world category '" + category +
                "' builds its own list rows but declares no total column set");

    private static Dictionary<string, string[]> Declare() => new(StringComparer.Ordinal)
    {
        ["structures"] =
            new[] { "entityId", "level", "queuedLevels", "state", "enabled", "affordable" },
        ["upgrades"] = new[]
        {
            "entityId", "level", "queuedLevels", "screen", "state", "maximum", "requirements",
            "affordable",
        },
        ["equipment"] = new[] { "entityId", "created", "equippedCount" },
        ["rituals"] = new[]
        {
            "entityId", "state", "selected", "reachedLevel", "selectedLevel", "waveTotal",
            "affordable",
        },
        ["alchemy-recipes"] = new[] { "entityId", "state", "masteryLevel" },
        ["research"] = new[]
        {
            "entityId", "state", "paused", "totalLevel", "queuedLevels", "requirements",
            "canDevelop", "affordable",
        },
        ["resource-types"] = new[] { "entityId", "level", "hidden" },
        ["glyphs"] = new[]
        {
            "entityId", "state", "discovered", "paidLevel", "bonusLevel", "totalLevel",
        },
        ["plot-nodes"] = new[]
        {
            "entityId", "state", "masteryLevel", "quantity", "availableQuantity",
        },
        ["purchase-costs"] =
            new[] { "resourceId", "cost", "spendableAmount", "affordable", "targetId" },
        ["challenges"] = new[] { "entityId", "state", "run", "level" },
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
        ["spell-slots"] = new[] { "slot", "spellRecipeId" },
        ["spell-costs"] = new[] { "slot", "kind", "resourceId", "amount" },
        ["alchemy-instances"] =
            new[] { "recipe", "activeCount", "queuedCount", "drainRatio" },
        ["alchemy-loadout"] = new[] { "recipeId", "slot", "slotCount", "amount" },
        ["agromancy-processing"] = new[]
        {
            "slot", "capacity", "used", "plot", "action", "amount",
        },
        ["agromancy-plot-actions"] = new[] { "plot", "action", "active", "add", "remove" },
        ["targeting"] = new[] { "owner", "candidates" },
    };

    /// <summary>
    /// Hold one built row to its category's declaration, so a column cannot be reintroduced as
    /// conditional by an edit that only looked at the row in front of it.
    /// </summary>
    /// <remarks>
    /// This fires on the first row of the first page of the category, which is every test that
    /// reads it — the same loudness <c>ScanFields</c> has for a category with no scan projection,
    /// and for the same reason: the shape a surface promises is not a runtime variable. The
    /// verdict pair used to be exempt, which made a page holding one refused row wider than the
    /// same page without it; now the declared set is the whole truth and there is no exception.
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
        foreach (var extra in present)
        {
            throw new InvalidOperationException(
                "list category '" + category + "' published undeclared column '" + extra +
                "'; a column is declared for every row or it is not a column");
        }
    }
}
#endif
