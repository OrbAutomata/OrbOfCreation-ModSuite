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


using static OrbAutomata.GameMcp.GameMcpCastProjection;
using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpRecipeBookProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpScreenState;
using static OrbAutomata.GameMcp.GameMcpSpellLevelProjection;
using static OrbAutomata.GameMcp.GameMcpSpellLoadoutProjection;

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
                ["reasonCode"] = layout.Code.Name,
                ["reason"] = layout.Reason,
            }.Freeze();
        }
        return new JObject
        {
            ["status"] = "available",
            ["core"] = ProjectGlyphs(layout.Core),
            ["augments"] = ProjectGlyphs(layout.Augments),
            ["loaded"] = SpellWorkbenchGameAction.LoadedSpellsPointer,
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
                ["reasonCode"] = preview.Code.Name,
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
        if (preview.Replaces.Length > 0) result["replaces"] = preview.Replaces;
        if (usage.Count > 0)
        {
            result["usageAllocation"] = usage;
            result["loaded"] = SpellWorkbenchGameAction.LoadedSpellsPointer;
        }
        return result.Freeze();
    }

    internal static GameMcpValue Project(in NativeActionOutcome submission)
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

    /// <summary>
    /// Where a freshly discovered spell ended up, read off the settled loadout.
    /// </summary>
    /// <remarks>
    /// The same press that discovers a spell may also load it: <c>SpellManager.PostDiscoverRecipe</c>
    /// loads it when the loadout has a free spot and the new spell's usage cost fits. Both halves
    /// are answered from the world after the fact rather than predicted, and when it did not load
    /// the free spot says which of the two refused.
    /// <para>
    /// A recipe can sit on the bar more than once, so the first slot holding it is not necessarily
    /// the copy this press minted: the one the press produced is the occupant the world before the
    /// press did not carry. Naming the first match sent a caller to an older, higher-level copy.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectDiscoveredSpellLoadout(
        GameWorldState world,
        GameWorldState? before,
        Guid recipeId)
    {
        var copies = 0;
        var mintedCount = 0;
        var minted = default(WorldSpellSlot);
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            copies++;
            if (before is not null &&
                ContainsSpellInstance(before.SpellSlots, slot.SpellInstanceId))
            {
                continue;
            }
            minted = slot;
            mintedCount++;
        }
        if (mintedCount == 1)
        {
            var loaded = new JObject
            {
                ["loaded"] = true,
                ["slot"] = GameMcpSlotNumbering.Wire(minted.SlotIndex),
                ["uuid"] = minted.SpellInstanceId.ToString("D"),
            };
            var budget = ProjectSpellUsageBudget(world);
            if (budget.Count > 0) loaded["usageBudget"] = budget;
            return loaded.Freeze();
        }
        if (mintedCount > 1)
        {
            return PostStateUnavailable(
                "loaded_copy_not_identified",
                "The bar carries " + mintedCount + " copies of this spell that the world before " +
                "the press did not, so this answer cannot say which one the press loaded. Read " +
                "the loadout.");
        }
        var missed = new JObject
        {
            ["loaded"] = false,
            ["reason"] = copies > 0
                ? "The bar already carried this spell before the press, and no new copy joined " +
                  "it. Load one yourself if you want a second."
                : world.SpellWorkbench.HasEmptySlot
                    ? "A loadout slot was free, so the game tried to load it and its usage cost " +
                      "did not fit the spell-power headroom. Free some, then load it yourself."
                    : "Every loadout slot already held a spell, so the game left it unloaded. " +
                      "Remove one, then load it yourself.",
        };
        var freeBudget = ProjectSpellUsageBudget(world);
        if (freeBudget.Count > 0) missed["usageBudget"] = freeBudget;
        return missed.Freeze();
    }

    internal static bool TryReadDiscoveryState(
        GameWorldState world,
        Guid targetId,
        string nativeType,
        out bool discovered)
    {
        switch (nativeType)
        {
            case "SpellRecipeSO" when WorldLookup.TryFind(
                world.SpellRecipes, targetId, out var spell):
                discovered = spell.Discovered;
                return true;
            case "GlyphSO" when WorldLookup.TryFind(world.AugmentGlyphs, targetId, out var glyph):
                discovered = glyph.Discovered;
                return true;
            case "RitualSO" when WorldLookup.TryFind(world.Rituals, targetId, out var ritual):
                discovered = ritual.Discovered;
                return true;
            case "TimeRuneSO" when WorldLookup.TryFind(
                world.TimeRunes, targetId, out var timeRune):
                discovered = timeRune.Discovered;
                return true;
            case "AlchemyRecipeSO" when WorldLookup.TryFind(
                world.AlchemyRecipes, targetId, out var alchemy):
                discovered = alchemy.Discovered;
                return true;
            case "EquipmentSO" when WorldLookup.TryFind(
                world.Equipment, targetId, out var equipment):
                discovered = equipment.IsCreated;
                return true;
            default:
                discovered = false;
                return false;
        }
    }

    internal static GameMcpValue ProjectSpellRecipe(
        GameWorldState world,
        in WorldSpellRecipe recipe)
    {
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "spell-recipes",
            ["discovered"] = recipe.Discovered,
            ["masteryLevel"] = recipe.MasteryLevel,
        };
        if (recipe.Discovered)
        {
            result["masteryXp"] = new GameMcpDomainValue(recipe.MasteryXp);
            result["masteryXpNeeded"] = new GameMcpDomainValue(recipe.RequiredMasteryXp);
            result["confirmMastery"] = ProjectConfirmMastery(world, in recipe);
        }

        var holdings = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipe.EntityId) continue;
            holdings.Add(ProjectEquippedSpell(world, in slot));
        }
        if (holdings.Count > 0) result["equipped"] = holdings;

        result["loadBudget"] = ProjectSpellLoadBudget(world);
        var weight = SpellWeight(world, in recipe);
        if (weight.Count > 0) result["weight"] = SpellWeightText(world, in recipe, weight);

        var next = new JObject();
        if (recipe.Discovered)
        {
            // The row's own button is disabled on five facts, and the only one a recipe row can
            // read ahead of the call is whether the loadout has a spot. Locked, unaffordable and
            // full are three different answers. A screen the game has not unlocked draws no row at
            // all, so the loadout being empty says nothing about whether the press exists.
            var unlocked = IsScreenUnlocked(world, KnownEntities.MagicSpellbookLoadout.Uuid);
            var weightShortfall = SpellWeightShortfall(world, weight);
            var available = unlocked && world.SpellWorkbench.HasEmptySlot &&
                weightShortfall.Length == 0;
            next["available"] = available;
            next["acceptsAugments"] = true;
            if (!unlocked)
            {
                next["reasonCode"] = "screen_locked";
                next["reason"] = GameMcpDecisionReason.ScreenLocked(
                    KnownEntities.MagicSpellbookLoadout.Uuid);
            }
            else if (!world.SpellWorkbench.HasEmptySlot)
            {
                // Both sides of the ceiling, so the retry needs no second read.
                next["reason"] = "the bar is full (" +
                    world.SpellWorkbench.EquippedCount.ToString(CultureInfo.InvariantCulture) +
                    " of " +
                    world.SpellWorkbench.MaximumEquipped.ToString(CultureInfo.InvariantCulture) +
                    ")";
            }
            else if (!available)
            {
                next["reason"] = weightShortfall;
            }
            else
            {
                // The bare spell's weight is answered above. What is left for the verb reads facts
                // no recipe row carries: the weight the chosen augments add, and whether those
                // augments meet their own duration/toggle requirements and fit the augment
                // selection's slots.
                //
                // The unique-spell rule was a fourth until the world published the fact it reads:
                // every equipped instance of this recipe is on this same row under `equipped`, each
                // carrying the game's own `isLoadoutUnique`, so the gate is pre-readable and naming
                // it here would tell a caller to wait for an answer it already holds. The verb still
                // re-reads it live before it stages anything, and refuses in exactly the same words.
                next["notYetChecked"] =
                    "Augments add their own weight and requirements, which the press settles.";
            }

            // Published on both sides of the answer.
            var options = ProjectRecipeAugmentOptions(world, in recipe);
            if (options.Count > 0) next["augmentOptions"] = options;
        }
        else if (TryFindHoldingDiscoveryTree(world, recipe.EntityId, out var holdingTree))
        {
            next = HeldOfferDiscovery(world, holdingTree);
        }
        else
        {
            next = RollOnlyDiscovery(
                world,
                KnownEntities.MagicSpellbookLearn.Uuid,
                recipe.Discovery.Visible,
                MissingRecipeBook(world, recipe.EntityId),
                IsCurrentDiscoveryOffer(world, recipe.EntityId));
        }
        result[recipe.Discovered ? "loadoutAdd" : "discover"] = next;
        AddAuthoredSpellGraph(world, recipe.EntityId, result);
        return result.Freeze();
    }

    /// <summary>
    /// The spell weight the game charges to load this recipe with no augment staged, per resource.
    /// </summary>
    /// <remarks>
    /// <c>Spell.GetUsageCost()</c>: the recipe's <c>baseUsageCost</c>, plus
    /// <c>penaltyUsageCost</c> on every row while <c>HasMetUsageRequirements()</c> is false, minus
    /// the <c>usageCostReduction</c> of each type in <c>GetAllSpellTypes()</c>, floored at zero. With
    /// no glyph staged that set is the recipe's <c>notSpellTypes</c> plus its authored
    /// <c>spellTypes</c>, which <c>SetupLimitedElementalType()</c> copies into
    /// <c>augmentedSpellTypes</c>; the Cantrip type's live reduction arrives through the second. The Loadout tile prints
    /// this number, and <c>GetUsageCostOfSpell</c> is what the add gate weighs.
    /// </remarks>
    internal static List<(Guid ResourceId, BigDouble Weight)> SpellWeight(
        GameWorldState world,
        in WorldSpellRecipe recipe)
    {
        var rows = new List<(Guid, BigDouble)>();
        if (!WorldSpellGraphLookup.TryFindCosts(
                world.SpellAuthoredCosts, recipe.EntityId, out var start, out var count))
            return rows;
        var penalty = recipe.UsageRequirementsMet
            ? BigDouble.Zero
            : new BigDouble(recipe.PenaltyUsageCost);
        var reduction = SpellTypeWeightReduction(world, recipe.EntityId);
        for (var index = start; index < start + count; index++)
        {
            var cost = world.SpellAuthoredCosts[index];
            if (cost.Kind != WorldSpellAuthoredCostKind.Usage) continue;
            rows.Add((cost.ResourceId,
                BigDouble.Max(cost.Amount + penalty - reduction, BigDouble.Zero)));
        }
        return rows;
    }

    internal static BigDouble SpellTypeWeightReduction(GameWorldState world, Guid recipeId)
    {
        var reduction = BigDouble.Zero;
        if (!WorldSpellGraphLookup.TryFindRelations(
                world.SpellRelations, recipeId, out var start, out var count))
            return reduction;
        for (var index = start; index < start + count; index++)
        {
            var relation = world.SpellRelations[index];
            if (relation.Kind is WorldSpellRelationKind.NotSpellType
                    or WorldSpellRelationKind.SpellType &&
                WorldLookup.TryFind(world.SpellTypes, relation.TargetId, out var type))
                reduction += type.UsageCostReduction;
        }
        return reduction;
    }

    /// <summary>The weight a player reads under the Loadout tile, and why it is not the authored one.</summary>
    internal static string SpellWeightText(
        GameWorldState world,
        in WorldSpellRecipe recipe,
        List<(Guid ResourceId, BigDouble Weight)> weight)
    {
        var text = new System.Text.StringBuilder();
        for (var index = 0; index < weight.Count; index++)
        {
            if (index > 0) text.Append(", ");
            text.Append(GameMcpNumberFormatter.Format(weight[index].Weight)).Append(' ')
                .Append(EntityIdentityFormatter.PlayerName(
                    weight[index].ResourceId, world.EntityIdentities));
        }
        if (!recipe.UsageRequirementsMet && recipe.PenaltyUsageCost != 0)
            text.Append(", including ")
                .Append(recipe.PenaltyUsageCost.ToString(CultureInfo.InvariantCulture))
                .Append(" more until its \"use effectively\" requirements are met");
        return text.ToString();
    }

    /// <summary>
    /// Why the bare spell does not fit the weight the bar has free, with both numbers and the gap,
    /// or empty when it fits. The comparison is <c>ResourceSO.HasAmount</c>'s.
    /// </summary>
    internal static string SpellWeightShortfall(
        GameWorldState world,
        List<(Guid ResourceId, BigDouble Weight)> weight)
    {
        for (var index = 0; index < weight.Count; index++)
        {
            var (resourceId, amount) = weight[index];
            if (!WorldLookup.TryFind(world.Resources, resourceId, out var resource) ||
                WorldResourceCoordinate.HasAmount(in resource, amount)) continue;
            var free = WorldResourceCoordinate.SpendableAmount(in resource);
            return SpellWorkbenchGameAction.WeightShortfall(
                EntityIdentityFormatter.PlayerName(resourceId, world.EntityIdentities),
                amount,
                free);
        }
        return string.Empty;
    }

    /// <summary>
    /// The authored half of a spell — how it casts, what it is priced at before any modifier, and
    /// which type, glyph, and recipe book it belongs to.
    /// </summary>
    /// <remarks>
    /// The world has captured this since the spell graph reader landed and nothing read it, which
    /// is a defect in the reader rather than in the publication: it is structural, so it costs one
    /// pass per run of the game, and a caller asking "what is this spell" wants it. It rides on the
    /// detail row rather than becoming three categories of its own, because it is three facets of
    /// one entity the surface already names.
    /// </remarks>
    internal static void AddAuthoredSpellGraph(
        GameWorldState world,
        Guid recipeId,
        JObject result)
    {
        if (WorldSpellGraphLookup.TryFindAuthoring(
                world.SpellRecipeAuthoring, recipeId, out var authoring))
        {
            // Words, not the game's ordinals. The block already names this spell's type in player
            // words a few lines up, so a bare `castType: 2` beside them was the one cell a reader
            // could not use.
            var casting = new JObject
            {
                ["castType"] = GameMcpNativeVocabulary.CastType(authoring.CastType),
                // What one counted unit is worth against the recharge. `1` means one cast (or one
                // attribute developed) advances it by one, and the game's own
                // `Duration.Entry.GetMultiplier()` forces exactly 1 whenever the recharge counts in
                // time, so the key beside `rechargeCountsIn: time` is always the identity. It
                // shipped as `rechargeMultiplier: 1` with nothing saying what it multiplied.
                ["rechargeUnitMultiplier"] = authoring.RechargeMultiplier,
                // What the recharge counts down in, rather than which processor class the game
                // builds for it: `Duration.ProcessorType`'s own labels are "Time", "Number of
                // Casts" and "Attributes Developed", and that is the question a reader has.
                ["rechargeCountsIn"] = GameMcpNativeVocabulary.RechargeProcessorType(
                    authoring.RechargeProcessorType),
            };
            if (authoring.MaximumChannelBase != 0d)
                casting["maximumChannelSeconds"] = authoring.MaximumChannelBase;
            // The name carries the unit, and the game's own name for it is a trap: the field is
            // called a "rate" and `Spell.InitializePersistence` hands it straight to
            // `TickTimer(tickTime, …)` as the seconds BETWEEN re-applications, floored at 0.01. It
            // shipped as a bare `repeatEffectRate: 1`, where the one reading a player would guess —
            // once per second, which happens to be right at 1 and wrong everywhere else — is the
            // reciprocal of what it means.
            if (authoring.RepeatInstantEffectRateBase != 0d)
                casting["repeatEffectSeconds"] = authoring.RepeatInstantEffectRateBase;
            result["casting"] = casting;
        }

        if (WorldSpellGraphLookup.TryFindCosts(
                world.SpellAuthoredCosts, recipeId, out var costStart, out var costCount))
        {
            var costs = new JObject();
            for (var index = costStart; index < costStart + costCount; index++)
            {
                var cost = world.SpellAuthoredCosts[index];
                var key = cost.Kind switch
                {
                    WorldSpellAuthoredCostKind.Immediate => "cast",
                    WorldSpellAuthoredCostKind.Usage => "upkeep",
                    _ => "hold",
                };
                if (costs[key] is not JArray rows) costs[key] = rows = new JArray();
                // The resource this id resolves to carries the player's own word for it, so naming
                // it again in a column of its own printed `300 | Mana | Mana b11072` on six of six
                // rows of one round: the same read, twice, in adjacent cells.
                rows.Add(new JObject
                {
                    ["resourceId"] = cost.ResourceId.ToString("D"),
                    ["cost"] = new GameMcpDomainValue(cost.Amount),
                });
            }
            if (costs.Count > 0) result["authoredCosts"] = costs;
        }

        if (WorldSpellGraphLookup.TryFindRelations(
                world.SpellRelations, recipeId, out var relationStart, out var relationCount))
        {
            var belongsTo = new JObject();
            for (var index = relationStart; index < relationStart + relationCount; index++)
            {
                var relation = world.SpellRelations[index];

                // The core glyphs are the data's old composition shape and name ids the world
                // publishes no row for; no roll reads them. The recipe books are the pool gate, so
                // each says whether it is owned.
                if (relation.Kind == WorldSpellRelationKind.CoreGlyph) continue;
                var key = relation.Kind == WorldSpellRelationKind.RecipeBook
                    ? "recipeBooks"
                    : "spellTypes";
                if (belongsTo[key] is not JArray rows) belongsTo[key] = rows = new JArray();
                var edge = new JObject
                {
                    ["uuid"] = relation.TargetId.ToString("D"),
                    ["name"] = EntityIdentityFormatter.PlayerName(
                        relation.TargetId, world.EntityIdentities),
                };
                if (relation.Kind == WorldSpellRelationKind.RecipeBook &&
                    WorldLookup.TryFind(world.RecipeBooks, relation.TargetId, out var book))
                    edge["owned"] = book.Available;
                rows.Add(edge);
            }
            if (belongsTo.Count > 0) result["belongsTo"] = belongsTo;
        }
    }

    /// <summary>
    /// Both budgets an equipped spell is weighed against: the slots it occupies, and the spell
    /// weight it draws.
    /// </summary>
    /// <remarks>
    /// The three spot counts were the whole block, and they answer a question the player did not
    /// ask. A round removed a spell specifically to free upkeep, read <c>fitsAnotherSpell: yes</c>
    /// both before and after, and could not tell that the gate refusing its add was the other
    /// budget entirely — <c>GetUsageCostOfSpell(candidate).HasEnough()</c> against the game's spell
    /// weight resources. Those resources are named here with the headroom the gate compares
    /// against, so the number that refuses is on the same block as the number that does not.
    /// </remarks>
    internal static JObject ProjectSpellLoadBudget(GameWorldState world)
    {
        var result = new JObject
        {
            ["used"] = world.SpellWorkbench.EquippedCount,
            ["maximum"] = world.SpellWorkbench.MaximumEquipped,
            ["fitsAnotherSpell"] = world.SpellWorkbench.HasEmptySlot,
        };
        var usage = ProjectSpellUsageBudget(world);
        if (usage.Count > 0) result["usageBudget"] = usage;
        return result;
    }

    /// <summary>
    /// The augments this recipe can actually carry, with the ceiling on each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This took no recipe at all and emitted every owned augment, so the same three rows appeared
    /// under every discovered recipe on the surface — the function's definition rather than a
    /// symptom of one. The game has no per-recipe augment whitelist, but it does have two
    /// per-recipe constraints, and the world already published both halves of each without ever
    /// joining them: <c>GlyphSO.MeetsNonLvRequirements</c> refuses an augment that requires a
    /// duration unless the spell is a duration spell and one that requires a toggle unless the
    /// spell is toggled, and <c>GetMaxUsages()</c> is the per-glyph ceiling.
    /// </para>
    /// <para>
    /// The toggle half is decidable off the published cast type. The duration half is not, in one
    /// direction only: an instant-cast recipe can still be a duration spell through its own cast
    /// effects, which the world does not publish. So an augment requiring duration is dropped from
    /// no recipe, and keeps its <c>requiresDuration</c> flag, which is what a reader needs in order
    /// to see the condition that is still open. A predicate that cannot be pre-read is not
    /// predicted in either direction.
    /// </para>
    /// </remarks>
    internal static JArray ProjectRecipeAugmentOptions(
        GameWorldState world,
        in WorldSpellRecipe recipe)
    {
        var toggled = SpellIsToggled(world, recipe.EntityId);
        var options = new JArray();
        for (var index = 0; index < world.AugmentGlyphs.Count; index++)
        {
            var glyph = world.AugmentGlyphs[index];

            // Every published glyph is an Augment Glyph now, so the population check is gone with
            // the population. Gating on `augmentsSpells` would still be wrong: it drops Distinct,
            // Weak and Wrath, three augments a player holds.
            if (!glyph.Learned || glyph.Level <= 0) continue;
            if (glyph.RequiresToggleable && !toggled) continue;
            if (glyph.MaximumUsages <= 0) continue;
            var option = new JObject
            {
                ["glyphId"] = glyph.GlyphId.ToString("D"),
                ["ownedLevel"] = glyph.Level,
                ["slots"] = glyph.MaximumUsages,
                ["masteryRequirement"] = glyph.MasteryReqCount,
            };
            if (glyph.FreeLevels != 0) option["bonusLevel"] = glyph.FreeLevels;
            if (glyph.RequiresDuration && !toggled) option["requiresDuration"] = true;
            options.Add(option);
        }
        return options;
    }

}
#endif
