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



namespace OrbAutomata.GameMcp;

internal static class GameMcpRowKit
{

    /// <summary>One duration in the exact form the game prints a time variable in.</summary>
    /// <remarks>
    /// <para>
    /// <c>Time Played</c> is a <c>DoubleVariable</c> the game marks <c>isTimeVariable</c> and
    /// <c>isTimeAccurateVariable</c>, which sends it through <c>Utils.BeautifyTimeUltraPrecise</c>
    /// on every screen that draws it: under a minute it is the number and <c>s</c>; over a Julian
    /// year it is the number of years and <c>y</c>; between the two it is hours, minutes and
    /// seconds, each padded to two digits, with leading empty units dropped — so a run of two
    /// hours reads <c>02:07:41</c> and one of seven minutes reads <c>07:41</c>.
    /// </para>
    /// <para>
    /// The two number branches print through the suite's own formatter rather than the game's, for
    /// the same reason every other magnitude on this wire does: one notation per response.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One duration in whichever of the game's two time formats the variable is marked for.
    /// </summary>
    /// <remarks>
    /// <c>NumberVariable.GetValueDisplay()</c> reads <c>isTimeAccurateVariable</c> only after
    /// <c>isTimeVariable</c> is set, and the two formats are not interchangeable: the ultra-precise
    /// one counts hours, minutes and seconds, and the accurate one names the largest unit that fits
    /// and says only that.
    /// </remarks>
    internal static string Clock(BigDouble seconds, bool ultraPrecise) =>
        ultraPrecise ? RunClock(seconds) : CoarseClock(seconds);

    /// <summary>
    /// The game's two duration formats live in <see cref="GameDurationText"/>, because the
    /// spell-loadout boundary prints countdowns too and a countdown must not read two ways.
    /// </summary>
    internal static string CoarseClock(BigDouble seconds) => GameDurationText.Accurate(seconds);

    /// <summary>
    /// The row a reflected projection reads, with a time variable's value already in the game's own
    /// clock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Substituting the row rather than hand-writing the projection keeps the whole declaration
    /// intact: a table still prints every column it promised and a detail block still says a false
    /// <c>isPercent</c> by being silent, because those are properties of the declared paths and the
    /// paths did not change. Only the type behind <c>value</c> did.
    /// </para>
    /// </remarks>
    internal static object AsDisplayed(object row) =>
        row is WorldNumberVariable variable && variable.IsTime
            ? new TimeVariableRow(
                variable.EntityId,
                Clock(variable.Value, variable.IsTimeAccurate),
                variable.IsPercent)
            : row is WorldModifierVariable modifier
            ? new ModifierVariableRow(
                modifier.EntityId,
                GameMcpNativeVocabulary.ModifierMagnitude(
                    modifier.ModifierType, modifier.Amount),
                modifier.Order)
            : row;

    /// <summary>
    /// A registry modifier as the screen writes it: the kind and the magnitude are one string,
    /// because the kind is what decides how the magnitude is written.
    /// </summary>
    /// <remarks>
    /// Substituted rather than hand-projected, for the reason the time variable above is: the
    /// declared column list stays the row's whole story, and only the type behind it changes.
    /// </remarks>
    internal readonly struct ModifierVariableRow
    {
        internal ModifierVariableRow(Guid entityId, string amount, int order)
        {
            EntityId = entityId;
            Amount = amount;
            Order = order;
        }

        internal Guid EntityId { get; }
        internal string Amount { get; }
        internal int Order { get; }
    }

    /// <summary>
    /// A number variable whose value the game draws as a duration, in the exact member names the
    /// two variable categories declare.
    /// </summary>
    internal readonly struct TimeVariableRow
    {
        internal TimeVariableRow(Guid entityId, string value, bool isPercent)
        {
            EntityId = entityId;
            Value = value;
            IsPercent = isPercent;
        }

        internal Guid EntityId { get; }
        internal string Value { get; }
        internal bool IsPercent { get; }
    }

    /// <summary>
    /// A reference column's value: the entity, or the word for naming none. The zero identity is
    /// an address nothing answers to, and dropping it would take the column with it on a page
    /// where no row names one.
    /// </summary>
    internal static object Named(Guid entityId) =>
        entityId == Guid.Empty ? GameMcpListColumns.Absent : entityId;

    /// <summary>
    /// The per-level conditions on the next purchase, as the second half of the can-purchase axis.
    /// </summary>
    /// <remarks>
    /// This is the branch <c>UIUpgradeButton.RenderContent()</c> takes when it replaces the price
    /// with a requirements notice, and it is a fact about the next press rather than about how far
    /// the row has come: a row whose requirements are unmet is still
    /// <see cref="GameMcpListColumns.Available"/>, on the panel, one condition away. An
    /// unevaluable verdict keeps its own word rather than borrowing <c>unmet</c>, because the two
    /// resolve differently — one waits for the save to progress, the other waits for this suite.
    /// </remarks>
    internal static string RequirementWord(WorldRequirementVerdict verdict) => verdict switch
    {
        WorldRequirementVerdict.Met => GameMcpListColumns.Met,
        WorldRequirementVerdict.Unmet => GameMcpListColumns.Unmet,
        _ => GameMcpListColumns.Unmodelled,
    };

    internal static GameWorldState? Before(GameMcpCommand command) =>
        command.FrameContext?.World?.Snapshot;

    internal static GameMcpValue EntityReference(
        GameWorldState world,
        Guid id,
        int quantity = 0)
    {
        var identity = EntityIdentityFormatter.Describe(id, world.EntityIdentities);
        var result = new JObject { ["uuid"] = id.ToString("D") };
        if (identity.HasName) result["name"] = identity.Name;
        if (quantity > 0) result["amount"] = quantity;
        return result.Freeze();
    }

    internal static GameMcpValue? Property(GameMcpValue? value, string name)
    {
        if (value is not GameMcpObject instance) return null;
        for (var index = 0; index < instance.Properties.Count; index++)
        {
            var property = instance.Properties[index];
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return property.Value;
        }
        return null;
    }

    internal static GameMcpValue Change(
        Guid uuid,
        object? before,
        object? after,
        string field) => new JObject
    {
        ["uuid"] = uuid.ToString("D"),
        [field] = new JObject { ["before"] = before, ["after"] = after },
    }.Freeze();

    /// <summary>
    /// The one answer a mutation the game queues is allowed to give: it worked, and this much is
    /// queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A queue hop is not a settled fact. The counts move again while the answer is being written and
    /// have moved back by the time the caller reads it, so a <c>{before, after}</c> pair over them
    /// reports a transition nobody can confirm — and the round that shipped one reported a purchase
    /// that landed on level 1 as <c>level: 0 -&gt; 0</c>, whose honest reading is "it failed". A loop
    /// driven off that either spins forever or buys twice. The press is what is knowable, so the
    /// press is what is said, and where the queue stands now is a read.
    /// </para>
    /// <para>
    /// A delivery short of the ask says both numbers on one line, because the difference is the whole
    /// fact and a partial that looks like a satisfied <c>amount=1</c> is the shape a caller cannot
    /// act on. An observed count is always said as that number, including the count of one: these
    /// verbs promise "how many", and answering the commonest press with a bare <c>yes</c> spends the
    /// one word that could carry the observation on saying nothing, leaving a caller to re-read the
    /// entity to learn what its own commit did. An unknown count says only that the press queued
    /// something: naming a number the evidence does not carry would be the same defect wearing a
    /// confident face. A count observed as nought or less is unknown rather than nought — this is
    /// only reached once the mutation's own sentinel has proved at least one entry was made, so a
    /// queue showing no growth has drained already, and reporting "0 of 1 asked" for a press that
    /// landed is the very lie being fixed.
    /// </para>
    /// </remarks>
    internal static GameMcpValue QueuedMutation(
        Guid uuid,
        int asked,
        int? queued,
        string? remainder = null) => new JObject
    {
        ["uuid"] = uuid.ToString("D"),
        ["queued"] = QueuedValue(asked, queued, remainder),
    }.Freeze();

    internal static object QueuedValue(int asked, int? queued, string? remainder = null) =>
        queued is null or < 1
            ? true
            : queued < asked
                ? queued + " of " + asked + " asked; " +
                  (string.IsNullOrEmpty(remainder)
                      ? "the game took no more this press."
                      : remainder)
                : queued.Value;

    /// <summary>
    /// A press that landed on something the settled world draws nothing for any more.
    /// </summary>
    /// <remarks>
    /// Twenty-three sites each wrote their own wording of "the settled world has no X row for the
    /// committed target" — the suite's own publication, in the suite's own words, about the one
    /// question the caller actually has. What they can act on is the same at all twenty-three: the
    /// press went through, and its result has to be read somewhere else. The caller names the thing
    /// in full, including its article, so the sentence reads as a sentence on every one of them.
    /// </remarks>
    internal static GameMcpValue PostStateNotPublished(string thing) =>
        PostStateUnavailable("post_state_not_published", NotShowing(thing));

    internal static GameMcpValue PostStateNotPublished(
        Guid uuid,
        int requestedAmount,
        string thing) =>
        PostStateUnavailable(
            uuid, requestedAmount, "post_state_not_published", NotShowing(thing));

    internal static string NotShowing(string thing) =>
        "The press went through, but the game is no longer showing " + thing +
        ", so its new state cannot be read back.";

    internal static GameMcpValue PostStateUnavailable(string reasonCode, string reason) =>
        new JObject
        {
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            },
        }.Freeze();

    /// <summary>
    /// An unobservable post-state still names what was acted on and how much was asked for. Being
    /// honest that the settled world proves nothing is right; dropping the identity with it made
    /// two commits on two different recipes byte-identical.
    /// </summary>
    internal static GameMcpValue PostStateUnavailable(
        Guid uuid,
        int requestedAmount,
        string reasonCode,
        string reason)
    {
        var result = new JObject
        {
            ["uuid"] = uuid.ToString("D"),
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            },
        };
        if (requestedAmount > 0) result["requestedAmount"] = requestedAmount;
        return result.Freeze();
    }

    // Levels, slots, counts, and enum discriminants are bounded cardinals, not game-domain
    // magnitudes. Keep them as JSON numbers; only BigDouble values use scientific strings.
    internal static int Number(int value) => value;
    internal static string IncompleteRowNote(bool requirementGap) =>
        requirementGap
            ? GameMcpDecisionReason.For("unmodeled_requirement_leaf") +
              " world_get on this uuid names the requirement."
            : GameMcpDecisionReason.For("offer_not_in_explainable_world");
    internal static GameMcpValue WithIncompleteNote(GameMcpValue projected, string note)
    {
        switch (projected)
        {
            case GameMcpObject frozen:
            {
                var row = new JObject();
                row.CopyFrom(frozen);
                row["incomplete"] = note;
                return row.Freeze();
            }
            case GameMcpProjectedDomainValue reflected:
            {
                var blocks = new JObject();
                if (reflected.Attached is not null) blocks.CopyFrom(reflected.Attached);
                blocks["incomplete"] = note;
                return reflected.With(blocks.Freeze());
            }
            default:
                return projected;
        }
    }

    internal static GameMcpValue WithOwnIdentity(
        GameMcpEntityCapabilityDescriptor category,
        GameMcpValue projected)
    {
        if (string.Equals(category.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal))
            return projected;
        if (projected is GameMcpProjectedDomainValue domain)
            return domain.WithoutAddressableIdentity();
        if (projected is not GameMcpObject frozen) return projected;
        var result = new JObject();
        result.CopyFrom(frozen);
        if (result["uuid"] is not null) return projected;
        result["category"] = category.Name;
        result["addressable"] = false;
        return result.Freeze();
    }



    internal static string RunClock(BigDouble seconds) => GameDurationText.UltraPrecise(seconds);

    /// <summary>
    /// The spell-slot list row, addressed the way <c>game_cast</c> and <c>game_spell_loadout</c>
    /// take it. The equipped spell's own runtime id stays off the row: it is in no catalog, so it
    /// resolves for nobody, and the recipe carries the same name.
    /// </summary>
    /// <remarks>
    /// Whether the slot is casting right is not on the row. The fact keeps every surface that
    /// answers for the instant — the scan, the <c>world_get</c> detail, and the <c>game_cast</c>
    /// response, which is the one a caller deciding its next press should be reading anyway.
    /// </remarks>

    internal static GameMcpProjectedDomainValue ReflectedDetail(
        GameMcpEntityCapabilityDescriptor category,
        object row) =>
        new(
            AsDisplayed(row),
            category.ScanFields,
            category.Name,
            category.ExpectedNativeType,
            tableRow: false);
}
#endif
