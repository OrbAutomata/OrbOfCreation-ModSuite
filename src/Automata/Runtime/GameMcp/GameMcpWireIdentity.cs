#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

using static OrbAutomata.GameMcp.GameMcpWireColumns;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWireIdentity
{

    internal static void PromoteNestedPrimaryIdentity(JObject item)
    {
        if (item["uuid"] is not null) return;

        // A row that has said it has no addressable identity keeps that answer. Promoting a nested
        // entity's UUID here is what made composite rows look fetchable under a foreign category.
        if (item["addressable"] is JValue { Type: JTokenType.Boolean } addressable &&
            !(bool)addressable)
        {
            return;
        }
        var roles = new[]
        {
            "action", "recipe", "plotNode", "plot", "target", "owner", "reference",
            "spellRecipe", "coreType", "entry", "partialRow",
        };
        for (var index = 0; index < roles.Length; index++)
        {
            if (item[roles[index]] is not JObject identity || identity["uuid"] is null)
                continue;
            CopyIfPresent(identity, item, "uuid");
            CopyIfPresent(identity, item, "name");
            CopyIfPresent(identity, item, "internalName");
            CopyIfPresent(identity, item, "category");
            CopyIfPresent(identity, item, "nativeType");

            // The role became the row's own identity, so leaving it in place printed a third column
            // that was the first two concatenated, on every row of the page. A row that names a
            // second entity keeps every role named, because then the promoted pair alone no longer
            // says which of the two it is; and a role carrying more than a bare reference keeps its
            // block, because only the bare reference is the duplicate.
            if (IsIdentity(identity) && ReferenceCount(item) == 1) item.Remove(roles[index]);
            return;
        }
    }

    internal static void CopyIfPresent(JObject source, JObject target, string field)
    {
        if (source[field] is JToken value) target[field] = value.DeepClone();
    }

    /// <summary>A reference and nothing else: the handle, and the player's name for it.</summary>
    internal static bool IsIdentity(JObject item) =>
        item["uuid"] is not null &&
        (item.Count == 1 || (item.Count == 2 && item["name"] is not null));

    /// <summary>How many other entities this row names.</summary>
    internal static int ReferenceCount(JObject item)
    {
        var count = 0;
        foreach (var property in item.Properties())
            if (property.Value is JObject nested && nested["uuid"] is not null) count++;
        return count;
    }

    /// <summary>
    /// A declared reference column naming no entity says so in a word, and keeps the column name a
    /// filled one would have had. Left under the raw <c>…Id</c> spelling it would be a second
    /// column for the same fact, and a page where no row names an entity would not share a header
    /// with one where some row does — which is what the declared column set exists to prevent.
    /// </summary>
    /// <remarks>
    /// Only a word reaches here: a real handle is rewritten one branch earlier, and the caller's
    /// own identity keeps its spelling because a row whose subject is unreadable is a defect the
    /// page should state plainly rather than file under a role.
    /// </remarks>
    internal static bool RenameWordReference(JObject parent, JProperty property)
    {
        if (property.Value is not JValue { Type: JTokenType.String }) return false;
        if (property.Name is "uuid" or "entityId") return false;
        var role = WireName(property.Name);
        if (string.Equals(role, property.Name, StringComparison.Ordinal)) return false;
        var value = property.Value;
        property.Remove();
        if (parent[role] is null) parent[role] = value;
        return true;
    }

    internal static void NormalizeIdentity(
        JObject parent,
        JProperty property,
        Guid uuid,
        Guid ownUuid,
        EntityIdentityCatalogSnapshot catalog,
        string producerCode = "")
    {
        if (uuid == Guid.Empty)
        {
            property.Remove();
            return;
        }
        if (property.Name == "uuid")
        {
            var unresolved = IsUnresolvedIdentity(parent);
            // A block whose whole answer is "nothing in this build carries this id" hands the id
            // back exactly as it was sent, all thirty-six characters. Shortening it to a handle
            // asked the caller to match a six-character stub against what they had typed — and a
            // handle is an address into a published set this id is by definition not in.
            if (!unresolved ||
                !string.Equals(producerCode, "unknown_uuid", StringComparison.Ordinal))
            {
                property.Value = new JValue(GameMcpEntityHandle.Format(uuid));
            }
            // No invented name on any of these blocks. `name: (unnamed deadbe)` dressed a thing
            // with no row as a row whose name happens to be missing. A known-but-unprojected id
            // still says the name the catalog really holds for it.
            AddIdentityFields(parent, uuid, catalog, inventUnnamed: !unresolved);
            return;
        }
        // Naming the subject again under its role is the same entity a third time: the row already
        // leads with that handle and that name, and no caller learns anything from being told the
        // plot it just acted on is the plot.
        if (ownUuid != Guid.Empty && ownUuid == uuid)
        {
            property.Remove();
            return;
        }

        if (!property.Name.EndsWith("Id", StringComparison.Ordinal) &&
            !property.Name.EndsWith("Uuid", StringComparison.Ordinal))
        {
            property.Value = Reference(uuid, catalog);
            return;
        }

        if (property.Name == "entityId")
        {
            property.Remove();
            parent["uuid"] = GameMcpEntityHandle.Format(uuid);
            AddIdentityFields(parent, uuid, catalog);
            return;
        }

        var role = WireName(property.Name);
        property.Remove();
        parent[role] = Reference(uuid, catalog);
    }

    /// <summary>
    /// One entity, one identity shape: the wire handle and the player's own name for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where a name came from and what the game calls it internally are catalog-browsing facts, and
    /// the <c>world_get</c> identity block publishes them there, where someone actually asking
    /// about one id asks for them.
    /// </para>
    /// <para>
    /// A UUID the catalog cannot name is marked, never quietly reduced to a bare id: the row that
    /// carries it is usually the one carrying the fact a caller must act on, and a nameless stub
    /// sent them to two tools that could not answer either.
    /// </para>
    /// </remarks>
    internal static JObject Reference(
        Guid uuid,
        EntityIdentityCatalogSnapshot catalog)
    {
        return new JObject
        {
            ["uuid"] = GameMcpEntityHandle.Format(uuid),
            ["name"] = GameMcpEntityHandle.Name(uuid, catalog),
        };
    }

    /// <summary>
    /// A block that exists to say the id names nothing: it answers <c>not_available</c> and points
    /// at the tool that could resolve it. There is no row behind it to name or to address, so the
    /// identity rules that turn a published id into a handle plus a name do not apply.
    /// </summary>
    internal static bool IsUnresolvedIdentity(JObject item) =>
        item["readWith"] is JObject && AnswersNoRow(item);

    /// <summary>
    /// A block whose whole answer is that no published row is addressed by this id — whether or not
    /// it can name a verb that would answer instead.
    /// </summary>
    /// <remarks>
    /// The producers write <c>not_available</c>; the status rewrite above has already turned it
    /// into the wire's <c>unavailable</c> by the time the identity pass runs. Both spellings answer
    /// here so the rule does not depend on which side of that rewrite it is read from.
    /// </remarks>
    internal static bool AnswersNoRow(JObject item) =>
        item["status"] is JValue { Type: JTokenType.String } status &&
        (string?)status is "unavailable" or "not_available";

    internal static void AddIdentityFields(
        JObject target,
        Guid uuid,
        EntityIdentityCatalogSnapshot catalog,
        bool inventUnnamed = true)
    {
        var identity = EntityIdentityFormatter.Describe(uuid, catalog);

        // `name` is the word the game shows a player, and where the game authors none the asset's
        // own name is the only word there is — so that is what the column prints. About five
        // hundred of the catalog's assets carry no authored word: the variables, the list holders,
        // the scaling weights. A page of them printing `name: -` beside a bare id named nothing at
        // all, and no caller could tell one `int-variables` row from the next without spending a
        // `world_get` on every one of them.
        //
        // One column, always. What may never come back is the asset id as a second field standing
        // beside a word the game or the producer already authored — that is what printed
        // `ActivePlotNodeActions` next to the queue's own name `Plot actions`, the game's field
        // talking over the game's word.
        if (identity.Source == EntityIdentityNameSource.LiveAssetName)
        {
            if (HasOwnName(target)) return;
            target["name"] = identity.AssetName;
            return;
        }
        if (identity.HasName)
        {
            target["name"] = identity.Name;
            return;
        }

        // The catalog holds assets. A loadout the player titled and a runtime spell instance are
        // neither, so a producer that read one off the live object keeps its name here — and an id
        // nobody can name says so rather than passing for a row whose name is a hex string.
        if (HasOwnName(target)) return;
        if (!inventUnnamed) return;
        target["name"] = GameMcpEntityHandle.Unnamed(uuid);
    }

    /// <summary>
    /// Whether the producer already wrote this row's own word, which outranks the asset id the
    /// catalog would otherwise supply.
    /// </summary>
    internal static bool HasOwnName(JObject target) =>
        target["name"] is JValue { Type: JTokenType.String } own &&
        ((string?)own ?? string.Empty).Length > 0;

    internal static void DeduplicateChildIdentity(JObject item, string field)
    {
        if (item["uuid"] is not JToken root || item[field] is not JObject child ||
            child["uuid"] is not JToken nested || !JToken.DeepEquals(root, nested))
        {
            return;
        }
        child.Remove("uuid");
        child.Remove("name");
        child.Remove("internalName");
        child.Remove("category");
        child.Remove("nativeType");

        // A type asset whose every record distributes into its members captures nothing beyond its
        // own handle, so the identity just lifted out was the whole row. The empty object left
        // behind printed as `row: -`, which reads as a fact the game withheld rather than as a
        // class that has no column of its own.
        if (child.Count == 0) item.Remove(field);
    }

    internal static readonly string[] Identity =
        { "nativeType", "category", "internalName", "name", "uuid" };

    internal static void PromoteIdentity(JObject item)
    {
        if (item["uuid"] is null) return;
        for (var index = 0; index < Identity.Length; index++)
        {
            var property = item.Property(Identity[index]);
            if (property is null) continue;
            property.Remove();
            item.AddFirst(property);
        }
        var status = item.Property("status");
        if (status is null) return;
        status.Remove();
        item.AddFirst(status);
    }
}
#endif
