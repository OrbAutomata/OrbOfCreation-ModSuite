#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// One wire vocabulary for entity identity, status, reason codes, and flat tool results. The
/// catalog is the exact immutable reference pinned by the answering world, so this HTTP-side pass
/// performs no Unity read and never copies mutable world state.
/// </summary>
internal static class GameMcpEntityWireNormalizer
{
    internal static JToken Normalize(
        JToken source,
        EntityIdentityCatalogSnapshot catalog)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        NormalizeToken(source, catalog);
        return source;
    }

    private static void NormalizeToken(
        JToken token,
        EntityIdentityCatalogSnapshot catalog)
    {
        if (token is JObject item)
        {
            NormalizeObject(item, catalog);
            return;
        }
        if (token is not JArray array) return;
        for (var index = 0; index < array.Count; index++)
        {
            var value = array[index];
            if (value is JValue { Type: JTokenType.String } scalar &&
                Guid.TryParseExact((string?)scalar, "D", out var uuid) &&
                uuid != Guid.Empty)
            {
                array[index] = Reference(uuid, catalog);
                continue;
            }
            if (value is not null) NormalizeToken(value, catalog);
        }
    }

    private static void NormalizeObject(
        JObject item,
        EntityIdentityCatalogSnapshot catalog)
    {
        FlattenDetails(item);
        FlattenReading(item);
        Rename(item, "unlocked", "available");
        Rename(item, "quantity", "amount");
        Rename(item, "availableAmount", "amount");
        Rename(item, "trueQuantity", "amount");
        Rename(item, "trueRate", "netRatePerSecond");
        Rename(item, "equippedLevel", "equippedCount");
        Rename(item, "equippedStacks", "equippedCount");
        Rename(item, "activeAmount", "activeCount");

        // One name for the ceiling on the read and on the refusal. Two words for one number made a
        // caller compare what a row offered against what a refusal named and see a difference.
        Rename(item, "maximumAdditional", "maximumAmount");
        if (item["position"] is JValue position &&
            position.Type == JTokenType.Integer && (long)position < 0)
            item.Remove("position");
        NormalizeCostRow(item);

        if (item["status"] is JValue statusValue)
        {
            var status = (string?)statusValue ?? string.Empty;
            item["status"] = status switch
            {
                "not_available" => "unavailable",
                "rejected" or "skipped" => "refused",
                _ => status,
            };
            if (status is "available" or "committed") item.Remove("code");
            else if (status is "refused" or "rejected" or "skipped" or "faulted")
            {
                if (item["code"] is JValue mutationCode)
                {
                    var normalizedCode = CanonicalCode(
                        Snake((string?)mutationCode ?? string.Empty));
                    item.Remove("code");
                    if (!string.Equals(normalizedCode, (string?)item["status"],
                            StringComparison.Ordinal))
                        item["reasonCode"] = normalizedCode;
                }
            }
            else if (item["code"] is JToken readCode)
            {
                item["reasonCode"] = CanonicalCode(Snake((string?)readCode ?? string.Empty));
                item.Remove("code");
            }
        }
        if (item["status"] is JValue arrayStatus &&
            ((string?)arrayStatus is "available" or "committed") &&
            IsArrayReadResult(item))
        {
            item.Remove("status");
            item.Remove("code");
        }
        // No bare no. The sentence rule used to bind only a block that already had a code, so a
        // decision could refuse by publishing `available: false` and nothing else — a caller with
        // no axis to branch on and no way to tell a temporary no from a permanent one. Producers
        // that know the axis name it; this is the backstop that makes silence impossible.
        if (item["available"] is JValue { Type: JTokenType.Boolean } availability &&
            !(bool)availability && item["reasonCode"] is null && item["status"] is null)
        {
            item["reasonCode"] = "native_rejected";
        }
        if (item["reasonCode"] is JValue reasonCode)
        {
            var code = CanonicalCode(Snake((string?)reasonCode ?? string.Empty));

            // The producer vocabulary picked the sentence and stops there. What crosses the wire is
            // one of eight classes: the class says which kind of no this is, the sentence says
            // everything else, and a check that answered yes carries neither. The sentence is
            // defaulted inside the refusal branch for that reason — every yes used to gain
            // "This check passes." as well, one line per verdict on the surface this idiom exists
            // to compact.
            if (GameMcpDecisionReason.IsPassing(code))
            {
                item.Remove("reasonCode");
            }
            else
            {
                // A code without a sentence taught callers to fire the mutation just to read the
                // sentence. Producers that hold the numbers write the better sentence themselves
                // and keep it; every other code is answered here, so none ships a bare one.
                if (item["reason"] is null) item["reason"] = GameMcpDecisionReason.For(code);
                item["reasonCode"] = GameMcpDecisionReason.Class(code);
            }
        }
        if (item["kind"] is JValue { Type: JTokenType.String } kind)
            item["kind"] = Snake((string?)kind ?? string.Empty);
        NormalizeCode(item, "outcome");
        NormalizeCode(item, "execution");

        if (item["mcpCategory"] is JToken category)
        {
            item["category"] = category;
            item.Remove("mcpCategory");
        }
        // Read before anything is rewritten: once the row's own id is a handle it is no longer an
        // identity, and a nested id must be compared against the whole UUID or two unrelated
        // entities that happen to start alike collapse into one.
        var ownUuid = item["uuid"] is JValue { Type: JTokenType.String } ownIdentity &&
            Guid.TryParseExact((string?)ownIdentity, "D", out var parsedOwn)
                ? parsedOwn
                : Guid.Empty;
        var properties = new List<JProperty>(item.Properties());
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (property.Parent is null) continue;
            if (property.Value is JValue { Type: JTokenType.String } scalar &&
                Guid.TryParseExact((string?)scalar, "D", out var uuid))
            {
                NormalizeIdentity(item, property, uuid, ownUuid, catalog);
                continue;
            }
            if (property.Value is JValue text && text.Type == JTokenType.String)
            {
                var raw = (string?)text ?? string.Empty;
                if (IsBoundedCardinal(property.Name) &&
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var cardinal) &&
                    cardinal >= int.MinValue && cardinal <= int.MaxValue &&
                    cardinal == Math.Truncate(cardinal))
                {
                    property.Value = new JValue((int)cardinal);
                    continue;
                }
                property.Value = new JValue(GameMcpTextFormatter.Plain(raw));
                continue;
            }
            if (property.Value is JValue number &&
                IsPlayerMagnitude(property.Name) &&
                number.Type is JTokenType.Integer or JTokenType.Float)
            {
                if (IsBoundedInContext(item, property.Name)) continue;
                number.Value = GameMcpNumberFormatter.Format(
                    Convert.ToDouble(number.Value, CultureInfo.InvariantCulture));
                continue;
            }
            NormalizeToken(property.Value, catalog);
        }

        DeduplicateChildIdentity(item, "state");
        PromoteNestedPrimaryIdentity(item);
        PromoteIdentity(item);

        // Both facts are read above and neither is news on the wire. A row with no `uuid` has no
        // handle to hand back, which is the whole of what `addressable: false` said; and the
        // category a row belongs to is the category the caller named to get the page it is on.
        if (item["addressable"] is JValue { Type: JTokenType.Boolean } addressable &&
            !(bool)addressable && item["uuid"] is null)
        {
            item.Remove("addressable");
            item.Remove("category");
        }
    }

    private static void PromoteNestedPrimaryIdentity(JObject item)
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
            // that was the first two concatenated, on every row of the page. A row that references
            // one other entity keeps its role named, because then the promoted pair no longer says
            // by itself which of the two it is; and a role carrying more than a bare reference keeps
            // its block, because only the bare reference is the duplicate.
            if (IsIdentity(identity) && ReferenceCount(item) == 1) item.Remove(roles[index]);
            return;
        }
    }

    private static void CopyIfPresent(JObject source, JObject target, string field)
    {
        if (source[field] is JToken value) target[field] = value.DeepClone();
    }

    /// <summary>A reference and nothing else: the handle, and the player's name for it.</summary>
    private static bool IsIdentity(JObject item) =>
        item["uuid"] is not null &&
        (item.Count == 1 || (item.Count == 2 && item["name"] is not null));

    /// <summary>How many other entities this row names.</summary>
    private static int ReferenceCount(JObject item)
    {
        var count = 0;
        foreach (var property in item.Properties())
            if (property.Value is JObject nested && nested["uuid"] is not null) count++;
        return count;
    }

    private static void NormalizeCostRow(JObject item)
    {
        if (item["resource"] is null && item["resourceId"] is null) return;
        if (item["cost"] is not null && item["amount"] is JToken held &&
            item["spendableAmount"] is null)
        {
            item["spendableAmount"] = held;
            item.Remove("amount");
            return;
        }
        if (item["cost"] is null && item["spendableAmount"] is not null &&
            item["amount"] is JToken price)
        {
            item["cost"] = price;
            item.Remove("amount");
            return;
        }
        if (item["cost"] is null && item["effectiveCost"] is JToken effective &&
            item["amount"] is JToken spendable)
        {
            item["cost"] = effective;
            item["spendableAmount"] = spendable;
            item.Remove("effectiveCost");
            item.Remove("totalCost");
            item.Remove("amount");
        }
    }

    private static void NormalizeIdentity(
        JObject parent,
        JProperty property,
        Guid uuid,
        Guid ownUuid,
        EntityIdentityCatalogSnapshot catalog)
    {
        if (uuid == Guid.Empty)
        {
            property.Remove();
            return;
        }
        if (property.Name == "uuid")
        {
            property.Value = new JValue(GameMcpEntityHandle.Format(uuid));
            AddIdentityFields(parent, uuid, catalog);
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

        var suffixLength = property.Name.EndsWith("Uuid", StringComparison.Ordinal) ? 4 : 2;
        var role = property.Name.Substring(0, property.Name.Length - suffixLength);
        property.Remove();
        parent[role] = Reference(uuid, catalog);
    }

    /// <summary>
    /// One entity, one identity shape: the wire handle and the player's own name for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asset name, the runtime type, and the category the type implies used to ride every
    /// identity in the system. Across one live round that was 28.9 KB — 21.1% of everything the
    /// server said — and no caller read one of them once. Where a name came from and what the game
    /// calls it internally are catalog-browsing facts, and <c>entity_catalog</c> and
    /// <c>explain_entity</c> publish them there, where someone actually browsing asks for them.
    /// </para>
    /// <para>
    /// A UUID the catalog cannot name is marked, never quietly reduced to a bare id: the row that
    /// carries it is usually the one carrying the fact a caller must act on, and a nameless stub
    /// sent them to two tools that could not answer either.
    /// </para>
    /// </remarks>
    private static JObject Reference(
        Guid uuid,
        EntityIdentityCatalogSnapshot catalog)
    {
        return new JObject
        {
            ["uuid"] = GameMcpEntityHandle.Format(uuid),
            ["name"] = GameMcpEntityHandle.Name(uuid, catalog),
        };
    }

    private static void AddIdentityFields(
        JObject target,
        Guid uuid,
        EntityIdentityCatalogSnapshot catalog)
    {
        var identity = EntityIdentityFormatter.Describe(uuid, catalog);
        if (identity.HasName)
        {
            target["name"] = identity.Name;
            return;
        }

        // The catalog holds assets. A loadout the player titled and a runtime spell instance are
        // neither, so a producer that read one off the live object keeps its name here — and an id
        // nobody can name says so rather than passing for a row whose name is a hex string.
        if (target["name"] is JValue { Type: JTokenType.String } own &&
            ((string?)own ?? string.Empty).Length > 0)
        {
            return;
        }
        target["name"] = GameMcpEntityHandle.Unnamed(uuid);
    }

    private static void FlattenDetails(JObject item)
    {
        if (item["details"] is not JObject details) return;
        foreach (var property in new List<JProperty>(details.Properties()))
        {
            if (item[property.Name] is null)
                item[property.Name] = property.Value;
        }
        item.Remove("details");
    }

    private static void FlattenReading(JObject item)
    {
        if (item["reading"] is not JObject reading) return;
        foreach (var property in new List<JProperty>(reading.Properties()))
        {
            if (item[property.Name] is null)
                item[property.Name] = property.Value;
        }
        item.Remove("reading");
    }

    private static void Rename(JObject item, string from, string to)
    {
        if (item[from] is not JToken value) return;
        if (item[to] is null) item[to] = value;
        item.Remove(from);
    }

    private static void NormalizeCode(JObject item, string field)
    {
        if (item[field] is JValue { Type: JTokenType.String } value)
            item[field] = Snake((string?)value ?? string.Empty);
    }

    private static void DeduplicateChildIdentity(JObject item, string field)
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
    }

    private static void PromoteIdentity(JObject item)
    {
        if (item["uuid"] is null) return;
        var fields = new[] { "nativeType", "category", "internalName", "name", "uuid" };
        for (var index = 0; index < fields.Length; index++)
        {
            var property = item.Property(fields[index]);
            if (property is null) continue;
            property.Remove();
            item.AddFirst(property);
        }
        var status = item.Property("status");
        if (status is null) return;
        status.Remove();
        item.AddFirst(status);
    }

    /// <summary>
    /// Keys that carry a quantity of stuff the player holds, spends, or gains. The game renders
    /// these in its Scientific style, so they ship as that string on every surface even where one
    /// category happens to hold the value in an <c>int</c> — a caller must not have to learn which
    /// category it is reading to know the shape of <c>amount</c>.
    /// </summary>
    /// <remarks>
    /// <c>maximumAmount</c> was here and is not a magnitude: it is the ceiling on the integer
    /// <c>amount</c> argument a tool accepts, it is an <c>int</c> at every one of its nine
    /// producers, and quoting it as a string put one range on the wire in two JSON types
    /// (<c>minimumAmount: 1</c> beside <c>maximumAmount: "240"</c>). <c>maximumCarry</c> stays,
    /// because it caps <c>amount</c> itself rather than an argument.
    /// </remarks>
    private static bool IsPlayerMagnitude(string field) => field switch
    {
        "amount" or
        "baseCost" or "effectiveCost" or "groupCost" or "totalCost" or "cost" or
        "capacity" or "netRatePerSecond" or "yield" or
        "startingAmount" or "maximumCarry" or
        "developmentProgress" => true,
        _ => false,
    };

    private static bool IsBoundedInContext(JObject item, string field) =>
        field == "capacity" && item["slot"] is not null && item["empty"] is not null;

    private static bool IsArrayReadResult(JObject item) =>
        item["rows"] is JArray || item["results"] is JArray ||
        item["categories"] is JArray;

    private static bool IsBoundedCardinal(string field) => field switch
    {
        "level" or "levels" or "effectiveLevel" or
        "queuedLevels" or "maximumLevel" or "remainingLevels" or "baseLevel" or
        "bonusLevel" or "totalLevel" or "purchasedLevel" or "purchasedLevels" or
        "freeLevels" or "baseLevelExcludingBonus" or "effectiveCap" or "artificialCap" or
        "equippedStacks" or "maximumStacks" or "multiBuy" or "queued" or
        "queuedAmount" or "queuedQuantity" or "purchaseAmount" or "maximumAmount" or "currentCharges" or
        "maximumCharges" or "requestedAmount" or "deliveredAmount" or
        "rerollsLeft" or "selectionMaximum" or
        "resetCount" or "persistenceCurrent" or "remainingBonusLevels" or
        "maximumBatch" => true,
        _ => false,
    };

    internal static string Snake(string value)
    {
        if (value.Length == 0) return value;
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current))
            {
                if (index > 0 && result[result.Length - 1] != '_') result.Append('_');
                result.Append(char.ToLowerInvariant(current));
            }
            else if (current == ' ' || current == '-')
            {
                if (result.Length > 0 && result[result.Length - 1] != '_') result.Append('_');
            }
            else result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }

    private static string CanonicalCode(string value) => value switch
    {
        "amount_not_available" or "usage_unavailable" => "amount_unavailable",
        _ => value,
    };
}
#endif
