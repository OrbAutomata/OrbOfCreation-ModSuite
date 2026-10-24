#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;


namespace OrbAutomata.GameMcp;

internal static class GameMcpWireValues
{

    /// <summary>
    /// One price shape wherever a price is said: what it asks, what you hold, whether that covers
    /// it — and the resource it is about, which every reference lands after.
    /// </summary>
    /// <remarks>
    /// Projectors supply the price and holding in the player's units. Their common column order is
    /// independent of the producer's insertion order, and keeps the resource reference after them.
    /// </remarks>
    internal static void OrderPriceColumns(JObject item)
    {
        for (var index = PriceColumns.Length - 1; index >= 0; index--)
        {
            if (item.Property(PriceColumns[index]) is not JProperty property) continue;
            var value = property.Value;
            property.Remove();
            item.AddFirst(new JProperty(PriceColumns[index], value));
        }
    }

    internal static readonly string[] PriceColumns = { "cost", "spendableAmount", "affordable" };

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
    internal static bool IsPlayerMagnitude(string field) => field switch
    {
        "amount" or
        "baseCost" or "effectiveCost" or "groupCost" or "totalCost" or "cost" or
        "capacity" or "netRatePerSecond" or "yield" or
        "startingAmount" or "maximumCarry" or
        "developmentProgress" => true,
        _ => false,
    };

    internal static bool IsBoundedInContext(JObject item, string field) =>
        field == "capacity" && item["slot"] is not null && item["empty"] is not null;

    internal static bool IsArrayReadResult(JObject item) =>
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
    internal static bool IsPlainSpelling(string raw, int cardinal) =>
        string.Equals(
            cardinal.ToString(CultureInfo.InvariantCulture), raw, StringComparison.Ordinal);

    internal static bool IsBoundedCardinal(string field) => field switch
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

    internal static string CanonicalCode(string value) => value switch
    {
        "amount_not_available" or "usage_unavailable" => "amount_unavailable",
        _ => value,
    };
}
#endif
