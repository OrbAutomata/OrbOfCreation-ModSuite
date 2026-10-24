#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

using static OrbAutomata.GameMcp.GameMcpWireValues;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWireShape
{

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
    internal static void DropDefaults(JObject item)
    {
        if (item["isPercent"] is JValue { Type: JTokenType.Boolean } percent && !(bool)percent)
            item.Remove("isPercent");
        if (item["order"] is JValue { Type: JTokenType.Integer } order && (long)order == 0)
            item.Remove("order");
    }

    internal static void FlattenDetails(JObject item)
    {
        if (item["details"] is not JObject details) return;
        foreach (var property in new List<JProperty>(details.Properties()))
        {
            if (item[property.Name] is null)
                item[property.Name] = property.Value;
        }
        item.Remove("details");
    }

    internal static void FlattenReading(JObject item)
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
    internal static void KeepReasonBesideItsCode(JObject item)
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

    internal static void Rename(JObject item, string from, string to)
    {
        if (item[from] is not JToken value) return;
        if (item[to] is null) item[to] = value;
        item.Remove(from);
    }

    internal static void NormalizeCode(JObject item, string field)
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
    internal static void DeduplicateRowVerdict(JObject item)
    {
        if (item["predicates"] is not JObject predicates ||
            (predicates["available"] is not JObject && predicates["visible"] is not JObject) ||
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
    /// these is a coarser second opinion: where it agrees it is the same opinion twice, and where
    /// it disagrees it is two answers to one question, which is worse.
    /// </summary>
    internal static readonly (string Predicate, string Action)[] PredicateActions =
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
    /// The rule is per field and it turns on the duplicate being present, not on the two agreeing.
    /// Agreement was the wrong test: three augments of one live round printed
    /// <c>canDiscover: yes</c> under a row saying <c>discover: no (ERR_LOCKED)</c>, so the coarser
    /// answer survived precisely where it contradicted the screen. The row's block is the screen's
    /// truth — it knows the costs, the owning page and whether that page is drawn — and one
    /// question gets one answer. A <c>canDiscover</c> whose row publishes no <c>discover</c> action
    /// still prints, because then nothing else answers it. A predicate carrying anything of its own
    /// still prints, which is why <c>canUse: yes slots=[1]</c> keeps its slot list — that list
    /// appears nowhere else on the page. A blocked axis prints in full, because the numbers behind a
    /// no are what a caller acts on; an axis whose whole content is that it is not blocking says
    /// what an unlisted axis already says, and <c>blockers</c> then reads as the list of what
    /// blocks. Both keys survive even when everything under them goes: an entity with no applicable
    /// predicate and one whose predicates were never evaluated are different answers, and an omitted
    /// key says both.
    /// </para>
    /// </remarks>
    internal static void DropRowRestatements(JObject item)
    {
        if (item["predicates"] is JObject predicates && item["row"] is JObject row)
        {
            for (var index = 0; index < PredicateActions.Length; index++)
            {
                var pair = PredicateActions[index];
                if (predicates[pair.Predicate] is not JObject verdict) continue;
                if (row[pair.Action] is not JObject) continue;
                if (!CarriesOnlyAVerdict(verdict)) continue;
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

    /// <summary>Whether a predicate block is nothing but a verdict about the row's own action.</summary>
    /// <remarks>
    /// A predicate with a field of its own is never a copy of anything: the field is the finding,
    /// and the verdict it sits beside is what gives it its meaning.
    /// </remarks>
    internal static bool CarriesOnlyAVerdict(JObject predicate)
    {
        foreach (var property in predicate.Properties())
            if (property.Name is not ("available" or "reasonCode" or "reason")) return false;
        return true;
    }
}
#endif
