#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The sole MCP category/native-type/capability registry. Read discovery and gameplay admission both
/// consume these descriptors, so a UUID cannot be accepted as one native type by <c>world_get</c> and
/// rejected under a second hand-written type/category map by an action.
/// </summary>
internal static class GameMcpEntityCapabilityMap
{
    private static readonly GameMcpEntityCapabilityDescriptor[] Descriptors = Create();
    private static readonly Dictionary<string, GameMcpEntityCapabilityDescriptor> ByCategory = Index();

    internal static IReadOnlyList<GameMcpEntityCapabilityDescriptor> Entries => Descriptors;

    internal static bool TryDescriptor(string category, out GameMcpEntityCapabilityDescriptor descriptor) =>
        ByCategory.TryGetValue(category, out descriptor!);

    internal static string ExpectedNativeType(string category)
    {
        if (!ByCategory.TryGetValue(category, out var descriptor))
            throw new InvalidOperationException(
                "MCP world category '" + category + "' has no entity-capability descriptor");
        return descriptor.ExpectedNativeType;
    }

    /// <summary>
    /// The category that publishes rows of one native type, where exactly one does.
    /// </summary>
    /// <remarks>
    /// A descriptor naming several native types answers only for a type no single-type descriptor
    /// claims, and only where it is the sole such claimant. Skipping those descriptors outright
    /// left <c>AlchemySnapshotListVariable</c> and <c>EquipmentSnapshotListVariable</c> reading as
    /// projected by nothing while <c>world_list snapshot-loadouts</c> paged them — the answer
    /// saying an id has no home was the only one that could not see it.
    /// The single-type pass still wins, because <c>EquipmentSO</c> is <c>equipment</c> whatever
    /// composite rows also name it, and a type several composites claim keeps no answer at all
    /// rather than the first one declared.
    /// </remarks>
    internal static bool TryCategoryForNativeType(string nativeType, out string category)
    {
        for (var index = 0; index < Descriptors.Length; index++)
        {
            var descriptor = Descriptors[index];
            if (descriptor.ExpectedNativeType.IndexOf('|') >= 0 ||
                !string.Equals(descriptor.ExpectedNativeType, nativeType, StringComparison.Ordinal))
            {
                continue;
            }
            category = descriptor.Category;
            return true;
        }

        category = string.Empty;
        var claimed = 0;
        for (var index = 0; index < Descriptors.Length; index++)
        {
            var descriptor = Descriptors[index];
            if (descriptor.ExpectedNativeType.IndexOf('|') < 0 ||
                !Names(descriptor.ExpectedNativeType, nativeType))
            {
                continue;
            }
            if (++claimed > 1)
            {
                category = string.Empty;
                return false;
            }
            category = descriptor.Category;
        }
        return claimed == 1;
    }

    /// <summary>Whether a pipe-separated descriptor names this native type as one of its members.</summary>
    private static bool Names(string descriptor, string nativeType)
    {
        var start = 0;
        while (start <= descriptor.Length)
        {
            var next = descriptor.IndexOf('|', start);
            var end = next < 0 ? descriptor.Length : next;
            if (end - start == nativeType.Length &&
                string.CompareOrdinal(descriptor, start, nativeType, 0, nativeType.Length) == 0)
            {
                return true;
            }
            if (next < 0) return false;
            start = next + 1;
        }
        return false;
    }

    internal static bool Contains(
        GameWorldState world,
        Guid target,
        GameMcpCommandKind capability,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (target == Guid.Empty && capability is not (
                GameMcpCommandKind.SpellLevel or GameMcpCommandKind.SpellComposition or GameMcpCommandKind.Targeting or
                GameMcpCommandKind.Challenge or GameMcpCommandKind.Prestige))
        {
            reason = "This needs the id of the thing to act on, and the all-zero id names nothing.";
            return false;
        }

        return capability switch
        {
            GameMcpCommandKind.Purchase => PurchaseTarget(world, target, out reason),
            GameMcpCommandKind.SpellLevel when target == Guid.Empty =>
                AvailableGlobal(world.SpellRecipes.Count > 0, "The spell recipe list", out reason),
            GameMcpCommandKind.Cast or GameMcpCommandKind.SpellLevel or GameMcpCommandKind.SpellWorkbench =>
                Entity(
                    world.EntityIdentities,
                    world.SpellRecipes,
                    target,
                    "spell-recipes",
                    capability,
                    out reason),
            GameMcpCommandKind.Concept =>
                Entity(
                    world.EntityIdentities,
                    world.AlchemyRecipes,
                    target,
                    GameMcpWorldCategories.ConceptsCategory,
                    capability,
                    out reason),
            GameMcpCommandKind.Harvest => HarvestTarget(world, target, out reason),
            GameMcpCommandKind.DiscoveryTreeOffer =>
                Entity(
                    world.EntityIdentities,
                    world.DiscoveryTrees,
                    target,
                    "discovery-trees",
                    capability,
                    out reason),
            GameMcpCommandKind.SpellComposition => SpellCompositionTarget(world, target, out reason),
            GameMcpCommandKind.SpellLoadout => SpellLoadoutTarget(world, target, out reason),
            GameMcpCommandKind.Targeting => TargetingTarget(world, target, out reason),
            GameMcpCommandKind.Consumable =>
                Entity(
                    world.EntityIdentities,
                    world.Consumables,
                    target,
                    "consumables",
                    capability,
                    out reason),
            GameMcpCommandKind.Crafting =>
                Entity(
                    world.EntityIdentities,
                    world.CraftingRecipes,
                    target,
                    "crafting-recipes",
                    capability,
                    out reason),
            GameMcpCommandKind.EquipmentLoadout =>
                Entity(world.EntityIdentities, world.Equipment, target, "equipment", capability, out reason),
            GameMcpCommandKind.Challenge when target == Guid.Empty =>
                AvailableGlobal(world.ChallengeContext.Available, "The challenge decision", out reason),
            GameMcpCommandKind.Challenge =>
                Entity(world.EntityIdentities, world.Challenges, target, "challenges", capability, out reason),
            GameMcpCommandKind.Prestige =>
                AvailableGlobal(world.ChallengeContext.Available, "The prestige decision", out reason),
            GameMcpCommandKind.Research =>
                Entity(world.EntityIdentities, world.Research, target, "research", capability, out reason),
            GameMcpCommandKind.AlchemyLoadout =>
                Entity(world.EntityIdentities, world.AlchemyRecipes, target,
                    "alchemy-recipes", capability, out reason),
            GameMcpCommandKind.RitualLifecycle =>
                Entity(world.EntityIdentities, world.Rituals, target,
                    "rituals", capability, out reason),
            GameMcpCommandKind.GenericLevel =>
                TryResolveGenericLevelType(world, target, out _, out reason),
            GameMcpCommandKind.Loadout =>
                TryResolveLoadoutType(world, target, out _, out reason),
            GameMcpCommandKind.HarvestLifecycle =>
                Entity(world.EntityIdentities, world.HarvestElements, target,
                    "agromancy-elements", capability, out reason),
            GameMcpCommandKind.StructureLifecycle =>
                Entity(world.EntityIdentities, world.Structures, target,
                    "attributes", capability, out reason),
            _ => Unsupported(out reason),
        };
    }

    internal static bool TryResolveLoadoutType(
        GameWorldState world,
        Guid target,
        out string nativeType,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (Supports("player-loadouts", GameMcpCommandKind.Loadout) &&
            WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts, target, out _))
        {
            nativeType = GameMcpLoadoutCommand.PlayerLoadoutType;
            reason = string.Empty;
            return true;
        }
        if (Supports("snapshot-loadouts", GameMcpCommandKind.Loadout) &&
            WorldLoadoutLookup.TryFindSnapshot(world.SnapshotLoadouts, target, out var snapshot))
        {
            nativeType = GameMcpLoadoutCommand.SnapshotListType(
                snapshot.Kind == WorldSnapshotLoadoutKind.Alchemy);
            reason = string.Empty;
            return true;
        }
        nativeType = string.Empty;
        reason = "That is not one of your loadouts or saved loadout lists.";
        return false;
    }

    internal static bool TryResolveGenericLevelType(
        GameWorldState world,
        Guid target,
        out string nativeType,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        nativeType = string.Empty;
        var matches = 0;
        if (Supports("equipment-types", GameMcpCommandKind.GenericLevel) &&
            WorldLookup.TryFind(world.EquipmentTypes, target, out _))
        {
            matches++;
            nativeType = "EquipmentTypeSO";
        }
        if (Supports("augment-glyphs", GameMcpCommandKind.GenericLevel) &&
            WorldLookup.TryFind(world.AugmentGlyphs, target, out _))
        {
            // The rule this whole resolver exists for: a level offer stands only where the game
            // draws a level button. `GlyphSO.CanLevel()` is `ldc.i4.1; ret` — the constant true — so
            // asking the interface would admit every augment at every moment, including the whole
            // game before Magic > Augments > Upgrade is bought and the panel exists at all.
            if (!GameMcpScreenState.IsScreenUnlocked(world, KnownEntities.MagicGlyphsUpgrade.Uuid))
            {
                reason = EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
                    " has no level button yet: Magic > Augments > Upgrade is locked until the " +
                    "Upgrade Glyphs upgrade is bought.";
                nativeType = string.Empty;
                return false;
            }
            matches++;
            nativeType = "GlyphSO";
        }
        if (Supports("resource-types", GameMcpCommandKind.GenericLevel) &&
            WorldLookup.TryFind(world.ResourceTypes, target, out _))
        {
            matches++;
            nativeType = "ResourceTypeSO";
        }
        if (Supports("time-runes", GameMcpCommandKind.GenericLevel) &&
            WorldLookup.TryFind(world.TimeRunes, target, out _))
        {
            matches++;
            nativeType = "TimeRuneSO";
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        // A caller holding one of the twenty-five retired unlocker ids asked for a level, and a
        // round spent a currency because the wire said yes. The refusal names what the id is and the
        // one purchase that does what the caller wanted.
        if (matches == 0 &&
            WorldRecipeBookGlyphLookup.TryFindBook(world.RecipeBookGlyphs, target, out var book))
        {
            reason = EntityIdentityFormatter.PlayerName(book, world.EntityIdentities) +
                " is a Recipe Book — there is nothing to level. It is owned by buying " +
                (GameMcpRecipeBookProjection.TryNameBookPurchase(world, book, out var purchase)
                    ? purchase
                    : "its one prerequisite") + ".";
            nativeType = string.Empty;
            return false;
        }
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is not something the game gives a level."
            : EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " matches " + matches + " different things that have levels, so this cannot tell " +
              "which one you mean.";
        nativeType = string.Empty;
        return false;
    }

    /// <summary>
    /// Whether the game has one target selection waiting for input. Nothing pending is a fact
    /// about the screen, not about the submitted target, so it earns its own refusal code.
    /// </summary>
    internal static bool HasPendingTargetSelection(GameWorldState world) =>
        world is not null && Supports("targeting", GameMcpCommandKind.Targeting) &&
        world.Targeting.Count == 1;

    private static bool TargetingTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!HasPendingTargetSelection(world))
        {
            reason = "There is no target selection waiting for input.";
            return false;
        }
        if (target == Guid.Empty) { reason = string.Empty; return true; }
        var candidates = world.Targeting[0].Candidates;
        var matches = 0;
        for (var index = 0; index < candidates.Count; index++)
            if (candidates[index].StructureId == target) matches++;
        if (matches == 1) { reason = string.Empty; return true; }
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is not one of the places this target picker accepts."
            : "That place is offered more than once by the target picker, so this cannot tell " +
              "which one you mean.";
        return false;
    }

    private static bool SpellLoadoutTarget(
        GameWorldState world,
        Guid target,
        out string reason)
    {
        if (!Supports("spell-slots", GameMcpCommandKind.SpellLoadout))
        {
            reason = "Equipped spells are not something these tools can change.";
            return false;
        }
        var matches = 0;
        for (var index = 0; index < world.SpellSlots.Count; index++)
            if (world.SpellSlots[index].Occupied &&
                world.SpellSlots[index].SpellInstanceId == target)
                matches++;
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is not one of the spells you have equipped."
            : EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is equipped in " + matches + " slots, so this cannot tell which one you mean.";
        return false;
    }

    private static bool SpellCompositionTarget(
        GameWorldState world,
        Guid target,
        out string reason)
    {
        if (target == Guid.Empty)
        {
            if (world.SpellWorkbench.MaximumOutputLevel > 0)
            {
                reason = string.Empty;
                return true;
            }
            reason = "The game has not set up its level dials yet.";
            return false;
        }
        var matches = 0;
        for (var index = 0; index < world.SpellSlots.Count; index++)
            if (world.SpellSlots[index].Occupied &&
                world.SpellSlots[index].SpellInstanceId == target)
                matches++;
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is not one of the spells you have equipped."
            : EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is equipped in " + matches + " slots, so this cannot tell which one you mean.";
        return false;
    }

    internal static bool Supports(string category, GameMcpCommandKind capability) =>
        ByCategory.TryGetValue(category, out var descriptor) && descriptor.Supports(capability);

    /// <summary>
    /// The verbs this entity's own screen offers it, named the way a caller would call them.
    /// </summary>
    /// <remarks>
    /// Where more than one verb stands the answer names them all rather than picking.
    /// </remarks>
    internal static bool TryOwningTool(
        GameWorldState world,
        Guid target,
        out string category,
        out string nativeType,
        out string tool)
    {
        category = string.Empty;
        nativeType = string.Empty;
        tool = string.Empty;
        if (target == Guid.Empty || !world.EntityIdentities.TryGet(target, out var identity))
            return false;
        nativeType = identity.RuntimeType;
        if (!TryCategoryForNativeType(nativeType, out category)) return false;

        // Two player categories share `AlchemyRecipeSO`, so the class alone cannot say which of
        // them a recipe is in — and the two offer different verbs. The recipe's own core alchemy
        // type says it, which is the same fact the row's category cell is written from.
        if (WorldLookup.TryFind(world.AlchemyRecipes, target, out var recipe))
            category = GameMcpWorldCategories.AlchemyRecipeCategory(recipe.CoreTypeId);
        if (!ByCategory.TryGetValue(category, out var descriptor)) return false;
        var names = new List<string>();
        for (var index = 0; index < descriptor.Capabilities.Count; index++)
        {
            var capability = descriptor.Capabilities[index];
            var candidate = GameMcpToolCatalog.Shipping.ToolFor(capability);
            if (candidate.Length == 0 || names.Contains(candidate)) continue;
            names.Add(candidate);
        }
        tool = names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => Join(names),
        };
        return true;
    }

    /// <summary>Several verbs, in one phrase a caller reads as a list rather than a choice.</summary>
    private static string Join(List<string> names)
    {
        var text = new StringBuilder();
        for (var index = 0; index < names.Count; index++)
        {
            if (index > 0) text.Append(index == names.Count - 1 ? " and " : ", ");
            text.Append(names[index]);
        }
        return text.ToString();
    }

    private static bool PurchaseTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!Supports("attributes", GameMcpCommandKind.Purchase) ||
            !Supports("upgrades", GameMcpCommandKind.Purchase))
        {
            reason = "Structures and upgrades are not something these tools can buy.";
            return false;
        }
        var structure = WorldLookup.TryFind(world.Structures, target, out _);
        var upgrade = WorldLookup.TryFind(world.Upgrades, target, out _);
        if (structure != upgrade)
        {
            reason = string.Empty;
            return true;
        }
        reason = structure
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " names both a structure and an upgrade, so this cannot tell which one you mean."
            : EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is neither a structure nor an upgrade you can buy.";
        return false;
    }

    private static bool HarvestTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!Supports("plot-nodes", GameMcpCommandKind.Harvest))
        {
            reason = "Plots are not something these tools can harvest.";
            return false;
        }
        if (!WorldLookup.TryFind(world.PlotNodes, target, out _))
        {
            reason = EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
                " is not one of your plots.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool Entity<TRow>(
        EntityIdentityCatalogSnapshot identities,
        PublicationTable<TRow> table,
        Guid target,
        string category,
        GameMcpCommandKind capability,
        out string reason)
        where TRow : struct, IWorldEntity
    {
        if (!Supports(category, capability))
        {
            reason = "Nothing in " + category + " supports that action.";
            return false;
        }
        if (WorldLookup.TryFind(table, target, out _))
        {
            reason = string.Empty;
            return true;
        }
        reason = EntityIdentityFormatter.PlayerName(target, identities) +
            " is not in " + category + ".";
        return false;
    }

    private static bool Unsupported(out string reason)
    {
        reason = "That action does not act on anything the game shows in a list.";
        return false;
    }

    private static bool AvailableGlobal(bool available, string label, out string reason)
    {
        reason = available ? string.Empty : label + " is not available right now.";
        return available;
    }

    private static Dictionary<string, GameMcpEntityCapabilityDescriptor> Index()
    {
        var result = new Dictionary<string, GameMcpEntityCapabilityDescriptor>(StringComparer.Ordinal);
        for (var index = 0; index < Descriptors.Length; index++)
            result.Add(Descriptors[index].Category, Descriptors[index]);
        return result;
    }

    private static GameMcpEntityCapabilityDescriptor[] Create() => new[]
    {
        Entity(
            "resources", nameof(GameWorldState.Resources), "ResourceSO",
            world => world.Resources,
            reports: new[] { "resources" },
            scan: new[] { "entityId", "reading.visible", "reading.quantity", "reading.capacity", "reading.rate", "reading.usage", "reading.reservation", "trueQuantity", "trueRate", "fillFraction", "isCapped", "isAtCapacity", "headroom" },
            columns: new[] { "entityId", "category", "meter", "amount", "capacity", "netRatePerSecond", "atCapacity", "inLedger" },
            failureReports: new[] { "modifier-folding" },
            projector: static (world, category, row) =>
                GameMcpResourceProjection.ProjectResource(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpResourceProjection.List(world, in row, offset, limit)),
        Entity(
            "attributes", nameof(GameWorldState.Structures), "StructureSO",
            world => world.Structures,
            reports: new[] { "attributes" },
            scan: new[] { "entityId", "reading.unlocked", "reading.level", "reading.queuedLevels", "effectiveLevel", "hasWorkInFlight", "developmentProgress", "reading.insufficientReqPenaltyActive" },
            columns: new[] { "entityId", "level", "queuedLevels", "state", "enabled", "affordable" },
            projector: static (world, category, row) =>
                GameMcpStructureLifecycleProjection.ProjectStructure(in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpStructureLifecycleProjection.List(world, in row, offset, limit),
            affordable: static (world, row) => row.Reading.Unlocked &&
                GameMcpPurchaseProjection.TryPurchaseAffordability(world, row.EntityId, out var value) && value,
            capabilities: new[] { GameMcpCommandKind.Purchase, GameMcpCommandKind.Targeting, GameMcpCommandKind.StructureLifecycle }),
        Entity(
            "upgrades", nameof(GameWorldState.Upgrades), "UpgradeSO",
            world => world.Upgrades,
            reports: new[] { "upgrades" },
            scan: new[] { "entityId", "reading.available", "reading.level", "reading.maxLevel", "reading.queuedLevels", "remainingLevels", "isExhausted", "isDeveloping", "developmentProgress" },
            columns: new[] { "entityId", "level", "queuedLevels", "screen", "state", "maximum", "nextLevelRequirements", "affordable" },
            projector: static (world, category, row) =>
                GameMcpUpgradeProjection.ProjectUpgrade(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpUpgradeProjection.List(world, in row, offset, limit),
            affordable: static (world, row) => !row.IsExhausted && GameMcpScreenState.IsUpgradeOffered(world, in row) &&
                GameMcpPurchaseProjection.TryPurchaseAffordability(world, row.EntityId, out var value) && value,
            capabilities: new[] { GameMcpCommandKind.Purchase }),
        Entity(
            "research", nameof(GameWorldState.Research), "ResearchSO",
            world => world.Research,
            reports: new[] { "research" },
            scan: new[] { "entityId", "level", "queuedLevels", "maxLevel", "researchStage", "available", "visible", "complete", "canDevelop", "withinDevelopRange", "meetsLevelRequirements", "stillHasLeeway", "belowArtificialMaxLevel", "belowMaxInvestmentLevel", "isActive", "isDeveloping", "flagged", "purchasedLevels", "baseLevel", "bonusLevel", "totalLevel", "artificialMaxLevel", "baseRequirementLevel", "effectiveRequirementLevel", "requirementLevelAdjustment", "requirementAdjustments" },
            columns: new[] { "entityId", "state", "paused", "totalLevel", "queuedLevels", "nextLevelRequirements", "canDevelop", "affordable" },
            projector: static (world, category, row) =>
                GameMcpResearchProjection.ProjectResearch(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpResearchProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Research }),
        Entity(
            "double-variables", nameof(GameWorldState.DoubleVariables), "DoubleVariable",
            world => world.DoubleVariables,
            reports: new[] { "double-variables" },
            scan: new[] { "entityId", "value", "isPercent" },
            columns: new[] { "entityId", "value", "isPercent" }),
        Entity(
            "int-variables", nameof(GameWorldState.IntVariables), "IntVariable",
            world => world.IntVariables,
            reports: new[] { "int-variables" },
            scan: new[] { "entityId", "value", "isPercent" },
            columns: new[] { "entityId", "value", "isPercent" }),
        Entity(
            "bool-variables", nameof(GameWorldState.BoolVariables), "BoolVariable",
            world => world.BoolVariables,
            reports: new[] { "bool-variables" },
            scan: new[] { "entityId", "value", "initialValue", "isSaved" },
            columns: new[] { "entityId", "value" }),
        Entity(
            "modifier-variables", nameof(GameWorldState.ModifierVariables), "ValueModifierVariable",
            world => world.ModifierVariables,
            reports: new[] { "modifier-variables" },
            scan: new[] { "entityId", "amount", "order" },
            columns: new[] { "entityId", "amount" }),
        Entity(
            "statistics", nameof(GameWorldState.Statistics), "AttributeSO",
            world => world.Statistics,
            reports: new[] { "statistics" },
            scan: new[] { "entityId", "displayType", "isPercent" },
            columns: new[] { "entityId", "displayType", "isPercent", "description" }),
        Entity(
            "attribute-groups", nameof(GameWorldState.AttributeGroups), "AttributeGroupSO",
            world => world.AttributeGroups,
            reports: new[] { "attribute-groups", "attribute-group-members" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Entity(
            "status-effects", nameof(GameWorldState.StatusEffects), "CombatStatusSO",
            world => world.StatusEffects,
            reports: new[] { "status-effects" },
            scan: new[] { "entityId", "isBuff", "maxDuration", "stacksSeparately", "resetDurationOnApplication", "effectTimer" },
            columns: new[] { "entityId", "isBuff", "maxDuration", "stacksSeparately", "description" }),
        Entity(
            "character-attributes", nameof(GameWorldState.CharacterAttributes), "CharacterAttributeSO",
            world => world.CharacterAttributes,
            reports: new[] { "character-attributes" },
            scan: new[] { "entityId", "damageTypeId" },
            columns: new[] { "entityId", "damageTypeId", "description" }),
        Entity(
            "damage-types", nameof(GameWorldState.DamageTypes), "DamageTypeSO",
            world => world.DamageTypes,
            reports: new[] { "damage-types" },
            scan: new[] { "entityId", "damageReductionRate", "ignoreEntrenched" },
            columns: new[] { "entityId", "damageReductionRate", "ignoreEntrenched", "description" }),
        Entity(
            "character-modifiers", nameof(GameWorldState.CharacterModifiers), "CharacterModifierSO",
            world => world.CharacterModifiers,
            reports: new[] { "character-modifiers" },
            scan: new[] { "entityId", "weightChance" },
            columns: new[] { "entityId", "weightChance", "description" }),
        Entity(
            "character-actions", nameof(GameWorldState.CharacterActions), "CharacterActionSO",
            world => world.CharacterActions,
            reports: new[] { "character-actions" },
            scan: new[] { "entityId", "prepTime", "actTime", "speedMod" },
            columns: new[] { "entityId", "prepTime", "actTime", "speedMod", "description" }),
        Entity(
            "character-types", nameof(GameWorldState.CharacterTypes), "CharacterTypeSO",
            world => world.CharacterTypes,
            reports: new[] { "character-types" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Entity(
            "enchantments", nameof(GameWorldState.Enchantments), "EnchantmentSO",
            world => world.Enchantments,
            reports: new[] { "enchantments" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Entity(
            "glyph-types", nameof(GameWorldState.GlyphTypes), "GlyphTypeSO",
            world => world.GlyphTypes,
            reports: new[] { "glyph-types" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Entity(
            "rune-stones", nameof(GameWorldState.RuneStones), "RuneStoneSO",
            world => world.RuneStones,
            reports: new[] { "rune-stones" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Entity(
            "display-types", nameof(GameWorldState.DisplayTypes), "DisplayTypeSO",
            world => world.DisplayTypes,
            reports: new[] { "display-types" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId", "description" }),
        Composite(
            "purchase-costs", nameof(GameWorldState.PurchaseCosts), "StructureSO|UpgradeSO",
            world => world.PurchaseCosts,
            reports: new[] { "attributes", "upgrades", "resources", "modifier-variables", "int-variables", "structure-costs", "upgrade-costs" },
            scan: new[] { "entityId", "resourceId", "baseExactAmount", "effectiveExactAmount", "amount", "exactGroupedLevels", "exactGroupedAmount", "modifierSources", "affordabilityEvaluated", "availableAmount", "combinedEffectiveAmount", "resourceAffordable", "resourceAffordabilityReasonCode", "affordable", "affordabilityReasonCode" },
            columns: new[] { "resourceId", "cost", "spendableAmount", "affordable", "targetId" },
            failureReports: new[] { "modifier-folding" },
            projector: static (world, category, row) =>
                GameMcpDecisionReason.ProjectPurchaseCost(world, in row).Freeze(),
            listProjector: static (world, row, offset, limit) =>
                GameMcpDecisionReason.List(world, in row, offset, limit)),
        Entity(
            "alchemy-recipes", nameof(GameWorldState.AlchemyRecipes), "AlchemyRecipeSO",
            world => world.AlchemyRecipes,
            reports: new[] { "alchemy-recipes" },
            scan: new[] { "entityId", "coreTypeId", "discovered", "masteryLevel", "masteryXp", "maxLevel", "advancementLevel" },
            columns: new[] { "entityId", "state", "masteryLevel" },
            projector: static (world, category, row) =>
                GameMcpAlchemyLoadoutProjection.ProjectAlchemyRecipe(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpAlchemyLoadoutProjection.List(world, in row, offset, limit),
            admits: static row => !GameMcpWorldCategories.IsScholarConcept(row.CoreTypeId),
            capabilities: new[] { GameMcpCommandKind.AlchemyLoadout }),
        Entity(
            "concepts", nameof(GameWorldState.AlchemyRecipes), "AlchemyRecipeSO",
            world => world.AlchemyRecipes,
            reports: new[] { "alchemy-recipes" },
            scan: new[] { "entityId", "coreTypeId", "discovered", "masteryLevel", "masteryXp", "maxLevel", "advancementLevel" },
            columns: new[] { "entityId", "state", "masteryLevel" },
            projector: static (world, category, row) =>
                GameMcpAlchemyLoadoutProjection.ProjectAlchemyRecipe(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpAlchemyLoadoutProjection.List(world, in row, offset, limit),
            admits: static row => GameMcpWorldCategories.IsScholarConcept(row.CoreTypeId),
            capabilities: new[] { GameMcpCommandKind.Concept }),
        Entity(
            "alchemy-types", nameof(GameWorldState.AlchemyTypes), "AlchemyTypeSO",
            world => world.AlchemyTypes,
            reports: new[] { "alchemy-types" },
            scan: new[] { "entityId", "selectedLevelId", "level", "maxUsageByMastery" },
            columns: new[] { "entityId", "selectedLevelId" }),
        Entity(
            "spell-recipes", nameof(GameWorldState.SpellRecipes), "SpellRecipeSO",
            world => world.SpellRecipes,
            reports: new[] { "spell-recipes" },
            scan: new[] { "entityId", "discovered", "masteryLevel", "masteryLevelReady", "masteryXp", "baseCharges", "castSpeed" },
            columns: new[] { "entityId", "masteryLevel", "discovered" },
            projector: static (world, category, row) =>
                GameMcpSpellWorkbenchProjection.ProjectSpellRecipe(world, in row),
            discovered: static row => row.Discovered,
            capabilities: new[] { GameMcpCommandKind.Cast, GameMcpCommandKind.SpellLevel, GameMcpCommandKind.SpellWorkbench }),
        Entity(
            "spell-types", nameof(GameWorldState.SpellTypes), "SpellTypeSO",
            world => world.SpellTypes,
            reports: new[] { "spell-types" },
            scan: new[] { "entityId", "typeLevel", "typeXp", "isVisible", "isElemental", "isLoadoutUnique" },
            columns: new[] { "entityId", "typeLevel" }),
        Entity(
            "equipment", nameof(GameWorldState.Equipment), "EquipmentSO",
            world => world.Equipment,
            reports: new[] { "equipment" },
            scan: new[] { "entityId", "isCreated", "masteryLevel", "masteryXp", "equippedLevel", "attuningLevel", "attunementTimeLeft" },
            columns: new[] { "entityId", "created", "equippedCount" },
            projector: static (world, category, row) =>
                GameMcpEquipmentLoadoutProjection.ProjectEquipment(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpEquipmentLoadoutProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.EquipmentLoadout }),
        Entity(
            "equipment-types", nameof(GameWorldState.EquipmentTypes), "EquipmentTypeSO",
            world => world.EquipmentTypes,
            reports: new[] { "equipment-types" },
            scan: new[] { "entityId", "level", "freeLevels", "baseUsage", "masteryLevel", "maxTypeSlots" },
            columns: new[] { "entityId", "totalLevel" },
            projector: static (world, category, row) =>
                GameMcpEquipmentLoadoutProjection.ProjectEquipmentType(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpEquipmentLoadoutProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.GenericLevel }),
        Entity(
            "resource-types", nameof(GameWorldState.ResourceTypes), "ResourceTypeSO",
            world => world.ResourceTypes,
            reports: new[] { "resource-types" },
            scan: new[] { "entityId", "level", "freeLevels", "specialHidden" },
            columns: new[] { "entityId", "totalLevel", "hidden" },
            projector: static (world, category, row) =>
                GameMcpResourceProjection.ProjectResourceType(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpResourceProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.GenericLevel }),
        Entity(
            "crafting-recipe-types", nameof(GameWorldState.CraftingRecipeTypes), "CraftingRecipeTypeSO",
            world => world.CraftingRecipeTypes,
            reports: new[] { "crafting-recipe-types" },
            scan: new[] { "entityId", "startingLevel", "maxStartingLevel", "craftVerb", "initiated" },
            columns: new[] { "entityId", "startingLevel" }),
        Entity(
            "structure-types", nameof(GameWorldState.StructureTypes), "StructureTypeSO",
            world => world.StructureTypes,
            reports: new[] { "structure-types" },
            scan: new[] { "entityId", "baseEffectLevel", "baseBuildTime", "overrideRankDefault", "overrideRank" },
            columns: new[] { "entityId", "baseEffectLevel" }),
        Entity(
            "ritual-types", nameof(GameWorldState.RitualTypes), "RitualTypeSO",
            world => world.RitualTypes,
            reports: new[] { "ritual-types" },
            scan: new[] { "entityId", "initiated", "activeRituals" },
            columns: new[] { "entityId", "initiated" }),
        Entity(
            "agromancy-element-types", nameof(GameWorldState.HarvestTypes), "HarvestTypeSO",
            world => world.HarvestTypes,
            reports: new[] { "harvest-types" },
            scan: new[] { "entityId", "level" },
            columns: new[] { "entityId", "level" }),
        Entity(
            "plot-node-types", nameof(GameWorldState.PlotNodeTypes), "PlotNodeTypeSO",
            world => world.PlotNodeTypes,
            reports: new[] { "plot-node-types" },
            scan: new[] { "entityId", "totalLevel" },
            columns: new[] { "entityId", "totalLevel" }),
        Entity(
            "research-types", nameof(GameWorldState.ResearchTypes), "ResearchTypeSO",
            world => world.ResearchTypes,
            reports: new[] { "research-types" },
            scan: new[] { "entityId", "linkedDevelopCost", "linkedResourceCost", "linkedResearchTime", "ignoreWhenLevelDependent", "persistThroughReset", "cachedTotalLevel", "cachedPeakLevel", "cachedQueuedLevel", "cachedDevelopingLevel", "cachedQueuedValue", "cachedInvestmentLevel", "cachedPurchasedLevel", "freeBonusLevels", "usedBonusLevels", "maxInvestmentLevel" },
            columns: new[] { "entityId", "linkedDevelopCost" }),
        Entity(
            "consumable-types", nameof(GameWorldState.ConsumableFamilies), "ConsumableTypeSO",
            world => world.ConsumableFamilies,
            reports: new[] { "consumable-families" },
            scan: new[] { "entityId", "hidden", "sortOrder" },
            columns: new[] { "entityId", "hidden" }),
        Entity(
            "plot-node-action-types", nameof(GameWorldState.HarvestActionTypes), "HarvestActionTypeSO",
            world => world.HarvestActionTypes,
            reports: new[] { "harvest-action-types" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId" }),
        Entity(
            "passive-ability-types", nameof(GameWorldState.PassiveAbilityTypes), "PassiveAbilityTypeSO",
            world => world.PassiveAbilityTypes,
            reports: new[] { "passive-ability-types" },
            scan: new[] { "entityId" },
            columns: new[] { "entityId" }),
        Entity(
            "time-rune-types", nameof(GameWorldState.TimeRuneTypes), "TimeRuneTypeSO",
            world => world.TimeRuneTypes,
            reports: new[] { "time-rune-types" },
            scan: new[] { "entityId", "initialized", "totalLevel" },
            columns: new[] { "entityId", "initialized" }),
        Entity(
            "crafting-recipes", nameof(GameWorldState.CraftingRecipes), "CraftingRecipeSO",
            world => world.CraftingRecipes,
            reports: new[] { "crafting-recipes", "crafting-recipe-state", "crafting-decisions", "resources" },
            scan: new[] { "entityId", "reading.visible", "reading.visibilityReasonCode", "reading.canBuyAtStartingQuantity", "reading.nativePurchaseReasonCode", "reading.startingQuantity", "reading.outputWithinCapacity", "reading.outputCapacityReasonCode" },
            columns: new[] { "entityId", "startingAmount" },
            projector: static (world, category, row) =>
                GameMcpCraftingProjection.ProjectCraftingRecipe(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpCraftingProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Crafting }),
        Composite(
            "crafting-queue-entries", nameof(GameWorldState.CraftingQueueEntries), "CraftingInstance",
            world => world.CraftingQueueEntries,
            reports: new[] { "crafting-decisions" },
            scan: new[] { "queueId", "slot", "recipeId", "amount", "automatic", "repetitions" },
            columns: new[] { "queueId", "slot", "recipeId", "amount", "repetitions" },
            listProjector: static (world, row, offset, limit) =>
                GameMcpCraftingProjection.List(world, in row, offset, limit)),
        Entity(
            "player-loadouts", nameof(GameWorldState.PlayerLoadouts), "PlayerLoadout",
            world => world.PlayerLoadouts,
            reports: new[] { "loadouts" },
            scan: new[] { "entityId", "name", "selected", "savesEquipment", "savesAlchemy", "icon", "color", "canSwitchNow" },
            columns: new[] { "name", "selected" },
            projector: static (world, category, row) =>
                GameMcpLoadoutProjection.ProjectPlayerLoadout(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpLoadoutProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Loadout }),
        Composite(
            "player-loadout-entries", nameof(GameWorldState.PlayerLoadoutEntries), "Spell|EquipmentSO|AlchemyRecipeSO",
            world => world.PlayerLoadoutEntries,
            reports: new[] { "loadouts" },
            scan: new[] { "ownerId", "kind", "entryId", "referenceId", "quantity" },
            columns: new[] { "ownerId", "kind", "entryId", "referenceId" }),
        Entity(
            "snapshot-loadouts", nameof(GameWorldState.SnapshotLoadouts), "AlchemySnapshotListVariable|EquipmentSnapshotListVariable",
            world => world.SnapshotLoadouts,
            reports: new[] { "loadouts" },
            scan: new[] { "entityId", "kind", "slots" },
            columns: new[] { "name", "kind", "slots" },
            projector: static (world, category, row) =>
                GameMcpLoadoutProjection.ProjectSnapshotLoadout(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpLoadoutProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Loadout }),
        Composite(
            "snapshot-slots", nameof(GameWorldState.SnapshotSlots), "AlchemySnapshot|EquipmentSnapshot",
            world => world.SnapshotSlots,
            reports: new[] { "loadouts" },
            scan: new[] { "ownerId", "slot", "populated" },
            columns: new[] { "ownerId", "slot", "populated" },
            listProjector: static (world, row, offset, limit) =>
                GameMcpLoadoutProjection.List(world, in row, offset, limit)),
        Composite(
            "snapshot-entries", nameof(GameWorldState.SnapshotEntries), "EquipmentSO|AlchemyRecipeSO",
            world => world.SnapshotEntries,
            reports: new[] { "loadouts" },
            scan: new[] { "ownerId", "slot", "entryId", "quantity" },
            columns: new[] { "ownerId", "slot", "entryId", "quantity" },
            listProjector: static (world, row, offset, limit) =>
                GameMcpLoadoutProjection.List(world, in row, offset, limit)),
        Entity(
            "agromancy-elements", nameof(GameWorldState.HarvestElements), "HarvestElementSO",
            world => world.HarvestElements,
            reports: new[] { "harvest-elements" },
            scan: new[] { "entityId", "masteryLevel", "masteryXp", "instances", "harvestTime", "growthTime", "harvestRate" },
            columns: new[] { "entityId", "masteryLevel" },
            projector: static (world, category, row) =>
                GameMcpHarvestLifecycleProjection.ProjectHarvestElement(world, in row),
            capabilities: new[] { GameMcpCommandKind.HarvestLifecycle }),
        Entity(
            "agromancy-actions", nameof(GameWorldState.HarvestActions), "HarvestActionSO",
            world => world.HarvestActions,
            reports: new[] { "harvest-actions" },
            scan: new[] { "entityId", "power", "speed", "costMod" },
            columns: new[] { "entityId", "power", "speed", "costMod" }),
        Entity(
            "time-runes", nameof(GameWorldState.TimeRunes), "TimeRuneSO",
            world => world.TimeRunes,
            reports: new[] { "time-runes" },
            scan: new[] { "entityId", "discovered", "level", "masteryLevel", "masteryXp", "seen" },
            columns: new[] { "entityId", "discovered" },
            projector: static (world, category, row) =>
                GameMcpGenericLevelProjection.ProjectTimeRune(world, in row),
            discovered: static row => row.Discovered,
            capabilities: new[] { GameMcpCommandKind.GenericLevel }),
        Entity(
            "augment-glyphs", nameof(GameWorldState.AugmentGlyphs), "GlyphSO",
            world => world.AugmentGlyphs,
            reports: new[] { "augment-glyphs" },
            scan: new[] { "entityId", "level", "freeLevels", "discovered", "discoverable", "maxUsages" },
            columns: new[] { "entityId", "state", "slots", "freeSlots", "paidLevel", "bonusLevel", "totalLevel" },
            projector: static (world, category, row) =>
                GameMcpGlyphProjection.ProjectGlyph(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpGlyphProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.GenericLevel }),
        Entity(
            "consumables", nameof(GameWorldState.Consumables), "ConsumableSO",
            world => world.Consumables,
            reports: new[] { "consumables", "consumable-inventory" },
            scan: new[] { "entityId", "visible", "quantity", "queuedQuantity", "maximumCarryLoad", "currentPrepTime", "currentCooldownTime", "canFire" },
            columns: new[] { "entityId", "quantity" },
            projector: static (world, category, row) =>
                GameMcpConsumableProjection.ProjectConsumable(world, in row),
            capabilities: new[] { GameMcpCommandKind.Consumable }),
        Entity(
            "rituals", nameof(GameWorldState.Rituals), "RitualSO",
            world => world.Rituals,
            reports: new[] { "rituals" },
            scan: new[] { "entityId", "discovered", "inBattle", "activeInstances", "reachedLevel", "selectedLevel", "wavesCompleted" },
            columns: new[] { "entityId", "state", "selected", "reachedLevel", "selectedLevel", "waveTotal", "affordable" },
            projector: static (world, category, row) =>
                GameMcpRitualLifecycleProjection.ProjectRitual(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpRitualLifecycleProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.RitualLifecycle }),
        Entity(
            "achievements", nameof(GameWorldState.Achievements), "AchievementSO",
            world => world.Achievements,
            reports: new[] { "achievements" },
            scan: new[] { "entityId", "level", "seen", "maxLevels" },
            columns: new[] { "entityId", "level" }),
        Entity(
            "advancements", nameof(GameWorldState.Advancements), "AdvancementSO",
            world => world.Advancements,
            reports: new[] { "advancements" },
            scan: new[] { "entityId", "levels", "xp", "isPersistent" },
            columns: new[] { "entityId", "levels" }),
        Entity(
            "challenges", nameof(GameWorldState.Challenges), "ChallengeSO",
            world => world.Challenges,
            reports: new[] { "challenges" },
            scan: new[] { "entityId", "level", "state", "seen", "rewardQueued", "maxLevel", "difficulty" },
            columns: new[] { "entityId", "state", "run", "level" },
            projector: static (world, category, row) =>
                GameMcpChallengeProjection.ProjectChallenge(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpChallengeProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Challenge, GameMcpCommandKind.Prestige }),
        Entity(
            "thought-streams", nameof(GameWorldState.ThoughtStreams), "ThoughtStreamSO",
            world => world.ThoughtStreams,
            reports: new[] { "thought-streams" },
            scan: new[] { "entityId", "state" },
            columns: new[] { "entityId", "state" }),
        Entity(
            "tutorials", nameof(GameWorldState.Tutorials), "TutorialSO",
            world => world.Tutorials,
            reports: new[] { "tutorials" },
            scan: new[] { "entityId", "isCompleted" },
            columns: new[] { "entityId", "isCompleted" }),
        Entity(
            "views", nameof(GameWorldState.Views), "ViewSO",
            world => world.Views,
            reports: new[] { "views" },
            scan: new[] { "entityId", "active", "alwaysActive", "available" },
            columns: new[] { "entityId", "active" }),
        Entity(
            "plot-node-actions", nameof(GameWorldState.PlotNodeActions), "PlotNodeActionSO",
            world => world.PlotNodeActions,
            reports: new[] { "plot-node-actions" },
            scan: new[] { "entityId", "hasBeenUsed", "isGrowingAction", "elementCost", "baseTime", "parallelAction", "prerequisiteCount", "resourceCostCount" },
            columns: new[] { "entityId", "hasBeenUsed" },
            projector: static (world, category, row) =>
                GameMcpPlotLifecycleProjection.WithPlotNodeActionDrains(world, in row, GameMcpRowKit.ReflectedDetail(category, row))),
        Entity(
            "passive-abilities", nameof(GameWorldState.PassiveAbilities), "PassiveAbilitySO",
            world => world.PassiveAbilities,
            reports: new[] { "passive-abilities" },
            scan: new[] { "entityId", "muted", "touched", "hidden", "global", "tokenRate" },
            columns: new[] { "entityId", "muted" }),
        Entity(
            "characters", nameof(GameWorldState.Characters), "CharacterSO",
            world => world.Characters,
            reports: new[] { "characters" },
            scan: new[] { "entityId", "discovered", "numberSlain", "floats" },
            columns: new[] { "entityId", "discovered" }),
        Entity(
            "discovery-trees", nameof(GameWorldState.DiscoveryTrees), "DiscoveryTreeSO",
            world => world.DiscoveryTrees,
            reports: new[] { "discovery-trees" },
            scan: new[] { "entityId", "actionMode", "actionTime", "rerollsLeft", "totalDiscoveredCount", "hasRemainingDiscovery", "hasCompletedAllDiscoveries", "selectedChoiceId" },
            columns: new[] { "entityId", "mode" },
            projector: static (world, category, row) =>
                GameMcpDiscoveryTreeOfferProjection.ProjectDiscoveryTree(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpDiscoveryTreeOfferProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.DiscoveryTreeOffer }),
        Entity(
            "recipe-books", nameof(GameWorldState.RecipeBooks), "RecipeBookSO",
            world => world.RecipeBooks,
            reports: new[] { "recipe-books" },
            scan: new[] { "entityId", "available" },
            columns: new[] { "entityId", "owned" },
            projector: static (world, category, row) =>
                GameMcpRecipeBookProjection.ProjectRecipeBook(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpRecipeBookProjection.List(world, in row, offset, limit)),
        Entity(
            "plot-nodes", nameof(GameWorldState.PlotNodes), "PlotNodeSO",
            world => world.PlotNodes,
            reports: new[] { "plot-nodes" },
            scan: new[] { "entityId", "reading.visible", "reading.masteryLevel", "reading.currentTime", "reading.idleQuantity", "reading.totalQuantity", "remainingQuantity", "remainingTotalQuantity" },
            columns: new[] { "entityId", "state", "masteryLevel", "quantity", "availableQuantity" },
            projector: static (world, category, row) =>
                GameMcpHarvestLifecycleProjection.ProjectPlotNode(in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpHarvestLifecycleProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.Harvest }),
        Composite(
            "agromancy-plot-actions", nameof(GameWorldState.PlotActions), "PlotNodeSO|PlotNodeActionSO",
            world => world.PlotActions,
            reports: new[] { "plot-nodes", "plot-node-actions", "plot-actions" },
            scan: new[] { "plotNodeId", "plotNodeActionId", "reading.offeredCount", "reading.instanceCount", "reading.prerequisitesConfirmed", "elementCost", "elementCostKnown", "hasEnoughForOneInstance", "maximumRemainingInstances" },
            columns: new[] { "plot", "action", "active", "add", "remove" },
            projector: static (world, category, row) =>
                GameMcpHarvestLifecycleProjection.ProjectPlotAction(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpPlotLifecycleProjection.List(world, in row, offset, limit)),
        Composite(
            "agromancy-processing", nameof(GameWorldState.ActionQueueSlots), "PlotNodeActionInstance",
            world => world.ActionQueueSlots,
            reports: new[] { "action-queues" },
            scan: new[] { "queueId", "index", "empty", "plotNodeId", "plotNodeActionId", "quantity", "engaged" },
            columns: new[] { "slot", "capacity", "used", "plot", "action", "amount" },
            projector: static (world, category, row) =>
                GameMcpHarvestLifecycleProjection.ProjectAgromancyProcessing(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpHarvestLifecycleProjection.List(world, in row, offset, limit)),
        Composite(
            "spell-slots", nameof(GameWorldState.SpellSlots), "Spell",
            world => world.SpellSlots,
            reports: new[] { "spell-slots" },
            scan: new[] { "slotIndex", "spellRecipeId", "occupied", "casting", "readyingCast", "attuning", "toggled", "castReady", "chargeAvailable", "resourcesCovered", "currentCharges", "maximumCharges", "cooldownRemaining" },
            columns: new[] { "slot", "spellRecipeId", "weight" },
            projector: static (world, category, row) =>
                GameMcpCastProjection.ProjectSpellSlot(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpCastProjection.List(world, in row, offset, limit),
            capabilities: new[] { GameMcpCommandKind.SpellComposition, GameMcpCommandKind.SpellLoadout }),
        Composite(
            "spell-costs", nameof(GameWorldState.SpellCosts), "Spell",
            world => world.SpellCosts,
            reports: new[] { "spell-slots" },
            scan: new[] { "slotIndex", "kind", "resourceId", "amount" },
            columns: new[] { "slot", "kind", "resourceId", "amount" },
            listProjector: static (world, row, offset, limit) =>
                GameMcpCastProjection.List(world, in row, offset, limit)),
        Composite(
            "targeting", nameof(GameWorldState.Targeting), "TargetingManager+TargetLink",
            world => world.Targeting,
            reports: new[] { "targeting" },
            scan: new[] { "ownerName", "ownerNativeType", "selectionNativeType", "cancelAvailable", "candidates.position", "candidates.structureId" },
            columns: new[] { "owner", "candidates" },
            projector: static (world, category, row) =>
                GameMcpTargetingProjection.ProjectTargeting(world, in row, 0, int.MaxValue),
            listProjector: static (world, row, offset, limit) =>
                GameMcpTargetingProjection.List(world, in row, offset, limit),
            pageKind: GameMcpWorldPageKind.Targeting,
            capabilities: new[] { GameMcpCommandKind.Targeting }),
        Composite(
            "mastery-experience", nameof(GameWorldState.MasteryExperience), "SpellRecipeSO|AlchemyRecipeSO|EquipmentSO",
            world => world.MasteryExperience,
            reports: new[] { "spell-recipes", "alchemy-recipes", "equipment" },
            scan: new[] { "sequence", "domain", "sourceId", "sourceMastery", "sourceEligible", "amount" },
            columns: new[] { "count", "domain", "sourceMastery", "sourceId" },
            pageKind: GameMcpWorldPageKind.MasteryExperience),
        Composite(
            "alchemy-instances", nameof(GameWorldState.AlchemyInstances), "AlchemyInstance",
            world => world.AlchemyInstances,
            reports: new[] { "concept-instances" },
            scan: new[] { "recipeId", "quantity", "queuedQuantity", "drainReadable", "drainRatio" },
            columns: new[] { "recipe", "activeCount", "queuedCount", "drainRatio" },
            projector: static (world, category, row) =>
                GameMcpAlchemyLoadoutProjection.ProjectAlchemyInstance(world, in row),
            listProjector: static (world, row, offset, limit) =>
                GameMcpAlchemyLoadoutProjection.List(world, in row, offset, limit)),
        Composite(
            "alchemy-costs", nameof(GameWorldState.AlchemyCosts), "AlchemyInstance",
            world => world.AlchemyCosts,
            reports: new[] { "concept-instances" },
            scan: new[] { "recipeId", "kind", "resourceId", "amount" },
            columns: new[] { "recipeId", "kind", "resourceId", "amount" }),
        Composite(
            "alchemy-loadout", nameof(GameWorldState.AlchemyLoadout), "AlchemyInstance",
            world => world.AlchemyLoadout,
            reports: new[] { "ordinary-alchemy-loadout" },
            scan: new[] { "recipeId", "position", "slotCount", "amount", "targetAmount", "multiBuy", "freeUsesRemaining", "maximumAdd", "discovered", "canAdd", "isActive", "nextAdd", "nextRemove" },
            columns: new[] { "recipeId", "slot", "slotCount", "amount" },
            listProjector: static (world, row, offset, limit) =>
                GameMcpAlchemyLoadoutProjection.List(world, in row, offset, limit)),
        Composite(
            "alchemy-usage-costs", nameof(GameWorldState.AlchemyUsageCosts), "ResourceCostList",
            world => world.AlchemyUsageCosts,
            reports: new[] { "ordinary-alchemy-loadout" },
            scan: new[] { "recipeId", "resourceId", "amount" },
            columns: new[] { "recipeId", "resourceId", "amount" }),
        Composite(
            "plot-authoring", nameof(GameWorldState.PlotAuthoring), "PlotNodeSO",
            world => world.PlotAuthoring,
            reports: new[] { "plot-authoring" },
            scan: new[] { "plotNodeId", "autoActionId", "phaseCount" },
            columns: new[] { "plotNodeId", "autoActionId", "phaseCount" }),
        Composite(
            "plot-phase-descriptors", nameof(GameWorldState.PlotPhaseDescriptors), "PlotNodeSO",
            world => world.PlotPhaseDescriptors,
            reports: new[] { "plot-authoring" },
            scan: new[] { "plotNodeId", "ordinal", "phase", "phaseTimeSeconds", "processType", "exitPhase" },
            columns: new[] { "plotNodeId", "ordinal", "phase", "phaseTimeSeconds" }),
        Composite(
            "effect-blocks", nameof(GameWorldState.EffectBlocks), "EffectSO",
            world => world.EffectBlocks,
            reports: new[] { "effect-blocks" },
            scan: new[] { "ownerId", "ordinal", "blockTypeName", "effectTypeName", "effectValue", "treasurePoolId", "prerequisiteCount", "modCount", "scriptCount" },
            columns: new[] { "ownerId", "ordinal", "blockTypeName", "effectTypeName" }),
        Composite(
            "entity-requirements", nameof(GameWorldState.EntityRequirements), "EntitySO",
            world => world.EntityRequirements,
            reports: new[] { "entity-requirements" },
            scan: new[] { "ownerId", "ordinal", "kind", "targetId", "reqType", "baseValue" },
            columns: new[] { "ownerId", "ordinal", "kind", "targetId" },
            localizesRequirementFailures: true),
        Entity(
            "treasure-pools", nameof(GameWorldState.TreasurePools), "TreasurePoolSO",
            world => world.TreasurePools,
            reports: new[] { "treasure-pools" },
            scan: new[] { "entityId", "poolId", "treasuresFound", "partialReward", "treasureLevel", "calculatedTreasureLevel" },
            columns: new[] { "entityId", "poolId" }),
    };

    private static GameMcpEntityCapabilityDescriptor Entity<TRow>(
        string category, string property, string nativeType,
        Func<GameWorldState, PublicationTable<TRow>> table,
        string[] reports, string[] scan, string[] columns,
        string[]? failureReports = null,
        Func<GameWorldState, GameMcpEntityCapabilityDescriptor, TRow, GameMcpValue>? projector = null,
        Func<GameWorldState, TRow, int, int, GameMcpValue>? listProjector = null,
        GameMcpWorldPageKind pageKind = GameMcpWorldPageKind.Rows,
        bool localizesRequirementFailures = false,
        Func<GameWorldState, TRow, bool>? affordable = null,
        Func<TRow, bool>? discovered = null,
        Func<TRow, bool>? admits = null,
        GameMcpCommandKind[]? capabilities = null)
        where TRow : struct, IWorldEntity =>
        Describe(category, nativeType, new GameMcpEntityTable<TRow>(property, table, admits),
            reports, scan, columns, failureReports, projector, listProjector, pageKind,
            localizesRequirementFailures, affordable, discovered, capabilities);

    private static GameMcpEntityCapabilityDescriptor Composite<TRow>(
        string category, string property, string nativeType,
        Func<GameWorldState, PublicationTable<TRow>> table,
        string[] reports, string[] scan, string[] columns,
        string[]? failureReports = null,
        Func<GameWorldState, GameMcpEntityCapabilityDescriptor, TRow, GameMcpValue>? projector = null,
        Func<GameWorldState, TRow, int, int, GameMcpValue>? listProjector = null,
        GameMcpWorldPageKind pageKind = GameMcpWorldPageKind.Rows,
        bool localizesRequirementFailures = false,
        Func<GameWorldState, TRow, bool>? affordable = null,
        Func<TRow, bool>? discovered = null,
        GameMcpCommandKind[]? capabilities = null)
        where TRow : struct =>
        Describe(category, nativeType, new GameMcpCompositeTable<TRow>(property, table),
            reports, scan, columns, failureReports, projector, listProjector, pageKind,
            localizesRequirementFailures, affordable, discovered, capabilities);

    private static GameMcpEntityCapabilityDescriptor Describe<TRow>(
        string category, string nativeType, GameMcpWorldTable table,
        string[] reports, string[] scan, string[] columns, string[]? failureReports,
        Func<GameWorldState, GameMcpEntityCapabilityDescriptor, TRow, GameMcpValue>? projector,
        Func<GameWorldState, TRow, int, int, GameMcpValue>? listProjector,
        GameMcpWorldPageKind pageKind, bool localizesRequirementFailures,
        Func<GameWorldState, TRow, bool>? affordable, Func<TRow, bool>? discovered,
        GameMcpCommandKind[]? capabilities)
        where TRow : struct =>
        new(category, nativeType, capabilities ?? Array.Empty<GameMcpCommandKind>(), table,
            reports, failureReports ?? Array.Empty<string>(), scan, columns,
            projector is null
                ? static (_, category, row) => GameMcpRowKit.ReflectedDetail(category, row)
                : (world, category, row) => projector(world, category, (TRow)row),
            listProjector is null
                ? static (_, category, row, _, _) => new GameMcpProjectedDomainValue(
                    GameMcpRowKit.AsDisplayed(row), category.ListColumns,
                    category.Name, category.ExpectedNativeType)
                : (world, _, row, offset, limit) => listProjector(world, (TRow)row, offset, limit),
            listProjector is not null, pageKind, localizesRequirementFailures,
            affordable is null ? null : (world, row) => affordable(world, (TRow)row),
            discovered is null ? null : row => discovered((TRow)row));

    internal static IEnumerable<string> Problems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in Descriptors)
        {
            if (!names.Add(descriptor.Category))
                yield return "duplicate category " + descriptor.Category;
            foreach (var problem in descriptor.Problems()) yield return problem;
        }
    }

}

#endif
