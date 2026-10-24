#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

using static OrbAutomata.GameMcp.GameMcpWireIdentity;
using static OrbAutomata.GameMcp.GameMcpWireShape;
using static OrbAutomata.GameMcp.GameMcpWireValues;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEntityWireNormalizer
{
    internal static JToken Normalize(
        JToken source,
        EntityIdentityCatalogSnapshot catalog)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        // Which verb is answering, read off the keys only a read page has: `rows` is a table's
        // rows, `results` is `world_get`'s blocks, and `row` is one entity's. Nothing else on the
        // surface answers with any of the three. A press, a configuration write and the two
        // previews carry none of them, which is why the classes keep crossing the wire there.
        var readPage = source is JObject page &&
            (page["rows"] is JArray || page["results"] is JArray || page["row"] is JObject);
        NormalizeToken(source, catalog, readPage, inRow: false);
        return source;
    }

    internal static void NormalizeToken(
        JToken token,
        EntityIdentityCatalogSnapshot catalog,
        bool readPage,
        bool inRow,
        bool inTable = false)
    {
        if (token is JObject item)
        {
            NormalizeObject(item, catalog, readPage, inRow, inTable);
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
            if (value is not null)
                NormalizeToken(value, catalog, readPage, inRow, inTable: true);
        }
    }

    internal static void NormalizeObject(
        JObject item,
        EntityIdentityCatalogSnapshot catalog,
        bool readPage,
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
        if (item["resource"] is not null || item["resourceId"] is not null) OrderPriceColumns(item);

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
            else if (status is "refused" or "failed" or "rejected" or "skipped" or "faulted")
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
        // A no that already carries its own sentence is not bare. Producers that hold the numbers
        // write the better sentence and skip the code entirely, and inventing one over the top of
        // theirs filed a stated limit as a lock the game had published no condition for.
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

                // A fact row prints a verdict word and a sentence. The eight classes are a press
                // vocabulary — the axis a caller branches on when a press refuses, and the thing a
                // preview promises that press will answer — and on a page of facts they read as
                // refusals of actions nobody asked for: `inLedger: no (ERR_LOCKED)` on a resource
                // the game simply has not counted yet, `loadoutAdd: no (ERR_LIMIT)` on a row that
                // was only ever describing the bar. The sentence carries the whole of the no here,
                // and the key it sits under says which question it answered.
                //
                // A block carrying a `status` is the exception on a read page, and it is not a fact
                // block: it is the read itself answering that it could not serve this id. That is a
                // refusal like any other, it names the verb that can answer, and a caller branches
                // on its class exactly as it branches on a press's.
                if (readPage && item["status"] is null) item.Remove("reasonCode");
                else item["reasonCode"] = GameMcpDecisionReason.Class(code);
                KeepReasonBesideItsCode(item);
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
                readPage,
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
}
#endif
