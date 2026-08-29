#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
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
                    "alchemy-recipes",
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
            GameMcpCommandKind.GenericDiscovery =>
                TryResolveGenericDiscoveryType(world, target, out _, out reason),
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
            GameMcpCommandKind.CraftingStation =>
                CraftingStationTarget(world, target, out reason),
            GameMcpCommandKind.Loadout =>
                TryResolveLoadoutType(world, target, out _, out reason),
            GameMcpCommandKind.HarvestLifecycle =>
                Entity(world.EntityIdentities, world.HarvestElements, target,
                    "agromancy-elements", capability, out reason),
            GameMcpCommandKind.StructureLifecycle =>
                Entity(world.EntityIdentities, world.Structures, target,
                    "structures", capability, out reason),
            _ => Unsupported(out reason),
        };
    }

    private static bool CraftingStationTarget(
        GameWorldState world,
        Guid target,
        out string reason)
    {
        if (!Supports("crafting-stations", GameMcpCommandKind.CraftingStation))
        {
            reason = "Brewing stations are not something these tools can change.";
            return false;
        }
        if (WorldCraftingStationLookup.TryFind(world.CraftingStations, target, out _))
        {
            reason = string.Empty;
            return true;
        }
        reason = EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
            " is not one of your brewing stations.";
        return false;
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
            nativeType = "PlayerLoadout";
            reason = string.Empty;
            return true;
        }
        if (Supports("snapshot-loadouts", GameMcpCommandKind.Loadout) &&
            WorldLoadoutLookup.TryFindSnapshot(world.SnapshotLoadouts, target, out var snapshot))
        {
            nativeType = snapshot.Kind == WorldSnapshotLoadoutKind.Alchemy
                ? "AlchemySnapshotListVariable"
                : "EquipmentSnapshotListVariable";
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
            if (!GameMcpWorldQuery.IsScreenUnlocked(world, KnownEntities.MagicGlyphsUpgrade.Uuid))
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
                (GameMcpWorldQuery.TryNameBookPurchase(world, book, out var purchase)
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

    internal static bool TryResolveGenericDiscoveryType(
        GameWorldState world,
        Guid target,
        out string nativeType,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        nativeType = string.Empty;
        var matches = 0;
        if (Supports("alchemy-recipes", GameMcpCommandKind.GenericDiscovery) &&
            WorldLookup.TryFind(world.AlchemyRecipes, target, out _))
        {
            matches++;
            nativeType = "AlchemyRecipeSO";
        }
        if (Supports("equipment", GameMcpCommandKind.GenericDiscovery) &&
            WorldLookup.TryFind(world.Equipment, target, out _))
        {
            matches++;
            nativeType = "EquipmentSO";
        }
        if (Supports("augment-glyphs", GameMcpCommandKind.GenericDiscovery) &&
            WorldLookup.TryFind(world.AugmentGlyphs, target, out _))
        {
            matches++;
            nativeType = "GlyphSO";
        }
        if (Supports("rituals", GameMcpCommandKind.GenericDiscovery) &&
            WorldLookup.TryFind(world.Rituals, target, out _))
        {
            matches++;
            nativeType = "RitualSO";
        }
        if (Supports("spell-recipes", GameMcpCommandKind.GenericDiscovery) &&
            WorldLookup.TryFind(world.SpellRecipes, target, out _))
        {
            matches++;
            nativeType = "SpellRecipeSO";
        }
        if (Supports("time-runes", GameMcpCommandKind.GenericDiscovery) &&
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
        reason = matches == 0
            ? EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " is not something the game lets you discover."
            : EntityIdentityFormatter.PlayerName(target, world.EntityIdentities) +
              " matches " + matches + " different discoverable things, so this cannot tell " +
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
            reason = "The workbench has no output level to set right now.";
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
        if (nativeType == "EquipmentSO" &&
            WorldLookup.TryFind(world.Equipment, target, out var equipment))
        {
            category = "equipment";
            tool = equipment.IsCreated ? "game_equipment" : "game_discover";
            return true;
        }
        if (!TryCategoryForNativeType(nativeType, out category) ||
            !ByCategory.TryGetValue(category, out var descriptor)) return false;
        for (var index = 0; index < descriptor.Capabilities.Count; index++)
        {
            var candidate = GameMcpCommandKinds.ToolName(descriptor.Capabilities[index]);
            if (candidate.Length == 0) continue;
            tool = candidate;
            return true;
        }
        return true;
    }

    private static bool PurchaseTarget(GameWorldState world, Guid target, out string reason)
    {
        if (!Supports("structures", GameMcpCommandKind.Purchase) ||
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
        D("resources", "ResourceSO"),
        D("structures", "StructureSO", GameMcpCommandKind.Purchase,
            GameMcpCommandKind.Targeting, GameMcpCommandKind.StructureLifecycle),
        D("upgrades", "UpgradeSO", GameMcpCommandKind.Purchase),
        D("research", "ResearchSO", GameMcpCommandKind.Research),
        D("double-variables", "DoubleVariable"),
        D("int-variables", "IntVariable"),
        D("bool-variables", "BoolVariable"),
        D("modifier-variables", "ValueModifierVariable"),

        // The glossary carries no capability and never will: nothing buys, levels or discovers a
        // definition. The descriptor is what makes the word addressable, and it is also what turned
        // all 211 of these from projected by nothing into `statistics`.
        D("statistics", "AttributeSO"),

        // The other half of the Statistics tab. Nothing buys or levels a heading either — a group is
        // bought by buying the things it distributes into — so it carries no capability and exists
        // to make the id resolvable.
        D("attribute-groups", "AttributeGroupSO"),

        // The ritual layer's ten glossaries, under the same rule as the statistic glossary: nothing
        // buys, levels or discovers a word. Each descriptor is what turns its type's rows from
        // `not-world-projected` into a category a read can answer.
        D("status-effects", "CombatStatusSO"),
        D("character-attributes", "CharacterAttributeSO"),
        D("damage-types", "DamageTypeSO"),
        D("character-modifiers", "CharacterModifierSO"),
        D("character-actions", "CharacterActionSO"),
        D("character-types", "CharacterTypeSO"),
        D("enchantments", "EnchantmentSO"),
        D("glyph-types", "GlyphTypeSO"),
        D("rune-stones", "RuneStoneSO"),
        D("display-types", "DisplayTypeSO"),
        D("purchase-costs", "StructureSO|UpgradeSO"),
        D("alchemy-recipes", "AlchemyRecipeSO", GameMcpCommandKind.Concept,
            GameMcpCommandKind.GenericDiscovery, GameMcpCommandKind.AlchemyLoadout),
        D("alchemy-types", "AlchemyTypeSO"),
        D("spell-recipes", "SpellRecipeSO", GameMcpCommandKind.Cast, GameMcpCommandKind.SpellLevel,
            GameMcpCommandKind.GenericDiscovery, GameMcpCommandKind.SpellWorkbench),
        D("spell-types", "SpellTypeSO"),
        D("equipment", "EquipmentSO", GameMcpCommandKind.GenericDiscovery,
            GameMcpCommandKind.EquipmentLoadout),
        D("equipment-types", "EquipmentTypeSO", GameMcpCommandKind.GenericLevel),
        D("resource-types", "ResourceTypeSO", GameMcpCommandKind.GenericLevel),
        D("crafting-recipe-types", "CraftingRecipeTypeSO"),

        // The nine taxonomies whose types the game gives no verb of their own: nothing buys,
        // levels or equips a structure type, so each descriptor carries no capability and exists
        // to make the id resolvable to a category a read can answer.
        D("structure-types", "StructureTypeSO"),
        D("ritual-types", "RitualTypeSO"),
        D("agromancy-element-types", "HarvestTypeSO"),
        D("plot-node-types", "PlotNodeTypeSO"),
        D("research-types", "ResearchTypeSO"),
        D("consumable-types", "ConsumableTypeSO"),
        D("plot-node-action-types", "HarvestActionTypeSO"),
        D("passive-ability-types", "PassiveAbilityTypeSO"),
        D("time-rune-types", "TimeRuneTypeSO"),
        D("crafting-recipes", "CraftingRecipeSO", GameMcpCommandKind.Crafting),
        D("crafting-queue-entries", "CraftingInstance"),
        D("player-loadouts", "PlayerLoadout", GameMcpCommandKind.Loadout),
        D("snapshot-loadouts", "AlchemySnapshotListVariable|EquipmentSnapshotListVariable",
            GameMcpCommandKind.Loadout),
        D("player-loadout-entries", "Spell|EquipmentSO|AlchemyRecipeSO"),
        D("snapshot-slots", "AlchemySnapshot|EquipmentSnapshot"),
        D("snapshot-entries", "EquipmentSO|AlchemyRecipeSO"),
        D("agromancy-elements", "HarvestElementSO", GameMcpCommandKind.HarvestLifecycle),

        // The base verb itself carries no capability: game_agromancy acts on an element/action
        // pair, so the element owns the lifecycle and this roster is read-only.
        D("agromancy-actions", "HarvestActionSO"),
        D("time-runes", "TimeRuneSO", GameMcpCommandKind.GenericDiscovery,
            GameMcpCommandKind.GenericLevel),
        D("augment-glyphs", "GlyphSO", GameMcpCommandKind.GenericDiscovery,
            GameMcpCommandKind.GenericLevel),
        D("consumables", "ConsumableSO", GameMcpCommandKind.Consumable),
        D("rituals", "RitualSO", GameMcpCommandKind.GenericDiscovery,
            GameMcpCommandKind.RitualLifecycle),
        D("achievements", "AchievementSO"),
        D("advancements", "AdvancementSO"),
        D("challenges", "ChallengeSO", GameMcpCommandKind.Challenge,
            GameMcpCommandKind.Prestige),
        D("thought-streams", "ThoughtStreamSO"),
        D("tutorials", "TutorialSO"),
        D("views", "ViewSO"),
        D("plot-node-actions", "PlotNodeActionSO"),
        D("passive-abilities", "PassiveAbilitySO"),
        D("characters", "CharacterSO"),
        D("discovery-trees", "DiscoveryTreeSO", GameMcpCommandKind.DiscoveryTreeOffer),
        D("recipe-books", "RecipeBookSO"),
        D("plot-nodes", "PlotNodeSO", GameMcpCommandKind.Harvest),
        D("agromancy-plot-actions", "PlotNodeSO|PlotNodeActionSO"),
        D("agromancy-processing", "PlotNodeActionInstance"),
        D("spell-slots", "Spell", GameMcpCommandKind.SpellComposition,
            GameMcpCommandKind.SpellLoadout),
        D("spell-costs", "Spell"),
        D("targeting", "TargetingManager+TargetLink", GameMcpCommandKind.Targeting),
        D("mastery-experience", "SpellRecipeSO|AlchemyRecipeSO|EquipmentSO"),
        D("concept-recipes", "AlchemyRecipeSO"),
        D("alchemy-instances", "AlchemyInstance"),
        D("alchemy-costs", "AlchemyInstance"),
        D("alchemy-loadout", "AlchemyInstance"),
        D("alchemy-usage-costs", "ResourceCostList"),
        D("plot-authoring", "PlotNodeSO"),
        D("plot-phase-descriptors", "PlotNodeSO"),
        D("effect-blocks", "EffectSO"),
        D("entity-requirements", "EntitySO"),
        D("treasure-pools", "TreasurePoolSO"),
    };

    private static GameMcpEntityCapabilityDescriptor D(
        string category,
        string nativeType,
        params GameMcpCommandKind[] capabilities) =>
        new(category, nativeType, capabilities);
}

internal sealed class GameMcpEntityCapabilityDescriptor
{
    private readonly GameMcpCommandKind[] _capabilities;

    internal GameMcpEntityCapabilityDescriptor(
        string category,
        string expectedNativeType,
        GameMcpCommandKind[] capabilities)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        ExpectedNativeType = expectedNativeType ??
            throw new ArgumentNullException(nameof(expectedNativeType));
        _capabilities = capabilities is null
            ? Array.Empty<GameMcpCommandKind>()
            : (GameMcpCommandKind[])capabilities.Clone();
    }

    internal string Category { get; }
    internal string ExpectedNativeType { get; }
    internal IReadOnlyList<GameMcpCommandKind> Capabilities => _capabilities;

    internal bool Supports(GameMcpCommandKind capability)
    {
        for (var index = 0; index < _capabilities.Length; index++)
            if (_capabilities[index] == capability) return true;
        return false;
    }
}
#endif
