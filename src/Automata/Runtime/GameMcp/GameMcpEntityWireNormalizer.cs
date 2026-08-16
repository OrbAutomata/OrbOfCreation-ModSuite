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
        NormalizeToken(source, catalog, inRow: false);
        return source;
    }

    private static void NormalizeToken(
        JToken token,
        EntityIdentityCatalogSnapshot catalog,
        bool inRow,
        bool inTable = false)
    {
        if (token is JObject item)
        {
            NormalizeObject(item, catalog, inRow, inTable);
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

            // Anything in a list may be rendered as a row, and a row answers to a header: a column
            // the header promises is said on every row of the page, so a default may not be dropped
            // from one of them. Only a block of its own may leave a default out.
            if (value is not null) NormalizeToken(value, catalog, inRow, inTable: true);
        }
    }

    private static void NormalizeObject(
        JObject item,
        EntityIdentityCatalogSnapshot catalog,
        bool inRow,
        bool inTable = false)
    {
        FlattenDetails(item);
        FlattenReading(item);
        if (!inTable) DropDefaults(item);
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

        // Read before the code is replaced by its class: only the producer's own word tells an id
        // this build never published apart from one it published somewhere else, and both arrive at
        // the identity pass below wearing the same ERR_NOT_FOUND.
        var producerCode = item["code"] is JValue producer
            ? Snake((string?)producer ?? string.Empty)
            : string.Empty;

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
        //
        // A table row is the one place it must not fire. There, `available` is a declared column a
        // header already names, and the pair it invents is a code and a sentence saying the game
        // refused and would not say why — the same 139 characters on every row of a page, telling
        // a reader nothing the `no` in the column did not. Cells carry words; the sentence lives
        // in get and in refusals, which are exactly the shapes this backstop still guards.
        //
        // What it supplies is a lock, not a refusal. `available: false` with no axis beside it is
        // the game holding something shut and publishing no condition for it — which is what
        // ERR_LOCKED means. Filing it as ERR_REFUSED invented a refusal of an action nobody had
        // asked for, and put a second class on the same fact the predicate block was already
        // answering ERR_LOCKED about, inside one response. On a mature save the producers' own
        // codes hid it; after a prestige nothing is exhausted and everything is re-locked, so it
        // answered for a whole category at once.
        if (!inRow &&
            item["available"] is JValue { Type: JTokenType.Boolean } availability &&
            !(bool)availability && item["reasonCode"] is null && item["status"] is null)
        {
            item["reasonCode"] = "native_unavailable";
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
                KeepReasonBesideItsCode(item);
            }
        }
        if (item["kind"] is JValue { Type: JTokenType.String } kind)
            item["kind"] = Snake((string?)kind ?? string.Empty);
        // One native enum, one word, wherever it surfaces. `modifierType` reaches the wire from two
        // producers — a research requirement adjustment and a modifier-variables row — and both
        // shipped the game's raw ordinal while the worth block beside them had been saying `raw` /
        // `diminishing` / `stacking` for the same enum all along.
        if (item["modifierType"] is JValue { Type: JTokenType.Integer } modifierType)
        {
            item["modifierType"] =
                GameMcpNativeVocabulary.ModifierEffect((int)modifierType);
        }
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
                NormalizeIdentity(item, property, uuid, ownUuid, catalog, producerCode);
                continue;
            }
            if (RenameWordReference(item, property)) continue;
            if (property.Value is JValue text && text.Type == JTokenType.String)
            {
                var raw = (string?)text ?? string.Empty;
                if (IsBoundedCardinal(property.Name) &&
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var cardinal) &&
                    cardinal >= int.MinValue && cardinal <= int.MaxValue &&
                    cardinal == Math.Truncate(cardinal) &&
                    IsPlainSpelling(raw, (int)cardinal))
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
            // A page's rows are its table, and every cell of a row — including the objects and
            // arrays a cell holds — answers under the table's grammar rather than a document's.
            NormalizeToken(
                property.Value,
                catalog,
                inRow || string.Equals(property.Name, "rows", StringComparison.Ordinal));
        }

        DeduplicateChildIdentity(item, "row");
        DeduplicateRowVerdict(item);
        DropRowRestatements(item);
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
            // that was the first two concatenated, on every row of the page. A row that names a
            // second entity keeps every role named, because then the promoted pair alone no longer
            // says which of the two it is; and a role carrying more than a bare reference keeps its
            // block, because only the bare reference is the duplicate.
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
        }
        else if (item["cost"] is null && item["spendableAmount"] is not null &&
            item["amount"] is JToken price)
        {
            item["cost"] = price;
            item.Remove("amount");
        }
        else if (item["cost"] is null && item["effectiveCost"] is JToken effective &&
            item["amount"] is JToken spendable)
        {
            item["cost"] = effective;
            item["spendableAmount"] = spendable;
            item.Remove("effectiveCost");
            item.Remove("totalCost");
            item.Remove("amount");
        }
        OrderPriceColumns(item);
    }

    /// <summary>
    /// One price shape wherever a price is said: what it asks, what you hold, whether that covers
    /// it — and the resource it is about, which every reference lands after.
    /// </summary>
    /// <remarks>
    /// A producer that published the price under <c>amount</c> had it renamed here, and a renamed
    /// member is written where a new one goes: last. So the same three facts came back
    /// <c>cost | spendableAmount | affordable</c> from one verb and <c>cost | affordable |
    /// spendableAmount</c> from the next, and a reader scanning two price tables in one session
    /// read the second one positionally and got the wrong column. The columns a price is made of
    /// are a fact about prices, not about which producer happened to build this one, so they are
    /// put in that order once, here, after every rename that could disturb it — the same order
    /// <c>purchase-costs</c> declares for its own page.
    /// </remarks>
    private static void OrderPriceColumns(JObject item)
    {
        for (var index = PriceColumns.Length - 1; index >= 0; index--)
        {
            if (item.Property(PriceColumns[index]) is not JProperty property) continue;
            var value = property.Value;
            property.Remove();
            item.AddFirst(new JProperty(PriceColumns[index], value));
        }
    }

    private static readonly string[] PriceColumns = { "cost", "spendableAmount", "affordable" };

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
    private static bool RenameWordReference(JObject parent, JProperty property)
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

    private static void NormalizeIdentity(
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
    /// The asset name, the runtime type, and the category the type implies used to ride every
    /// identity in the system. Across one live round that was 28.9 KB — 21.1% of everything the
    /// server said — and no caller read one of them once. Where a name came from and what the game
    /// calls it internally are catalog-browsing facts, and the <c>world_get</c> identity block
    /// publishes them there, where someone actually asking about one id asks for them.
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

    /// <summary>
    /// A block that exists to say the id names nothing: it answers <c>not_available</c> and points
    /// at the tool that could resolve it. There is no row behind it to name or to address, so the
    /// identity rules that turn a published id into a handle plus a name do not apply.
    /// </summary>
    private static bool IsUnresolvedIdentity(JObject item) =>
        item["readWith"] is JObject &&
        item["status"] is JValue { Type: JTokenType.String } status &&
        // The producers write `not_available`; the status rewrite above has already turned it into
        // the wire's `unavailable` by the time the identity pass runs. Both spellings answer here
        // so the rule does not depend on which side of that rewrite it is read from.
        (string?)status is "unavailable" or "not_available";

    private static void AddIdentityFields(
        JObject target,
        Guid uuid,
        EntityIdentityCatalogSnapshot catalog,
        bool inventUnnamed = true)
    {
        var identity = EntityIdentityFormatter.Describe(uuid, catalog);

        // `name` is the word the game shows a player, or it is nothing. Five hundred of the
        // catalog's assets — the variables, list holders, scaling weights, tutorials — carry no
        // authored word at all, and standing the Unity asset id in for one published
        // `SummonedLevel` and `ScalingBase` under the field every other row spells a real name in.
        // Nothing is lost: the asset id is a different fact and says so under its own key, beside
        // the id the row is addressed by.
        if (identity.Source == EntityIdentityNameSource.LiveAssetName)
        {
            if (target["internalName"] is null) target["internalName"] = identity.AssetName;
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
        if (target["name"] is JValue { Type: JTokenType.String } own &&
            ((string?)own ?? string.Empty).Length > 0)
        {
            return;
        }
        if (!inventUnnamed) return;
        target["name"] = GameMcpEntityHandle.Unnamed(uuid);
    }

    /// <summary>
    /// The two flags a detail block stops spelling out when they read the way they almost always
    /// read: absence is the default, and the default is documented where the shape is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A live round spent 3,712 bytes on <c>isPercent: no</c> — 76% of every time it was said — and
    /// 726 on <c>order: 0</c>, which was the value on 100% of the rows that carried it. Neither is
    /// a reading that could be missing: the game holds a bool and an int, so absence has exactly
    /// one meaning and <c>docs/development/mcp-tools.md</c> states it beside the field.
    /// </para>
    /// <para>
    /// A block only, never a row. A table's header promises a column on every row of the page, so
    /// dropping a default there would leave <c>-</c> where the game published <c>0</c> — absence
    /// standing in for a number, which is the one thing the absence mark may never mean.
    /// </para>
    /// </remarks>
    private static void DropDefaults(JObject item)
    {
        if (item["isPercent"] is JValue { Type: JTokenType.Boolean } percent && !(bool)percent)
            item.Remove("isPercent");
        if (item["order"] is JValue { Type: JTokenType.Integer } order && (long)order == 0)
            item.Remove("order");
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

    /// <summary>
    /// The sentence sits under the code it explains, wherever the two arrived from.
    /// </summary>
    /// <remarks>
    /// A block that publishes both adjacently reads as one fact; one that does not reads as two. A
    /// live round met a locked glyph whose <c>reasonCode</c> and <c>reason</c> sat seven lines
    /// apart with three decisions wedged between them — the sentence ending up beside an unrelated
    /// <c>discover: yes</c> — and could not tell whether the trailing sentence was that block's
    /// blocker or a stray, so it spent a second read on two other rows to learn the shape. The pair
    /// is one pair on every page now, whether the producer wrote both or this pass supplied one.
    /// </remarks>
    private static void KeepReasonBesideItsCode(JObject item)
    {
        if (item.Property("reasonCode") is not { } code ||
            item.Property("reason") is not { } sentence)
        {
            return;
        }
        if (ReferenceEquals(code.Next, sentence)) return;
        sentence.Remove();
        code.AddAfterSelf(sentence);
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

    /// <summary>
    /// One class per fact per response. A response that publishes an entity's row under <c>row</c>
    /// and its evaluated verdicts under <c>predicates</c> answered availability twice, and the two
    /// answers disagreed about which kind of no it was: the row's <c>reasonCode</c> read
    /// ERR_REFUSED where <c>predicates.available</c> read ERR_LOCKED, for one entity, in one
    /// payload. A caller branching on the first got a different program than one branching on the
    /// second, which makes the taxonomy useless for control flow — the failure this deletes.
    /// </summary>
    /// <remarks>
    /// The predicate block wins because it is the surface built to hold verdicts: one per named
    /// fact, each with its own code, where the row carries a single pair for the whole row. The row
    /// keeps the fact itself — <c>available</c> stays — and gives up only the second opinion about
    /// it, which the block below states in full.
    /// </remarks>
    private static void DeduplicateRowVerdict(JObject item)
    {
        if (item["predicates"] is not JObject predicates ||
            predicates["available"] is not JObject ||
            item["row"] is not JObject row ||
            row["available"] is null)
        {
            return;
        }
        row.Remove("reasonCode");
        row.Remove("reason");
    }

    /// <summary>
    /// Which row action answers the same question as which predicate. A predicate beside one of
    /// these is a coarser second opinion, and where it agrees word for word it is the same opinion
    /// twice.
    /// </summary>
    private static readonly (string Predicate, string Action)[] PredicateActions =
    {
        ("canDiscover", "discover"),
        ("canPurchase", "purchase"),
        ("canDevelop", "develop"),
        ("canUse", "use"),
    };

    /// <summary>
    /// Drops every line under <c>predicates</c> and <c>blockers</c> that is a second copy of what
    /// the row beside them already says — and only where the row really says it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four entities of one live round printed
    /// <c>discover: no (ERR_STATE): This is already discovered.</c> on the row and
    /// <c>canDiscover: no (ERR_STATE): This is already discovered.</c> under predicates — the
    /// identical sentence twice inside one entity — while the word <c>predicates</c> appeared once
    /// in the caller's entire reasoning and <c>blockers</c> never appeared at all.
    /// </para>
    /// <para>
    /// The rule is per field and it turns on the duplicate being present. A <c>canDiscover</c> whose
    /// row publishes no <c>discover</c> action still prints. A predicate carrying anything of its
    /// own still prints, which is why <c>canUse: yes slots=[1]</c> keeps its slot list — that list
    /// appears nowhere else on the page. A blocked axis prints in full, because the numbers behind a
    /// no are what a caller acts on; an axis whose whole content is that it is not blocking says
    /// what an unlisted axis already says, and <c>blockers</c> then reads as the list of what
    /// blocks. Both keys survive even when everything under them goes: an entity with no applicable
    /// predicate and one whose predicates were never evaluated are different answers, and an omitted
    /// key says both.
    /// </para>
    /// </remarks>
    private static void DropRowRestatements(JObject item)
    {
        if (item["predicates"] is JObject predicates && item["row"] is JObject row)
        {
            for (var index = 0; index < PredicateActions.Length; index++)
            {
                var pair = PredicateActions[index];
                if (predicates[pair.Predicate] is not JObject verdict) continue;
                if (row[pair.Action] is not JObject twin) continue;
                if (!SameVerdict(verdict, twin)) continue;
                predicates.Remove(pair.Predicate);
            }
        }
        if (item["blockers"] is not JObject blockers) return;
        var axes = new List<string>(blockers.Count);
        foreach (var axis in blockers.Properties()) axes.Add(axis.Name);
        for (var index = 0; index < axes.Count; index++)
        {
            if (blockers[axes[index]] is not JObject axisBlock || axisBlock.Count != 1) continue;
            if (axisBlock["blocked"] is not JValue { Type: JTokenType.Boolean } blocked) continue;
            if (!(bool)blocked) blockers.Remove(axes[index]);
        }
    }

    /// <summary>Whether two verdict blocks say the same yes or the same no for the same reason.</summary>
    /// <remarks>
    /// A predicate with a field of its own is never a copy of anything: the field is the finding,
    /// and the verdict it sits beside is what gives it its meaning.
    /// </remarks>
    private static bool SameVerdict(JObject predicate, JObject action)
    {
        foreach (var property in predicate.Properties())
            if (property.Name is not ("available" or "reasonCode" or "reason")) return false;
        return JToken.DeepEquals(predicate["available"], action["available"]) &&
            JToken.DeepEquals(predicate["reasonCode"], action["reasonCode"]) &&
            JToken.DeepEquals(predicate["reason"], action["reason"]);
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

        // A type asset whose every record distributes into its members captures nothing beyond its
        // own handle, so the identity just lifted out was the whole row. The empty object left
        // behind printed as `row: -`, which reads as a fact the game withheld rather than as a
        // class that has no column of its own.
        if (child.Count == 0) item.Remove(field);
    }

    private static readonly string[] Identity =
        { "nativeType", "category", "internalName", "name", "uuid" };

    private static void PromoteIdentity(JObject item)
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

    /// <summary>
    /// What this pass will call a producer's declared columns, in the order it will leave them in.
    /// </summary>
    /// <remarks>
    /// A page that matched no rows has no rows to read the header off, and a reader needs that
    /// header most on exactly that page — "no rows here" and "no such shape" are different answers
    /// and a bare count says neither. So the producer states its declaration and this says what the
    /// wire will have made of it, from the same rule and the same promotion order the rows go
    /// through. A category whose rendered page disagrees with what this predicts fails the gate
    /// rather than shipping a header naming columns its rows do not carry.
    /// </remarks>
    internal static string[] WireColumns(IReadOnlyList<string> declared)
    {
        if (declared is null) throw new ArgumentNullException(nameof(declared));
        var names = new List<string>(declared.Count + 1);
        for (var index = 0; index < declared.Count; index++) Declare(names, declared[index]);

        // Each of these lands its value under a name it did not have, and a name a row did not
        // already carry is appended — so a renamed column is last, in the order they are applied
        // to the row itself.
        for (var index = 0; index < Renamed.Length; index += 2)
        {
            if (!names.Remove(Renamed[index])) continue;
            Declare(names, Renamed[index + 1]);
        }

        // A reference is republished under its role rather than renamed in place, so it lands after
        // the columns that were already there — and the subject of a row is the one reference that
        // becomes two columns, the handle a caller acts on and the name a player reads.
        var declaredOrder = new List<string>(names);
        for (var index = 0; index < declaredOrder.Count; index++)
        {
            var name = declaredOrder[index];
            if (string.Equals(name, "entityId", StringComparison.Ordinal))
            {
                names.Remove(name);
                Declare(names, "uuid");
                Declare(names, "name");
                continue;
            }
            var role = WireName(name);
            if (string.Equals(role, name, StringComparison.Ordinal)) continue;
            names.Remove(name);
            Declare(names, role);
        }

        if (!names.Contains("uuid")) return names.ToArray();
        for (var index = 0; index < Identity.Length; index++)
        {
            if (!names.Remove(Identity[index])) continue;
            names.Insert(0, Identity[index]);
        }
        return names.ToArray();
    }

    /// <summary>
    /// The renames <see cref="NormalizeObject"/> applies, as from/to pairs in the order it applies
    /// them, so a declaration and a row cannot drift into two vocabularies.
    /// </summary>
    private static readonly string[] Renamed =
    {
        "unlocked", "available",
        "quantity", "amount",
        "availableAmount", "amount",
        "trueQuantity", "amount",
        "trueRate", "netRatePerSecond",
        "equippedLevel", "equippedCount",
        "equippedStacks", "equippedCount",
        "activeAmount", "activeCount",
        "maximumAdditional", "maximumAmount",
    };

    /// <summary>
    /// One declared path as one column name. A member read out of a nested reading is lifted onto
    /// the row under its own name, so the column is the leaf; any other nested declaration stays
    /// the object it was copied into, so the column is its root.
    /// </summary>
    private static void Declare(List<string> names, string path)
    {
        var stop = path.IndexOf('.');
        var name = stop < 0
            ? path
            : path.StartsWith("reading.", StringComparison.Ordinal) ||
                path.StartsWith("details.", StringComparison.Ordinal)
                ? path.Substring(path.LastIndexOf('.') + 1)
                : path.Substring(0, stop);
        if (!names.Contains(name)) names.Add(name);
    }

    /// <summary>
    /// The column a reference is published under: its role, not the raw member that held the id. A
    /// row says the resource it costs, not the <c>resourceId</c> it was read from.
    /// </summary>
    private static string WireName(string name)
    {
        if (string.Equals(name, "uuid", StringComparison.Ordinal)) return name;
        if (name.EndsWith("Uuid", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 4);
        return name.EndsWith("Id", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - 2)
            : name;
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

    /// <summary>
    /// Whether the published string is already the plain decimal spelling of that integer, which is
    /// the only case the cardinal rewrite may take.
    /// </summary>
    /// <remarks>
    /// A producer that holds a count as a magnitude publishes it in the screen's Scientific style,
    /// where anything at or above a thousand is a rounded two-digit reading. Re-parsing that reading
    /// into an <c>int</c> did two things at once: it put the same quantity on the wire in two
    /// notations in one response (a type's <c>level=10300</c> beside <c>value: 1.03e4</c> in the
    /// worth block under it), and it published a precision the reading never carried — every level
    /// from 10,250 to 10,349 came back as exactly <c>10300</c>. Below a thousand the screen writes
    /// the number plainly, the two spellings are the same characters, and the rewrite is free.
    /// </remarks>
    private static bool IsPlainSpelling(string raw, int cardinal) =>
        string.Equals(
            cardinal.ToString(CultureInfo.InvariantCulture), raw, StringComparison.Ordinal);

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
