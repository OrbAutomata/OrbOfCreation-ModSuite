#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldCategories
{
    internal static readonly GameMcpWorldCategory[] Categories = CreateCategories();
    internal static readonly Dictionary<string, GameMcpWorldCategory> ByName = IndexCategories();

    /// <summary>
    /// Every collection report at least one listable category is built from.
    /// </summary>
    /// <remarks>
    /// The collector runs more categories than this surface lists, and a listable category is often
    /// built from several of them, so the two counts never matched and nothing said which collectors
    /// the difference was. A collector missing from this set publishes no table of its own, and
    /// <see cref="GameMcpWorldOverview.ListCategories"/> gives it a row saying exactly that rather than
    /// leaving it to be inferred from arithmetic in a diagnostic nobody runs.
    /// </remarks>
    internal static readonly HashSet<string> ListedReportCategories = CollectReportCategories();

    /// <summary>
    /// What a diagnostic calls each collection report, in the words this surface answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both surfaces describe the same collectors, and until this join was published they described
    /// them under two vocabularies: six collectors named for a table since renamed, two more no row
    /// count could separate, and two whose names appeared on no row at all. The join was already
    /// declared on the categories themselves — every listable page names the reports it is built
    /// from — and read by nothing but a census. Reading it here is the whole of the fix, because
    /// world_categories is where a name becomes an action and the acting surface's words win.
    /// </para>
    /// <para>
    /// A report that is itself a row name keeps it. Other pages read it too, and naming them here
    /// would put three names on the dearest span of a pass to answer a question nobody asked of it.
    /// A report that feeds several pages and names none of them says all of them, because choosing
    /// one would be this code's opinion rather than the world's structure. And where two reports
    /// feed one page, each says which of the two it is: a row name printed twice reads as one
    /// collector measured twice.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> CollectionReportPages = MapReportsToPages();

    /// <summary>
    /// Every page a collection report is read into, so a caller who named the report can be sent to
    /// the pages that carry its rows rather than told the name means nothing.
    /// </summary>
    internal static readonly Dictionary<string, string[]> ListedReportPageNames = MapReportsToPageNames();

    /// <summary>
    /// The world_categories row <paramref name="report"/> feeds, or its own name where no listable
    /// page is built from it and the page therefore gives it a row of that name.
    /// </summary>
    internal static string CollectionReportPage(string report) =>
        CollectionReportPages.TryGetValue(report, out var page) ? page : report;

    /// <summary>Which listable pages each collection report is read into.</summary>
    private static Dictionary<string, List<string>> FedReports()
    {
        var fed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
        {
            var category = Categories[index];
            for (var required = 0; required < category.ReportCategories.Length; required++)
                Feeds(fed, category.ReportCategories[required], category.Name);
            for (var only = 0; only < category.FailureOnlyReportCategories.Length; only++)
                Feeds(fed, category.FailureOnlyReportCategories[only], category.Name);
        }
        return fed;
    }

    private static Dictionary<string, string[]> MapReportsToPageNames()
    {
        var fed = FedReports();
        var result = new Dictionary<string, string[]>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed) result.Add(pair.Key, pair.Value.ToArray());
        return result;
    }

    private static Dictionary<string, string> MapReportsToPages()
    {
        var fed = FedReports();
        var alone = new Dictionary<string, string>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed)
        {
            if (ByName.ContainsKey(pair.Key)) alone.Add(pair.Key, pair.Key);
            else if (pair.Value.Count == 1) alone.Add(pair.Key, pair.Value[0]);
        }

        var feeders = new Dictionary<string, int>(alone.Count, StringComparer.Ordinal);
        foreach (var pair in alone)
        {
            feeders.TryGetValue(pair.Value, out var count);
            feeders[pair.Value] = count + 1;
        }

        var result = new Dictionary<string, string>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed)
        {
            if (!alone.TryGetValue(pair.Key, out var page))
                result.Add(pair.Key, string.Join("+", pair.Value.ToArray()));
            else if (page == pair.Key || feeders[page] == 1)
                result.Add(pair.Key, page);
            else
                result.Add(pair.Key, page + " (" + pair.Key + ")");
        }
        return result;
    }

    /// <summary>
    /// Records that <paramref name="page"/> is built from <paramref name="report"/>. The pages
    /// arrive in the order <see cref="GameMcpWorldOverview.ListCategories"/> prints them, so a report
    /// feeding several names them in the order a reader will find them on that page.
    /// </summary>
    private static void Feeds(
        Dictionary<string, List<string>> fed,
        string report,
        string page)
    {
        if (!fed.TryGetValue(report, out var pages)) fed.Add(report, pages = new List<string>());
        if (!pages.Contains(page)) pages.Add(page);
    }

    private static HashSet<string> CollectReportCategories()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
        {
            var category = Categories[index];
            for (var required = 0; required < category.ReportCategories.Length; required++)
                result.Add(category.ReportCategories[required]);
            for (var only = 0; only < category.FailureOnlyReportCategories.Length; only++)
                result.Add(category.FailureOnlyReportCategories[only]);
        }
        return result;
    }

    internal static GameMcpCategoryAvailability Availability(
        GameWorldState world,
        GameMcpWorldCategory category)
    {
        for (var requiredIndex = 0;
             requiredIndex < category.ReportCategories.Length;
             requiredIndex++)
        {
            var reportName = category.ReportCategories[requiredIndex];
            var found = false;
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                found = true;
                if (report.Outcome == WorldCategoryOutcome.Unavailable)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName + " unavailable"
                            : report.FirstFailure);
                }
                if (report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        "collection is partial: " +
                        report.Skipped.ToString(CultureInfo.InvariantCulture) +
                        " native rows were skipped from " + reportName +
                        "; first failure: " +
                        (report.FirstFailure.Length == 0
                            ? "the collector did not publish a failure reason"
                            : report.FirstFailure));
                }
                break;
            }
            if (!found)
            {
                return new GameMcpCategoryAvailability(
                    false,
                    "the publication carries no collection report for " + reportName);
            }
        }

        for (var blockerIndex = 0;
             blockerIndex < category.FailureOnlyReportCategories.Length;
             blockerIndex++)
        {
            var reportName = category.FailureOnlyReportCategories[blockerIndex];
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (report.Outcome == WorldCategoryOutcome.Unavailable ||
                    report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName +
                              " degraded without a failure reason"
                            : report.FirstFailure);
                }
                break;
            }
        }

        return new GameMcpCategoryAvailability(true, string.Empty);
    }

    /// <summary>
    /// The page the Scholar screen's Concept list is read as. A Concept is an
    /// <c>AlchemyRecipeSO</c> and is not an Alchemy recipe in any sense a player meets: it is
    /// discovered on its own tree, slotted into development slots, and drains while it levels.
    /// Concepts are the seventh discovery surface, so they are a category beside the other six
    /// rather than rows inside one of them.
    /// </summary>
    internal const string ConceptsCategory = "concepts";

    /// <summary>
    /// Whether an alchemy recipe is one of the forty-six Concepts, from the recipe's own core
    /// alchemy type. Same audited mapping the discovery boundary classifies the press with, so the
    /// category a row prints and the screen that draws it cannot disagree.
    /// </summary>
    internal static bool IsScholarConcept(Guid coreTypeId) =>
        AlchemyGameplayDomainClassifier.ClassifyTypeUuid(coreTypeId) ==
            AlchemyGameplayDomain.ScholarConcept;

    /// <summary>
    /// Which of the two categories an alchemy recipe files under. A type in neither audited set
    /// stays on <c>alchemy-recipes</c>, where every recipe the world publishes has always been
    /// readable, rather than vanishing from both pages.
    /// </summary>
    internal static string AlchemyRecipeCategory(Guid coreTypeId) =>
        IsScholarConcept(coreTypeId) ? ConceptsCategory : "alchemy-recipes";

    internal static bool TryCategory(
        string name,
        out GameMcpWorldCategory category,
        out string reason)
    {
        var normalized = Normalize(name);
        if (ByName.TryGetValue(normalized, out category!))
        {
            reason = string.Empty;
            return true;
        }
        category = null!;
        reason = "unknown category '" + (name ?? string.Empty) + "'; " +
            (RetiredCategoryNames.TryGetValue(normalized, out var moved)
                ? moved
                : "call world_categories for the exact discoverable names");
        return false;
    }

    /// <summary>
    /// A name the wire used to answer to, kept only so a caller holding it is told where its rows
    /// went. `glyphs` published two player concepts under one native class, and each half is its
    /// own category now, so the refusal names both rather than sending the caller back to the list.
    /// </summary>
    private static readonly Dictionary<string, string> RetiredCategoryNames =
        new(StringComparer.Ordinal)
        {
            ["glyphs"] =
                "it named two things and is now two categories. 'augment-glyphs' is the " +
                "twenty-two a caster sockets into a spell, whose level buys slots. " +
                "'recipe-books' is the thirty-four tiles that widen a discovery pool",
            ["concept-recipes"] =
                "the forty-six Concepts are a category of their own now: list 'concepts'. This " +
                "name republished them beside the seventy-nine Alchemy recipes, and the Scholar " +
                "screen draws them nowhere near the Alchemy screen",
            ["structures"] =
                "the screen calls these attributes and so does the wire: list 'attributes'. " +
                "StructureSO is the game's class name for them, not a word any screen draws",
        };

    internal readonly struct GameMcpCategoryAvailability
    {
        internal GameMcpCategoryAvailability(bool available, string reason)
        {
            Available = available;
            Reason = reason ?? string.Empty;
        }

        internal bool Available { get; }
        internal string Reason { get; }
    }

    private static GameMcpWorldCategory[] CreateCategories()
    {
        var result = new GameMcpWorldCategory[]
        {
            Entity(nameof(GameWorldState.Resources), world => world.Resources),
            Entity("attributes", nameof(GameWorldState.Structures), world => world.Structures),
            Entity(nameof(GameWorldState.Upgrades), world => world.Upgrades),
            Entity(nameof(GameWorldState.Research), world => world.Research),
            Entity(nameof(GameWorldState.DoubleVariables), world => world.DoubleVariables),
            Entity(nameof(GameWorldState.IntVariables), world => world.IntVariables),
            Entity(nameof(GameWorldState.BoolVariables), world => world.BoolVariables),
            Entity(nameof(GameWorldState.ModifierVariables), world => world.ModifierVariables),
            Entity(nameof(GameWorldState.Statistics), world => world.Statistics),
            Entity(nameof(GameWorldState.AttributeGroups), world => world.AttributeGroups),

            // The ritual layer's vocabulary, one category per word the game itself names a class
            // with. Ten one-row-per-asset tables rather than one glossary, because the game prints
            // ten different words above them and they carry ten different sets of facts — and
            // because two of them are joined: a character attribute names a damage type.
            Entity(nameof(GameWorldState.StatusEffects), world => world.StatusEffects),
            Entity(nameof(GameWorldState.CharacterAttributes), world => world.CharacterAttributes),
            Entity(nameof(GameWorldState.DamageTypes), world => world.DamageTypes),
            Entity(nameof(GameWorldState.CharacterModifiers), world => world.CharacterModifiers),
            Entity(nameof(GameWorldState.CharacterActions), world => world.CharacterActions),
            Entity(nameof(GameWorldState.CharacterTypes), world => world.CharacterTypes),
            Entity(nameof(GameWorldState.Enchantments), world => world.Enchantments),
            Entity(nameof(GameWorldState.GlyphTypes), world => world.GlyphTypes),
            Entity(nameof(GameWorldState.RuneStones), world => world.RuneStones),
            Entity(nameof(GameWorldState.DisplayTypes), world => world.DisplayTypes),
            Composite(nameof(GameWorldState.PurchaseCosts), world => world.PurchaseCosts),

            // One native class, two player concepts, two categories — the ruling `GlyphSO` already
            // earned. `AlchemyRecipeSO` backs the seventy-nine recipes the Alchemy screen draws and
            // the forty-six Concepts the Scholar screen draws, and the two are unrelated layers of
            // the game. The split is the recipe's own core alchemy type, read through the audited
            // classifier the discovery boundary already reads it through, so no second capture and
            // no second vocabulary can drift from this one.
            Entity(
                nameof(GameWorldState.AlchemyRecipes),
                world => world.AlchemyRecipes,
                static (WorldAlchemyRecipe recipe) => !IsScholarConcept(recipe.CoreTypeId)),
            Entity(
                ConceptsCategory,
                nameof(GameWorldState.AlchemyRecipes),
                world => world.AlchemyRecipes,
                static (WorldAlchemyRecipe recipe) => IsScholarConcept(recipe.CoreTypeId)),
            Entity(nameof(GameWorldState.AlchemyTypes), world => world.AlchemyTypes),
            Entity(nameof(GameWorldState.SpellRecipes), world => world.SpellRecipes),
            Entity(nameof(GameWorldState.SpellTypes), world => world.SpellTypes),
            Entity(nameof(GameWorldState.Equipment), world => world.Equipment),
            Entity(nameof(GameWorldState.EquipmentTypes), world => world.EquipmentTypes),
            Entity(nameof(GameWorldState.ResourceTypes), world => world.ResourceTypes),
            Entity(nameof(GameWorldState.CraftingRecipeTypes), world => world.CraftingRecipeTypes),
            Entity(nameof(GameWorldState.StructureTypes), world => world.StructureTypes),
            Entity(nameof(GameWorldState.RitualTypes), world => world.RitualTypes),
            Entity("agromancy-element-types", nameof(GameWorldState.HarvestTypes),
                world => world.HarvestTypes),
            Entity(nameof(GameWorldState.PlotNodeTypes), world => world.PlotNodeTypes),
            Entity(nameof(GameWorldState.ResearchTypes), world => world.ResearchTypes),
            Entity("consumable-types", nameof(GameWorldState.ConsumableFamilies),
                world => world.ConsumableFamilies),
            Entity("plot-node-action-types", nameof(GameWorldState.HarvestActionTypes),
                world => world.HarvestActionTypes),
            Entity(nameof(GameWorldState.PassiveAbilityTypes), world => world.PassiveAbilityTypes),
            Entity(nameof(GameWorldState.TimeRuneTypes), world => world.TimeRuneTypes),
            Entity(nameof(GameWorldState.CraftingRecipes), world => world.CraftingRecipes),
            Composite(
                nameof(GameWorldState.CraftingQueueEntries),
                world => world.CraftingQueueEntries),
            Entity(nameof(GameWorldState.PlayerLoadouts), world => world.PlayerLoadouts),
            Composite(nameof(GameWorldState.PlayerLoadoutEntries), world => world.PlayerLoadoutEntries),
            Entity(nameof(GameWorldState.SnapshotLoadouts), world => world.SnapshotLoadouts),
            Composite(nameof(GameWorldState.SnapshotSlots), world => world.SnapshotSlots),
            Composite(nameof(GameWorldState.SnapshotEntries), world => world.SnapshotEntries),
            Entity("agromancy-elements", nameof(GameWorldState.HarvestElements),
                world => world.HarvestElements),
            Entity("agromancy-actions", nameof(GameWorldState.HarvestActions),
                world => world.HarvestActions),
            Entity(nameof(GameWorldState.TimeRunes), world => world.TimeRunes),
            Entity("augment-glyphs", nameof(GameWorldState.AugmentGlyphs), world => world.AugmentGlyphs),
            Entity(nameof(GameWorldState.Consumables), world => world.Consumables),
            Entity(nameof(GameWorldState.Rituals), world => world.Rituals),
            Entity(nameof(GameWorldState.Achievements), world => world.Achievements),
            Entity(nameof(GameWorldState.Advancements), world => world.Advancements),
            Entity(nameof(GameWorldState.Challenges), world => world.Challenges),
            Entity(nameof(GameWorldState.ThoughtStreams), world => world.ThoughtStreams),
            Entity(nameof(GameWorldState.Tutorials), world => world.Tutorials),
            Entity(nameof(GameWorldState.Views), world => world.Views),
            Entity(nameof(GameWorldState.PlotNodeActions), world => world.PlotNodeActions),
            Entity(nameof(GameWorldState.PassiveAbilities), world => world.PassiveAbilities),
            Entity(nameof(GameWorldState.Characters), world => world.Characters),
            Entity(nameof(GameWorldState.DiscoveryTrees), world => world.DiscoveryTrees),
            Entity(nameof(GameWorldState.RecipeBooks), world => world.RecipeBooks),
            Entity(nameof(GameWorldState.PlotNodes), world => world.PlotNodes),
            Composite("agromancy-plot-actions", nameof(GameWorldState.PlotActions),
                world => world.PlotActions),
            Composite("agromancy-processing", nameof(GameWorldState.ActionQueueSlots),
                world => world.ActionQueueSlots),
            Composite(nameof(GameWorldState.SpellSlots), world => world.SpellSlots),
            Composite(nameof(GameWorldState.SpellCosts), world => world.SpellCosts),
            Composite(nameof(GameWorldState.Targeting), world => world.Targeting),
            Composite(nameof(GameWorldState.MasteryExperience), world => world.MasteryExperience),
            Composite(nameof(GameWorldState.AlchemyInstances), world => world.AlchemyInstances),
            Composite(nameof(GameWorldState.AlchemyCosts), world => world.AlchemyCosts),
            Composite(nameof(GameWorldState.AlchemyLoadout), world => world.AlchemyLoadout),
            Composite(nameof(GameWorldState.AlchemyUsageCosts), world => world.AlchemyUsageCosts),
            Composite(nameof(GameWorldState.PlotAuthoring), world => world.PlotAuthoring),
            Composite(nameof(GameWorldState.PlotPhaseDescriptors), world => world.PlotPhaseDescriptors),
            Composite(nameof(GameWorldState.EffectBlocks), world => world.EffectBlocks),
            Composite(nameof(GameWorldState.EntityRequirements), world => world.EntityRequirements),
            Entity(nameof(GameWorldState.TreasurePools), world => world.TreasurePools),
        };
        Array.Sort(result, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return result;
    }

    private static GameMcpWorldCategory Entity<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table,
        Func<TRow, bool>? admits = null)
        where TRow : struct, IWorldEntity =>
        Entity(Normalize(propertyName), propertyName, table, admits);

    private static GameMcpWorldCategory Entity<TRow>(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table,
        Func<TRow, bool>? admits = null)
        where TRow : struct, IWorldEntity =>
        new GameMcpEntityCategory<TRow>(
            publicName,
            propertyName,
            table,
            ExpectedNativeType(Normalize(publicName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)),
            admits);

    private static GameMcpWorldCategory Composite<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct =>
        Composite(Normalize(propertyName), propertyName, table);

    private static GameMcpWorldCategory Composite<TRow>(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct =>
        new GameMcpCompositeCategory<TRow>(
            publicName,
            propertyName,
            table,
            ExpectedNativeType(Normalize(publicName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)));

    private static Dictionary<string, GameMcpWorldCategory> IndexCategories()
    {
        var result = new Dictionary<string, GameMcpWorldCategory>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
            result.Add(Categories[index].Name, Categories[index]);
        return result;
    }

    private static string ExpectedNativeType(string category) =>
        GameMcpEntityCapabilityMap.ExpectedNativeType(category);

    private static string[] RequiredReportCategories(string category) => category switch
    {
        "purchase-costs" => new[]
        {
            "attributes",
            "upgrades",
            "resources",
            "modifier-variables",
            "int-variables",
            "structure-costs",
            "upgrade-costs",
        },
        // A group's answer carries its distribution, so a member walk that did not bind is a group
        // page that reads as "this heading groups nothing" — the exact ambiguity this list exists to
        // refuse.
        "attribute-groups" => new[] { "attribute-groups", "attribute-group-members" },
        "plot-actions" => new[] { "plot-nodes", "plot-node-actions", "plot-actions" },
        "plot-action-instances" => new[] { "plot-actions" },
        "action-queue-slots" => new[] { "action-queues" },
        "spell-costs" => new[] { "spell-slots" },
        "targeting" => new[] { "targeting" },
        "alchemy-instances" or "alchemy-costs" => new[] { "concept-instances" },
        "alchemy-loadout" or "alchemy-usage-costs" =>
            new[] { "ordinary-alchemy-loadout" },
        "plot-phase-descriptors" => new[] { "plot-authoring" },
        "mastery-experience" =>
            new[] { "spell-recipes", "alchemy-recipes", "equipment" },
        "crafting-recipes" => new[]
        {
            "crafting-recipes",
            "crafting-recipe-state",
            "crafting-decisions",
            "resources",
        },
        "crafting-queue-entries" => new[] { "crafting-decisions" },
        "player-loadouts" or "player-loadout-entries" or "snapshot-loadouts" or
            "snapshot-slots" or "snapshot-entries" => new[] { "loadouts" },
        "harvest-element-controls" or "harvest-action-controls" or
            "harvest-lifecycle-costs" => new[] { "harvest-lifecycle" },
        "consumables" => new[] { "consumables", "consumable-inventory" },

        // The property is Structures because the game's class is StructureSO; the report the
        // world publishes is the screen's word.
        "structures" => new[] { "attributes" },
        _ => new[] { category },
    };

    private static string[] FailureOnlyReportCategories(string category) => category switch
    {
        // The collector emits modifier-folding only when a frame-global modifier could not be
        // reconstructed. Its absence is the clean case; its presence invalidates every row derived
        // with those globals.
        "resources" or "harvest-resources" or "purchase-costs" =>
            new[] { "modifier-folding" },
        _ => Array.Empty<string>(),
    };

    private static string[] ScanFields(string category) => category switch
    {
        "resources" => new[]
        {
            "entityId", "reading.visible", "reading.quantity", "reading.capacity",
            "reading.rate", "reading.usage", "reading.reservation", "trueQuantity",
            "trueRate", "fillFraction", "isCapped", "isAtCapacity", "headroom",
        },
        "structures" => new[]
        {
            "entityId", "reading.unlocked", "reading.level", "reading.queuedLevels",
            "effectiveLevel", "hasWorkInFlight",
            "developmentProgress", "reading.insufficientReqPenaltyActive",
        },
        "upgrades" => new[]
        {
            "entityId", "reading.available", "reading.level", "reading.maxLevel",
            "reading.queuedLevels",
            "remainingLevels", "isExhausted", "isDeveloping", "developmentProgress",
        },
        "research" => new[]
        {
            "entityId", "level", "queuedLevels", "maxLevel", "researchStage",
            "available", "visible", "complete", "canDevelop", "withinDevelopRange",
            "meetsLevelRequirements", "stillHasLeeway", "belowArtificialMaxLevel",
            "belowMaxInvestmentLevel", "isActive", "isDeveloping", "flagged",
            "purchasedLevels", "baseLevel", "bonusLevel", "totalLevel", "artificialMaxLevel",
            "baseRequirementLevel", "effectiveRequirementLevel",
            "requirementLevelAdjustment", "requirementAdjustments",
        },
        "double-variables" or "int-variables" =>
            new[] { "entityId", "value", "isPercent" },
        "bool-variables" =>
            new[] { "entityId", "value", "initialValue", "isSaved" },
        "modifier-variables" =>
            new[] { "entityId", "amount", "order" },

        // The glossary sentence is deliberately absent here and present on the list page below.
        // A detail read already prints the game's own words for the thing it describes, read
        // through the very native type this category declares, so a `description` on the row would
        // be the same sentence twice on one page — while the list page, which has no such line, is
        // the one read that turns 211 rows into a glossary rather than 211 further calls.
        "statistics" => new[] { "entityId", "displayType", "isPercent" },

        // Same rule one register over: a detail read on any of these prints the game's own sentence
        // for the thing already, read through the native type the category declares, so the row does
        // not spell it a second time. Five of the eleven carry no scalar at all — the asset is a
        // handle and a word — and their scan is the identity the list page expands into a name.
        "status-effects" => new[]
        {
            "entityId", "isBuff", "maxDuration", "stacksSeparately",
            "resetDurationOnApplication", "effectTimer",
        },
        "character-attributes" => new[] { "entityId", "damageTypeId" },
        "damage-types" => new[] { "entityId", "damageReductionRate", "ignoreEntrenched" },
        "character-modifiers" => new[] { "entityId", "weightChance" },
        "character-actions" => new[] { "entityId", "prepTime", "actTime", "speedMod" },
        "character-types" or "enchantments" or "glyph-types" or "rune-stones" or
            "display-types" or "attribute-groups" => new[] { "entityId" },
        "purchase-costs" => new[]
        {
            "entityId", "resourceId", "baseExactAmount", "effectiveExactAmount",
            "amount", "exactGroupedLevels", "exactGroupedAmount", "modifierSources",
            "affordabilityEvaluated", "availableAmount", "combinedEffectiveAmount",
            "resourceAffordable", "resourceAffordabilityReasonCode", "affordable",
            "affordabilityReasonCode",
        },
        "alchemy-recipes" => new[]
        {
            "entityId", "coreTypeId", "discovered", "masteryLevel", "masteryXp",
            "maxLevel", "advancementLevel",
        },
        "alchemy-types" => new[]
        {
            "entityId", "selectedLevelId", "level", "maxUsageByMastery",
        },
        "alchemy-loadout" => new[]
        {
            "recipeId", "position", "slotCount", "amount", "targetAmount", "multiBuy",
            "freeUsesRemaining", "maximumAdd", "discovered", "canAdd", "isActive",
            "nextAdd", "nextRemove",
        },
        "alchemy-usage-costs" => new[] { "recipeId", "resourceId", "amount" },
        "spell-recipes" => new[]
        {
            "entityId", "discovered", "masteryLevel", "masteryLevelReady",
            "masteryXp", "baseCharges", "castSpeed",
        },
        "spell-types" => new[]
        {
            "entityId", "typeLevel", "typeXp", "isVisible", "isElemental",
            "isLoadoutUnique",
        },
        "equipment" => new[]
        {
            "entityId", "isCreated", "masteryLevel", "masteryXp", "equippedLevel",
            "attuningLevel", "attunementTimeLeft",
        },
        "equipment-types" => new[]
        {
            "entityId", "level", "freeLevels", "baseUsage", "masteryLevel",
            "maxTypeSlots",
        },
        "resource-types" =>
            new[] { "entityId", "level", "freeLevels", "specialHidden" },
        "crafting-recipe-types" => new[]
        {
            "entityId", "startingLevel", "maxStartingLevel", "craftVerb",
            "initiated",
        },

        // The nine taxonomies whose assets are the types themselves. Every fact the world captured
        // for one of them is on its row, because there is no second place any of them is already
        // said. What is deliberately not here is the distributors' magnitude: that is a total
        // derived from the contribution rows and it is read through the worth block, where the
        // sentence that stops a reader multiplying it into a member sits beside it. Two classes
        // store nothing at all, so their row is their handle and the worth block is the answer.
        "harvest-action-types" or "passive-ability-types" => new[] { "entityId" },
        "structure-types" => new[]
        {
            "entityId", "baseEffectLevel", "baseBuildTime", "overrideRankDefault",
            "overrideRank",
        },
        "consumable-families" => new[] { "entityId", "hidden", "sortOrder" },
        "ritual-types" => new[] { "entityId", "initiated", "activeRituals" },
        "harvest-types" => new[] { "entityId", "level" },
        "plot-node-types" => new[] { "entityId", "totalLevel" },
        "time-rune-types" => new[] { "entityId", "initialized", "totalLevel" },
        "research-types" => new[]
        {
            "entityId", "linkedDevelopCost", "linkedResourceCost", "linkedResearchTime",
            "ignoreWhenLevelDependent", "persistThroughReset", "cachedTotalLevel",
            "cachedPeakLevel", "cachedQueuedLevel", "cachedDevelopingLevel",
            "cachedQueuedValue", "cachedInvestmentLevel", "cachedPurchasedLevel",
            "freeBonusLevels", "usedBonusLevels", "maxInvestmentLevel",
        },
        "crafting-recipes" => new[]
        {
            "entityId", "reading.visible", "reading.visibilityReasonCode",
            "reading.canBuyAtStartingQuantity", "reading.nativePurchaseReasonCode",
            "reading.startingQuantity", "reading.outputWithinCapacity",
            "reading.outputCapacityReasonCode",
        },
        "crafting-queue-entries" => new[]
        {
            "queueId", "slot", "recipeId", "amount", "automatic", "repetitions",
        },
        "player-loadouts" => new[]
        {
            "entityId", "name", "selected", "savesEquipment", "savesAlchemy",
            "icon", "color", "canSwitchNow",
        },
        "player-loadout-entries" =>
            new[] { "ownerId", "kind", "entryId", "referenceId", "quantity" },
        "snapshot-loadouts" => new[] { "entityId", "kind", "slots" },
        "snapshot-slots" => new[] { "ownerId", "slot", "populated" },
        "snapshot-entries" => new[] { "ownerId", "slot", "entryId", "quantity" },
        "harvest-elements" => new[]
        {
            "entityId", "masteryLevel", "masteryXp", "instances", "harvestTime",
            "growthTime", "harvestRate",
        },

        // Every scalar the class stores, which is the whole of what a bonus on this verb can move:
        // GetScalingInfo() loads exactly these three, with costMod standing in for the drain
        // modifier as well. Which types the verb wears is the keywords cell, not a column.
        "harvest-actions" => new[] { "entityId", "power", "speed", "costMod" },
        "harvest-resources" => new[]
        {
            "entityId", "elementId", "resource.entityId", "resource.reading.visible",
            "resource.trueQuantity", "resource.trueRate",
        },
        "harvest-element-controls" => new[]
        {
            "elementId", "visible", "active", "maximumAdditional",
            "listSpaceAvailable", "usageAffordable", "addAvailable", "removeAvailable",
        },
        "harvest-action-controls" => new[]
        {
            "elementId", "actionId", "visible", "active", "maximum",
            "addAvailable", "removeAvailable",
        },
        "harvest-lifecycle-costs" => new[]
        {
            "elementId", "actionId", "kind", "resourceId", "amount",
        },
        "time-runes" => new[]
        {
            "entityId", "discovered", "level", "masteryLevel", "masteryXp", "seen",
        },
        "augment-glyphs" => new[]
        {
            "entityId", "level", "freeLevels", "discovered", "discoverable",
            "maxUsages",
        },
        "consumables" => new[]
        {
            "entityId", "visible", "quantity", "queuedQuantity", "maximumCarryLoad",
            "currentPrepTime", "currentCooldownTime", "canFire",
        },
        "rituals" => new[]
        {
            "entityId", "discovered", "inBattle", "activeInstances", "reachedLevel",
            "selectedLevel", "wavesCompleted",
        },
        "achievements" =>
            new[] { "entityId", "level", "seen", "maxLevels" },
        "advancements" =>
            new[] { "entityId", "levels", "xp", "isPersistent" },
        "challenges" => new[]
        {
            "entityId", "level", "state", "seen", "rewardQueued", "maxLevel",
            "difficulty",
        },
        "thought-streams" => new[] { "entityId", "state" },
        "tutorials" => new[] { "entityId", "isCompleted" },
        "views" => new[] { "entityId", "active", "alwaysActive", "available" },
        "plot-node-actions" => new[]
        {
            "entityId", "hasBeenUsed", "isGrowingAction", "elementCost", "baseTime",
            "parallelAction", "prerequisiteCount", "resourceCostCount",
        },
        "passive-abilities" => new[]
        {
            "entityId", "muted", "touched", "hidden", "global", "tokenRate",
        },
        "characters" =>
            new[] { "entityId", "discovered", "numberSlain", "floats" },
        "discovery-trees" => new[]
        {
            "entityId", "actionMode", "actionTime", "rerollsLeft",
            "totalDiscoveredCount", "hasRemainingDiscovery",
            "hasCompletedAllDiscoveries", "selectedChoiceId",
        },
        "recipe-books" => new[] { "entityId", "available" },
        "plot-nodes" => new[]
        {
            "entityId", "reading.visible", "reading.masteryLevel",
            "reading.currentTime", "reading.idleQuantity", "reading.totalQuantity",
            "remainingQuantity", "remainingTotalQuantity",
        },
        "plot-actions" => new[]
        {
            "plotNodeId", "plotNodeActionId", "reading.offeredCount",
            "reading.instanceCount", "reading.prerequisitesConfirmed", "elementCost",
            "elementCostKnown", "hasEnoughForOneInstance",
            "maximumRemainingInstances",
        },
        "plot-action-instances" => new[]
        {
            "plotNodeId", "plotNodeActionId", "ordinal", "quantity", "engaged",
            "empty", "referenceResolved",
        },
        "action-queues" => new[]
        {
            "entityId", "queueId", "slotCount", "usedSlots", "emptySlots",
            "hasEmptySlot", "consistent",
        },
        "action-queue-slots" => new[]
        {
            "queueId", "index", "empty", "plotNodeId", "plotNodeActionId",
            "quantity", "engaged",
        },
        "spell-slots" => new[]
        {
            "slotIndex", "spellRecipeId", "occupied",
            "casting", "readyingCast", "attuning", "toggled", "castReady",
            "chargeAvailable", "resourcesCovered", "currentCharges",
            "maximumCharges", "cooldownRemaining",
        },
        "spell-costs" => new[] { "slotIndex", "kind", "resourceId", "amount" },
        "targeting" => new[]
        {
            "ownerName", "ownerNativeType", "selectionNativeType", "cancelAvailable",
            "candidates.position", "candidates.structureId",
        },
        "mastery-experience" => new[]
        {
            "sequence", "domain", "sourceId", "sourceMastery", "sourceEligible",
            "amount",
        },
        "alchemy-instances" => new[]
        {
            "recipeId", "quantity", "queuedQuantity", "drainReadable", "drainRatio",
        },
        "alchemy-costs" => new[] { "recipeId", "kind", "resourceId", "amount" },
        "plot-authoring" => new[] { "plotNodeId", "autoActionId", "phaseCount" },
        "plot-phase-descriptors" => new[]
        {
            "plotNodeId", "ordinal", "phase", "phaseTimeSeconds", "processType",
            "exitPhase",
        },
        "effect-blocks" => new[]
        {
            "ownerId", "ordinal", "blockTypeName", "effectTypeName", "effectValue",
            "treasurePoolId", "prerequisiteCount", "modCount", "scriptCount",
        },
        "entity-requirements" => new[]
        {
            "ownerId", "ordinal", "kind", "targetId", "reqType", "baseValue",
        },
        "treasure-pools" => new[]
        {
            "entityId", "poolId", "treasuresFound", "partialReward",
            "treasureLevel", "calculatedTreasureLevel",
        },
        _ => throw new InvalidOperationException(
            "world category '" + category + "' has no deliberate MCP scan projection"),
    };

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new System.Text.StringBuilder(value.Length + 8);
        var previousWasSeparator = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-' || character == '_' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator) result.Append('-');
                previousWasSeparator = true;
                continue;
            }
            if (char.IsUpper(character) && !previousWasSeparator && result.Length > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }
        if (result.Length > 0 && result[result.Length - 1] == '-')
            result.Length--;
        return result.ToString();
    }

    internal static string[] RegisteredCategoryNames()
    {
        var result = new string[Categories.Length];
        for (var index = 0; index < Categories.Length; index++)
            result[index] = Categories[index].Name;
        return result;
    }

    /// <summary>
    /// The collection reports a listable category is built from, so a census can state which
    /// collectors this surface reaches through a table and which need a row of their own.
    /// </summary>
    internal static string[] ListedCollectionReportNames()
    {
        var result = new string[ListedReportCategories.Count];
        ListedReportCategories.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }
}

internal abstract class GameMcpWorldCategory
{
    protected GameMcpWorldCategory(
        string publicName,
        string propertyName,
        string rowTypeName,
        string expectedNativeType,
        string[] reportCategories,
        string[] failureOnlyReportCategories,
        string[] scanFields,
        string identityMode)
    {
        WorldPropertyName = propertyName;
        Name = GameMcpWorldCategories.Normalize(publicName);
        RowTypeName = rowTypeName;
        ExpectedNativeType = expectedNativeType;
        ReportCategories = reportCategories ??
            throw new ArgumentNullException(nameof(reportCategories));
        if (ReportCategories.Length == 0)
            throw new ArgumentException(
                "A world category requires at least one collection report.",
                nameof(reportCategories));
        FailureOnlyReportCategories = failureOnlyReportCategories ??
            throw new ArgumentNullException(nameof(failureOnlyReportCategories));
        ScanFields = scanFields ??
            throw new ArgumentNullException(nameof(scanFields));
        if (ScanFields.Length == 0)
            throw new ArgumentException(
                "A world category requires a deliberate MCP scan projection.",
                nameof(scanFields));
        IdentityMode = identityMode;
    }

    internal string Name { get; }
    internal string WorldPropertyName { get; }
    internal string RowTypeName { get; }
    internal string ExpectedNativeType { get; }
    internal string[] ReportCategories { get; }
    internal string[] FailureOnlyReportCategories { get; }
    internal string[] ScanFields { get; }
    internal string IdentityMode { get; }
    internal abstract int Count(GameWorldState world);
    internal abstract object Row(GameWorldState world, int index);
    internal abstract bool TryIdentity(object row, out Guid identity);
}

internal sealed class GameMcpEntityCategory<TRow> : GameMcpWorldCategory
    where TRow : struct, IWorldEntity
{
    private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

    /// <summary>
    /// Which of the table's rows this category holds, where one native class backs two player
    /// concepts and each half is a category of its own. Null holds the whole table.
    /// </summary>
    private readonly Func<TRow, bool>? _admits;

    internal GameMcpEntityCategory(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table,
        string expectedNativeType,
        string[] reportCategories,
        string[] failureOnlyReportCategories,
        string[] scanFields,
        Func<TRow, bool>? admits = null)
        : base(
            publicName,
            propertyName,
            typeof(TRow).Name,
            expectedNativeType,
            reportCategories,
            failureOnlyReportCategories,
            scanFields,
            "stable_entity_uuid")
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _admits = admits;
    }

    internal override int Count(GameWorldState world)
    {
        var table = _table(world);
        if (_admits is null) return table.Count;
        var held = 0;
        for (var index = 0; index < table.Count; index++)
            if (_admits(table[index])) held++;
        return held;
    }

    internal override object Row(GameWorldState world, int index)
    {
        var table = _table(world);
        if (_admits is null) return table[index];
        var held = 0;
        for (var position = 0; position < table.Count; position++)
        {
            if (!_admits(table[position])) continue;
            if (held == index) return table[position];
            held++;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal override bool TryIdentity(object row, out Guid identity)
    {
        if (row is TRow typed)
        {
            identity = typed.EntityId;
            return identity != Guid.Empty;
        }
        identity = Guid.Empty;
        return false;
    }
}

internal sealed class GameMcpCompositeCategory<TRow> : GameMcpWorldCategory
    where TRow : struct
{
    private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

    internal GameMcpCompositeCategory(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table,
        string expectedNativeType,
        string[] reportCategories,
        string[] failureOnlyReportCategories,
        string[] scanFields)
        : base(
            publicName,
            propertyName,
            typeof(TRow).Name,
            expectedNativeType,
            reportCategories,
            failureOnlyReportCategories,
            scanFields,
            "composite_guid_fields")
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    internal override int Count(GameWorldState world) => _table(world).Count;
    internal override object Row(GameWorldState world, int index) => _table(world)[index];
    internal override bool TryIdentity(object row, out Guid identity)
    {
        identity = Guid.Empty;
        return false;
    }
}
#endif
