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


using static OrbAutomata.GameMcp.GameMcpRowKit;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpRecipeBookProjection
{

    /// <summary>
    /// The authored condition holding an entity shut, named. A Recipe Book carries exactly one in
    /// <c>RecipeBookSO.prerequisites</c> — twenty-four an upgrade, nine a research, one a
    /// prerequisite link — and <c>Prerequisites.Container.Check()</c> asks all of them at level
    /// zero, which is the level this evaluates at. The first unmet one is the answer: they are a
    /// flat AND, so any of them refusing is a complete reason, and naming the first keeps one
    /// sentence per row. Which kind of thing it is, is read rather than assumed: pinning "the Learn
    /// upgrade" would have been wrong on ten of the thirty-four.
    /// </summary>
    internal static bool TryNameRequirementBlocker(
        GameWorldState world,
        Guid entityId,
        out string name,
        out Guid blockerId)
    {
        name = string.Empty;
        blockerId = Guid.Empty;
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, entityId, out var start, out var count))
        {
            return false;
        }

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.NodeKind != WorldRequirementNodeKind.Leaf) continue;
            if (WorldRequirementEvaluator.Evaluate(world, in row, level: 0) !=
                WorldRequirementVerdict.Unmet)
            {
                continue;
            }
            var identity = EntityIdentityFormatter.Describe(row.TargetId, world.EntityIdentities);
            if (!identity.HasName) continue;
            name = identity.Name;
            blockerId = row.TargetId;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The purchase that owns a Recipe Book, by name, where the world can read one. Shared with the
    /// level verb's refusal so the row and the refusal name the same thing.
    /// </summary>
    internal static bool TryNameBookPurchase(GameWorldState world, Guid bookId, out string name)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        return TryNameRequirementBlocker(world, bookId, out name, out _);
    }

    /// <summary>
    /// Which Recipe Book keeps a spell out of its tree's pool, in the words its rows use.
    /// </summary>
    /// <remarks>
    /// The pool is <c>!IsDiscovered() &amp;&amp; IsDiscoverVisible()</c>, and
    /// <c>SpellRecipeSO.IsDiscoverVisible</c> asks every book of <c>recipeBookList</c> to be owned —
    /// the edge the row prints as <c>belongsTo.recipeBooks</c>. A book the world publishes no row
    /// for is not named: ownership is the fact the sentence turns on.
    /// </remarks>
    internal static string MissingRecipeBookReason(GameWorldState world, in WorldSpellRecipe recipe)
    {
        var book = MissingRecipeBook(world, recipe.EntityId);
        return book.Length == 0
            ? string.Empty
            : GameMcpDecisionReason.For("not_visible") + " It needs the " + book +
              " recipe book, which is not owned.";
    }

    internal static string MissingRecipeBook(GameWorldState world, Guid recipeId)
    {
        if (!WorldSpellGraphLookup.TryFindRelations(
                world.SpellRelations, recipeId, out var start, out var count))
            return string.Empty;
        for (var index = start; index < start + count; index++)
        {
            var relation = world.SpellRelations[index];
            if (relation.Kind != WorldSpellRelationKind.RecipeBook) continue;
            if (!WorldLookup.TryFind(world.RecipeBooks, relation.TargetId, out var book) ||
                book.Available)
                continue;
            return EntityIdentityFormatter.PlayerName(relation.TargetId, world.EntityIdentities);
        }
        return string.Empty;
    }

    /// <summary>Whether any augment glyph is the other half of this book.</summary>
    internal static bool HasGlyphTwin(GameWorldState world, Guid bookId)
    {
        for (var index = 0; index < world.RecipeBookGlyphs.Count; index++)
            if (world.RecipeBookGlyphs[index].RecipeBookId == bookId) return true;
        return false;
    }

    /// <summary>
    /// One Recipe Book: whether the player owns it, what buys it if not, and which discovery pools
    /// it widens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole of a book is a yes/no. What is actionable is the one purchase that flips the
    /// answer, which is why an unowned book names it.
    /// </para>
    /// <para>
    /// <c>widens</c> and "which screen draws this" are the same authored fact:
    /// <c>UIDiscoveryTreePage.UIStart()</c> is the only reader of
    /// <c>DiscoveryTreeSO.availableRecipeBooks</c>, so a book appears exactly on the discovery
    /// pages whose pool it opens — six of the seven trees on the pinned build, and four of them for
    /// each of the seven elemental books. A separate <c>screen</c> column would restate this edge
    /// in a worse form: it would have to pick one page where the game draws several.
    /// </para>
    /// <para>
    /// Six books share a display name with a spell type — Arcane, Dragon, Expansion, Flow, Psionic,
    /// Storm — and a single spell-recipe response can print both, so the row names its twin by
    /// uuid. It is read from the published world rather than pinned to those six names, because
    /// which names collide is a fact about the build.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectRecipeBook(GameWorldState world, in WorldRecipeBook book)
    {
        var result = new JObject
        {
            ["entityId"] = book.EntityId.ToString("D"),
            ["category"] = "recipe-books",
            ["owned"] = book.Available,
        };
        if (!book.Available &&
            TryNameRequirementBlocker(world, book.EntityId, out var name, out var blockerId))
        {
            result["ownedBy"] = new JObject
            {
                ["uuid"] = blockerId.ToString("D"),
                ["name"] = name,
            };
        }

        if (WorldDiscoveryTreeBookLookup.TryFindRange(
                world.DiscoveryTreeBooks, book.EntityId, out var start, out var count))
        {
            var widens = new JArray();
            for (var index = 0; index < count; index++)
                widens.Add(EntityReference(world, world.DiscoveryTreeBooks[start + index].TreeId));
            result["widens"] = widens;
        }

        // Twenty-five of the thirty-four books are the far half of an augment glyph of the same
        // name, and nine are not. Their rows were identical, so a reader could not tell a book
        // that is discovered on the Glyphcraft page from one that only ever arrives as a book —
        // and the only tell on the wire was that five of the nine happen to carry an internal
        // name, which is a diagnostic and not a statement.
        if (!HasGlyphTwin(world, book.EntityId))
        {
            result["obtaining"] = "No augment glyph carries this book, so no discovery produces " +
                "it: it is owned by meeting its prerequisite and nothing else.";
        }

        var bookName = EntityIdentityFormatter.PlayerName(book.EntityId, world.EntityIdentities);
        if (bookName.Length > 0)
        {
            for (var index = 0; index < world.SpellTypes.Count; index++)
            {
                var typeId = world.SpellTypes[index].EntityId;
                if (!string.Equals(
                        EntityIdentityFormatter.PlayerName(typeId, world.EntityIdentities),
                        bookName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                result["nameSharedWith"] = new JObject
                {
                    ["uuid"] = typeId.ToString("D"),
                    ["category"] = "spell-types",
                };
                break;
            }
        }

        return result.Freeze();
    }
    internal static GameMcpValue List(
        GameWorldState world,
        in WorldRecipeBook recipeBook,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = recipeBook.EntityId.ToString("D"),
                ["owned"] = recipeBook.Available,
            }.Freeze();
    }

}
#endif
