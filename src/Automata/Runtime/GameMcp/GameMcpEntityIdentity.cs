#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


namespace OrbAutomata.GameMcp;

internal static class GameMcpEntityIdentity
{

    /// <summary>
    /// The authored description the game itself would print on this entity's tooltip, read live for
    /// this one id. Empty for an entity this build publishes no evaluated detail for: the read is
    /// exactly as wide as it has always been, and the merge gave it no new reach.
    /// </summary>
    internal static string ReadDescription(GameWorldState world, Guid uuid)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        return TryResolve(world, uuid, out _, out _, out var nativeType)
            ? TryReadNativeDescription(uuid, nativeType)
            : string.Empty;
    }

    /// <summary>
    /// The authored description for one id, read through the native type its own category declares.
    /// </summary>
    internal static string ReadDescription(Guid uuid, string nativeType) =>
        string.IsNullOrEmpty(nativeType) ? string.Empty : TryReadNativeDescription(uuid, nativeType);

    /// <summary>
    /// The refusal for an id no published category is addressed by, naming which kind of miss it is
    /// and the read that does carry the id.
    /// </summary>
    internal static JObject UnresolvedEntity(GameWorldState world, Guid uuid)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var known = world.EntityIdentities.TryGet(uuid, out var identity);

        // A runtime instance is not a loaded asset, so the asset catalog does not know it — but the
        // world published it minutes earlier inside a composite row. Answering "nothing in this
        // process knows this UUID" was wrong about the process and pointed at a registry that could
        // never resolve it.
        if (!known && TryOwningList(world, uuid, out var owningCategory))
        {
            return Unresolved(
                uuid,
                "not_world_projected",
                "this is a runtime member of a published row rather than an entity of its own; " +
                "read the row that owns it",
                new JObject
                {
                    ["tool"] = "world_list",
                    ["category"] = owningCategory,
                });
        }
        if (!known)
        {
            // An id this build never published is the one case where a caller has no name to search
            // with — the missing name is the whole problem — so pointing at a name search sent them
            // to a tool that could not answer. The categories are what they can actually page.
            return Unresolved(
                uuid,
                "unknown_uuid",
                "no entity in this build carries this id; page world_categories for the category " +
                "you meant, or check the id you copied",
                new JObject { ["tool"] = "world_categories" });
        }
        // The twenty-five retired unlockers. They are loaded `GlyphSO`, so the native-type arm below
        // would send a caller to `augment-glyphs`, where their row will never be — the whole point of
        // retiring them. The world publishes the link, so the refusal names the book that answers
        // for the id instead of a page that cannot.
        if (WorldRecipeBookGlyphLookup.TryFindBook(world.RecipeBookGlyphs, uuid, out var book))
        {
            return Unresolved(
                uuid,
                "not_world_projected",
                "what answers to this id is internal machinery the world does not publish; the " +
                "Recipe Book it is the internal half of is " +
                EntityIdentityFormatter.PlayerHandle(book, world.EntityIdentities),
                new JObject
                {
                    ["tool"] = "world_get",
                    ["category"] = "recipe-books",
                    ["uuid"] = book.ToString("D"),
                });
        }
        if (GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                identity.RuntimeType,
                out var knownCategory))
        {
            return Unresolved(
                uuid,
                "not_world_projected",
                "this is loaded in this build, but no published row is addressed by this id; the " +
                "rows that carry it are in " + knownCategory,
                new JObject
                {
                    ["tool"] = "world_list",
                    ["category"] = knownCategory,
                });
        }
        // A remedy names a verb that will answer, and there is none to name: an id no published
        // category claims is this build's own internal machinery, and its whole readable identity
        // is on this block already. The sentence says what is true of the id and stops.
        return Unresolved(
            uuid,
            "not_world_projected",
            "this is loaded in this build, but it is internal machinery no published row covers; " +
            "its identity is all there is to read, and it is here",
            readWith: null);
    }

    internal static JObject Unresolved(Guid uuid, string code, string reason, JObject? readWith)
    {
        var result = new JObject
        {
            ["status"] = "not_available",
            ["code"] = code,
            ["reason"] = reason,
            ["uuid"] = uuid.ToString("D"),
        };
        if (readWith is not null) result["readWith"] = readWith;
        return result;
    }

    /// <summary>
    /// The published list whose rows carry this UUID as a member rather than as their own identity.
    /// Equipped spell instances are the one such member the world publishes; nothing here guesses
    /// at a category that does not actually name the UUID it was handed.
    /// </summary>
    internal static bool TryOwningList(GameWorldState world, Guid uuid, out string category)
    {
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            if (world.SpellSlots[index].SpellInstanceId != uuid) continue;
            category = "spell-slots";
            return true;
        }
        category = string.Empty;
        return false;
    }

    internal static string TryReadNativeDescription(Guid uuid, string nativeType)
    {
        try
        {
            return TryResolveTooltip(uuid, nativeType, out var tooltip)
                ? tooltip.GetDescription()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch (Exception exception)
        {
            return "Description unavailable." +
                GameActionFaultLog.Record(exception, "description of " + uuid.ToString("D"));
        }
    }

    /// <summary>
    /// The live native entity one id names, through the type its catalog row declares, when that
    /// entity is something the game draws a tooltip for.
    /// </summary>
    internal static bool TryResolveTooltip(Guid uuid, string nativeType, out ITooltipable tooltip)
    {
        tooltip = null!;
        if (string.IsNullOrEmpty(nativeType)) return false;
        var type = Type.GetType(nativeType + ", Assembly-CSharp", throwOnError: false);
        if (type is null) return false;
        var resolved = TypedRegistryResolver.Shared.Resolve(uuid, type);
        if (!resolved.IsResolved || resolved.Value is not ITooltipable found) return false;
        tooltip = found;
        return true;
    }

    internal static bool TryDescribePublishedEntity(
        GameWorldState world,
        Guid id,
        out string category,
        out string nativeType,
        out object row)
    {
        if (!TryResolve(world, id, out var kind, out row, out nativeType))
        {
            category = string.Empty;
            return false;
        }
        category = kind switch
        {
            EntityKind.Structure => "attributes",
            EntityKind.Upgrade => "upgrades",
            EntityKind.Research => "research",
            EntityKind.SpellRecipe => "spell-recipes",

            // One native class, two player categories. Which page publishes a recipe is a fact
            // about its own core alchemy type, and the row's category cell is written from that
            // same fact — so a caller who read a category off a row can always send it back.
            EntityKind.AlchemyRecipe when row is WorldAlchemyRecipe alchemyRecipe =>
                GameMcpWorldCategories.AlchemyRecipeCategory(alchemyRecipe.CoreTypeId),
            EntityKind.CraftingRecipe => "crafting-recipes",
            EntityKind.Consumable => "consumables",
            EntityKind.Resource => "resources",
            EntityKind.Ritual => "rituals",
            EntityKind.Glyph => "augment-glyphs",
            EntityKind.Equipment => "equipment",
            EntityKind.TimeRune => "time-runes",
            EntityKind.DiscoveryTree => "discovery-trees",
            _ => string.Empty,
        };
        return category.Length != 0;
    }

    internal static bool TryResolve(
        GameWorldState world,
        Guid id,
        out EntityKind kind,
        out object row,
        out string nativeType)
    {
        var matches = 0;
        var resolvedKind = EntityKind.Unknown;
        object? resolvedRow = null;
        var resolvedNativeType = string.Empty;
        void Found(EntityKind candidateKind, object candidate, string candidateType)
        {
            matches++;
            resolvedKind = candidateKind;
            resolvedRow = candidate;
            resolvedNativeType = candidateType;
        }

        if (WorldLookup.TryFind(world.Structures, id, out var structure))
            Found(EntityKind.Structure, structure, "StructureSO");
        if (WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
            Found(EntityKind.Upgrade, upgrade, "UpgradeSO");
        if (WorldLookup.TryFind(world.Research, id, out var research))
            Found(EntityKind.Research, research, "ResearchSO");
        if (WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
            Found(EntityKind.SpellRecipe, spell, "SpellRecipeSO");
        if (WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
            Found(EntityKind.AlchemyRecipe, alchemy, "AlchemyRecipeSO");
        if (WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
            Found(EntityKind.CraftingRecipe, crafting, "CraftingRecipeSO");
        if (WorldLookup.TryFind(world.Consumables, id, out var consumable))
            Found(EntityKind.Consumable, consumable, "ConsumableSO");
        if (WorldLookup.TryFind(world.Resources, id, out var resource))
            Found(EntityKind.Resource, resource, "ResourceSO");
        if (WorldLookup.TryFind(world.Rituals, id, out var ritual))
            Found(EntityKind.Ritual, ritual, "RitualSO");
        if (WorldLookup.TryFind(world.AugmentGlyphs, id, out var glyph))
            Found(EntityKind.Glyph, glyph, "GlyphSO");
        if (WorldLookup.TryFind(world.Equipment, id, out var equipment))
            Found(EntityKind.Equipment, equipment, "EquipmentSO");
        if (WorldLookup.TryFind(world.TimeRunes, id, out var timeRune))
            Found(EntityKind.TimeRune, timeRune, "TimeRuneSO");
        if (WorldLookup.TryFind(world.DiscoveryTrees, id, out var discoveryTree))
            Found(EntityKind.DiscoveryTree, discoveryTree, "DiscoveryTreeSO");
        kind = resolvedKind;
        row = resolvedRow!;
        nativeType = resolvedNativeType;
        return matches == 1;
    }

    internal enum EntityKind
    {
        Unknown = 0,
        Structure = 1,
        Upgrade = 2,
        Research = 3,
        SpellRecipe = 4,
        AlchemyRecipe = 5,
        CraftingRecipe = 6,
        Consumable = 7,
        Resource = 8,
        Ritual = 9,
        Glyph = 10,
        Equipment = 11,
        TimeRune = 12,
        DiscoveryTree = 13,
    }
}
#endif
