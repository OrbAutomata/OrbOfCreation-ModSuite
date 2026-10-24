#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

using static OrbAutomata.GameMcp.GameMcpEntityIdentity;
using static OrbAutomata.GameMcp.GameMcpEntityPredicates;

namespace OrbAutomata.GameMcp;

internal static class GameMcpRequirementProjection
{
    internal const int MaximumRequirementExpansionDepth = 32;

    internal static JObject? Requirements(GameWorldState world, Guid id, EntityKind kind)
    {
        // A resource's only authored gate is the container CheckVisibility() consults, so the block
        // it publishes is that container and nothing else: there is no per-level program to state a
        // suite verdict about, and no native parity probe answers for a ResourceSO. While the game
        // already counts it, there is no lock to explain.
        if (kind == EntityKind.Resource)
        {
            if (!WorldLookup.TryFind(world.Resources, id, out var resource) ||
                resource.Reading.Visible)
            {
                return null;
            }
            var unlocks = ProjectUnlockConditions(world, id);
            return unlocks is null ? null : new JObject { ["unlocksWhen"] = unlocks };
        }
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade and not EntityKind.Research)
            return null;

        var checkLevel = kind switch
        {
            EntityKind.Upgrade => UpgradeCheckLevel(world, id),
            EntityKind.Research when WorldLookup.TryFind(world.Research, id, out var research) =>
                research.EffectiveRequirementLevel,
            _ => StructureCheckLevel(world, id),
        };
        var suite = WorldRequirementEvaluator.Evaluate(world, id, checkLevel);
        var lockedByUnlockGate = IsLockedByItsUnlockGate(world, id, kind);
        var unmet = new JArray();
        var root = ProjectRequirementContainer(
            world,
            id,
            containerIndex: 0,
            checkLevel,
            new HashSet<RequirementKey>(),
            depth: 0,
            unmet);
        // The verdict is the screen's, not one container's. The rows below are the *next level's*
        // container; the game's own lock is `UpgradeSO.prerequisites` / `StructureSO.prerequisites`
        // / `ResearchSO.visibilityPrerequisites`, a different authored list the reader never walks.
        // While the block published the per-level answer alone it said `Met` beside `state: locked`
        // on the same response, three rounds running.
        var verdict = suite == WorldRequirementVerdict.Unevaluable || !lockedByUnlockGate
            ? suite
            : WorldRequirementVerdict.Unmet;
        var requirements = new JObject
        {
            ["suiteVerdict"] = verdict.ToString(),
        };
        if (lockedByUnlockGate) requirements["reasonCode"] = "unlock_conditions_unmet";

        // What would actually unlock it, which the block could not say while the unlock containers
        // were uncaptured. Only while the gate is shut: an entity the game already lets through has
        // no lock to explain, and its latched `available` means the game would not walk these
        // conditions again either.
        if (lockedByUnlockGate)
        {
            var unlocksWhen = ProjectUnlockConditions(world, id);
            if (unlocksWhen is not null) requirements["unlocksWhen"] = unlocksWhen;
        }

        // The line a player can act on, first. Everything a locked entity is waiting for, each row
        // naming the thing, what it asks and what is held — read straight off the leaves below, so
        // it can never disagree with them. A round read one of these out of column ten of a
        // twenty-column table and wrote down that it was buried; a reader who needs no more than
        // this now stops at the second line of the block.
        // No block-wide `checkLevel`. It is the level the thresholds scale to, which matters only on
        // the rows whose threshold scales at all — those carry it as `forLevel` — and on every other
        // entity it was a number printed once per read that changed nothing.
        if (unmet.Count > 0) requirements["unmet"] = unmet;
        requirements["root"] = root;

        // No `authority` paragraph. It was 178 bytes of fixed prose asserting that the authored
        // rows are met and are not what holds the entity shut — the claim `suiteVerdict: Met`
        // already makes one line above it, and the gap it was written to name is what
        // `predicates.available` says with its own reason code. A live round emitted it nine times,
        // byte-identical, and in eight of those it sat under `root: no conditions`: it declared a
        // set of rows met for entities that have no rows at all.
        return requirements;
    }

    /// <summary>
    /// Whether the game's own unlock gate — the container the suite does not read — is what holds
    /// this entity shut.
    /// </summary>
    /// <remarks>
    /// Each owner family keeps two authored containers and the suite walks only the per-level one:
    /// <c>UpgradeSO.IsVisible()</c> and <c>IsAvailable()</c> are <c>prerequisites.Check()</c>,
    /// <c>StructureSO.IsAvailable()</c> is <c>prerequisites.Check()</c> (and its
    /// <c>IsVisible()</c> is that same call), and <c>ResearchSO.IsVisible()</c> is
    /// <c>visibilityPrerequisites.Check() &amp;&amp; levelVisibilityPrereq.Check()</c> — while
    /// <c>HasMetQueuedLevelRequirements()</c>, <c>HasMetLevelRequirements()</c> and
    /// <c>MeetsLevelRequirements()</c> all ask <c>prerequisitesPerLevel</c> /
    /// <c>levelPrerequisites</c>. The world already publishes each family's native answer, so the
    /// lock is read from that rather than re-derived from a container nobody captured.
    /// <para>
    /// An upgrade at its ceiling reports <c>IsAvailable()</c> false for a reason that is not a lock,
    /// so being exhausted is excluded here; <c>state</c> and the cap blocker already say it.
    /// </para>
    /// </remarks>
    internal static bool IsLockedByItsUnlockGate(GameWorldState world, Guid id, EntityKind kind) =>
        kind switch
        {
            EntityKind.Upgrade => WorldLookup.TryFind(world.Upgrades, id, out var upgrade) &&
                !upgrade.Reading.Available && !upgrade.IsExhausted,
            EntityKind.Structure => WorldLookup.TryFind(world.Structures, id, out var structure) &&
                !structure.Reading.Unlocked,
            EntityKind.Research => WorldLookup.TryFind(world.Research, id, out var research) &&
                !research.Visible,
            _ => false,
        };

    /// <summary>
    /// The conditions behind the game's own unlock gate, worded like every other requirement row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read at level nought, which is the level the no-argument <c>Container.Check()</c> walks its
    /// conditions at, with each row carrying the container's own threshold adjustment. It is not the
    /// asking entity's check level: an unlock container has no level of its own.
    /// </para>
    /// <para>
    /// Its unmet leaves are deliberately not folded into the block's <c>unmet</c> summary. That list
    /// documents itself as the next level's, and two authored lists sharing one summary would leave a
    /// caller unable to tell which question a line answers — the exact confusion the lock verdict was
    /// fixed to end. Every leaf here already carries its own <c>needs</c> and <c>met</c>, and a lock is
    /// a short list.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether this entity has an unlock container the read would actually print, asked by running
    /// the same producer the block is built from rather than a second rule about containers.
    /// </summary>
    internal static bool HasUnlockConditions(GameWorldState world, Guid ownerId) =>
        ProjectUnlockConditions(world, ownerId) is not null;

    internal static JObject? ProjectUnlockConditions(GameWorldState world, Guid ownerId)
    {
        if (!WorldEntityRequirementLookup.TryFindContainerRange(
                world.EntityRequirements, ownerId, containerIndex: 0, out var start, out var count))
        {
            return null;
        }

        var root = ProjectRequirementContainer(
            world, ownerId, containerIndex: 0,
            WorldRequirementEvaluator.UnlockCheckLevel,
            new HashSet<RequirementKey>(), depth: 0, new JArray(),
            WorldRequirementProgramKind.Unlock);
        var children = (JArray)root["children"]!;

        // Nothing captured means nothing honest to say. An empty operator node would assert a
        // structure this entity does not have and read as a lock with no conditions.
        return children.Count == 0
            ? null
            : new JObject { ["operator"] = "AND", ["children"] = children };
    }

    /// <summary>
    /// Why the Research grid has no tile to press: <c>ResearchSO.IsVisible()</c> is false, and the
    /// unlock conditions still unmet are what it is waiting for. The press and both reads answer
    /// with this one sentence.
    /// </summary>
    internal static string LockedResearchReason(GameWorldState world, Guid researchId)
    {
        var conditions = ProjectUnlockConditions(world, researchId)?.Freeze();
        var needs = conditions?["children"] is GameMcpArray children
            ? UnmetNeeds(children.Items, " and ")
            : string.Empty;
        return needs.Length == 0
            ? "This research is locked, so the Research grid draws no tile for it, and the game " +
                "says nothing about what would unlock it."
            : "This research is locked, so the Research grid draws no tile for it. It unlocks " +
                "with " + needs + ".";
    }

    internal static string UnmetNeeds(IReadOnlyList<GameMcpValue> children, string separator)
    {
        var parts = new List<string>(children.Count);
        foreach (var child in children)
        {
            if (child is not GameMcpObject node || IsMet(node)) continue;
            if (node["children"] is GameMcpArray arms)
            {
                var joined = UnmetNeeds(
                    arms.Items, Text(node["operator"]) == "OR" ? " or " : " and ");
                parts.Add(children.Count > 1 && arms.Items.Count > 1 ? "(" + joined + ")" : joined);
            }
            else
            {
                parts.Add(Text(node["needs"]) ?? "a condition the suite cannot read");
            }
        }
        return string.Join(separator, parts);
    }

    internal static bool IsMet(GameMcpObject node)
    {
        if (node["children"] is not GameMcpArray arms)
            return node["met"] is GameMcpScalar { Value: true };
        var any = Text(node["operator"]) == "OR";
        foreach (var arm in arms.Items)
        {
            var met = arm is GameMcpObject armNode && IsMet(armNode);
            if (any && met) return true;
            if (!any && !met) return false;
        }
        return !any;
    }

    internal static JObject ProjectRequirementContainer(
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth,
        JArray unmet,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel)
    {
        var key = new RequirementKey(ownerId, containerIndex, program);
        if (depth > MaximumRequirementExpansionDepth)
            return Refusal("requirement_depth_exceeded", ownerId, containerIndex);
        if (!trail.Add(key)) return Refusal("requirement_cycle", ownerId, containerIndex);
        try
        {
            var children = new JArray();
            if (WorldEntityRequirementLookup.TryFindContainerRange(
                    world.EntityRequirements, ownerId, containerIndex, out var start, out var count))
            {
                var rows = world.EntityRequirements.AsSpan();
                for (var offset = 0; offset < count; offset++)
                {
                    ref readonly var row = ref rows[start + offset];
                    if (row.Program != program || row.ParentOrdinal >= 0) continue;
                    children.Add(ProjectRequirementNode(
                        world, rows, start, count, in row, checkLevel, trail, depth + 1, unmet));
                }
            }
            var result = new JObject
            {
                ["status"] = "available",
                ["ownerUuid"] = ownerId.ToString("D"),
                ["tierIndex"] = containerIndex,
                ["operator"] = "AND",

                // An operator node always carries its list. Dropping an empty one left a node
                // asserting a structure it did not have, and read the same as one nobody expanded.
                ["children"] = children,
            };
            return result;
        }
        finally
        {
            trail.Remove(key);
        }
    }

    internal static JObject ProjectRequirementNode(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        in WorldEntityRequirement row,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth,
        JArray unmet)
    {
        if (row.NodeKind == WorldRequirementNodeKind.Group)
        {
            var children = new JArray();
            for (var offset = 0; offset < count; offset++)
            {
                ref readonly var child = ref rows[start + offset];
                if (child.Program != row.Program || child.ParentOrdinal != row.Ordinal) continue;
                children.Add(ProjectRequirementNode(
                    world, rows, start, count, in child, checkLevel, trail, depth + 1, unmet));
            }
            // The node is its operator and its arms. `nodeKind`, `ordinal`, `parentOrdinal` and
            // `depth` said where it sat in a tree the reader is already holding: the nesting is the
            // nesting, and the order is the authored order.
            return new JObject
            {
                ["operator"] = row.Operator.ToString().ToUpperInvariant(),
                ["children"] = children,
            };
        }

        // A condition class this build does not model has nothing to word and no target to name, so
        // it says exactly that, in one sentence, and points at the screen that does draw it. It used
        // to answer `unsupported_requirement_value` — the sentence for a *comparison* nobody
        // modelled — beside a `conditionType` column carrying the game's own class name.
        if (row.Kind == WorldRequirementConditionKind.Unknown)
        {
            return new JObject
            {
                ["met"] = false,
                ["reasonCode"] = "requirement_class_unread",
                ["reason"] = UnreadRequirementSentence(world, in row),
            };
        }

        var evaluated = WorldRequirementEvaluator.ExplainLeaf(world, in row, checkLevel);

        // What the row wants, in the words the screen uses for it, then whether it holds. Those are
        // the two facts a player acts on. Everything the leaf used to carry beside them — the tree
        // position, the native class, the selected value's internal name, and three thresholds of
        // which only one is ever displayed — was the suite explaining itself: a live round met the
        // one usable line in column ten of twenty and wrote down that it was buried.
        var tierUnmet = new JArray();
        var tier = row.Kind == WorldRequirementConditionKind.PrerequisiteLink
            ? LinkTierConditions(world, in row, in evaluated, checkLevel, trail, depth + 1, tierUnmet)
            : null;
        var sole = tier is { Count: 1 } ? SoleLeaf(tier.Items[0]) : null;
        var needs = tier is null
            ? RequirementNeeds(world, in row, in evaluated)
            : TierNeeds(tier.Items, " and ");
        var target = tier is null
            ? row.TargetId
            : Guid.TryParse(Text(sole?["requirementUuid"]), out var soleTarget)
                ? soleTarget
                : Guid.Empty;
        var leaf = new JObject
        {
            ["needs"] = needs,
            ["met"] = evaluated.Met,
        };

        // The level the threshold was scaled to, on the rows whose threshold scales at all. On every
        // other row it is a number that changes nothing, which is why it is not a block-wide field.
        if (ThresholdScales(in row)) leaf["forLevel"] = checkLevel;

        // A met or unmet row is fully answered by the two fields above. Only a row the suite could
        // not evaluate carries a class and a sentence, because only that one asks the reader to do
        // something else — read it on the game's own tooltip.
        if (evaluated.Verdict == WorldRequirementVerdict.Unevaluable)
            leaf["reasonCode"] = evaluated.ReasonCode;

        if (target != Guid.Empty) leaf["requirementUuid"] = target.ToString("D");
        if (!evaluated.Met && tierUnmet.Count > 0)
        {
            foreach (var condition in tierUnmet.Items) unmet.Add(condition);
        }
        else if (!evaluated.Met && target != Guid.Empty)
        {
            unmet.Add(new JObject
            {
                ["requirementUuid"] = target.ToString("D"),
                ["needs"] = needs,
            });
        }
        if (row.Kind == WorldRequirementConditionKind.PrerequisiteLink && !evaluated.Met)
        {
            if (TierCount(world, row.TargetId) is var tiers and > 1) leaf["otherTiers"] = tiers - 1;
            if (tier is null && TierPublished(world, in row, in evaluated) is false)
                leaf["reasonCode"] = "tier_not_published";
            if (tier is not null && sole is null) leaf["children"] = tier;
        }
        return leaf;
    }

    /// <summary>
    /// Which tier of a gate this row asks about: its own threshold, or the base tier for a row
    /// that names none.
    /// </summary>
    internal static long SelectedTier(
        in WorldEntityRequirement row,
        in WorldRequirementLeafEvaluation evaluation) =>
        row.ReqType == 0 ? 0L : BigDouble.Round(evaluation.ScaledThreshold).ToLong();

    /// <summary>
    /// The conditions of the one tier this gate row asks about, or <see langword="null"/> when the
    /// world publishes none for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A gate is a <c>PrerequisiteLinkSO</c>, and the player never sees one: its tooltip node is
    /// hidden and its id answers to no screen. What the game checks through it is
    /// <c>LinkDefinition.IsEnabled()</c> — the tier's own container, and the gate's binding — so the
    /// row keeps the gate's <c>met</c> and words its <c>needs</c> from the tier's conditions. Named
    /// after the asset it read "the InnovationTiers gate", a name no screen draws, beside an id
    /// whose read was a dead end.
    /// </para>
    /// <para>
    /// A tier of one condition is that condition, so the row points at its target and carries no
    /// children. A tier of several lists them as <c>children</c> while the gate is shut, in the
    /// same leaf shape as every other requirement, and <c>otherTiers</c> counts the link's other
    /// rungs. Frame counters and enabled flags are the game's bookkeeping and stay off the wire.
    /// </para>
    /// </remarks>
    internal static JArray? LinkTierConditions(
        GameWorldState world,
        in WorldEntityRequirement row,
        in WorldRequirementLeafEvaluation evaluation,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth,
        JArray unmet)
    {
        if (TierPublished(world, in row, in evaluation) is not true) return null;
        var container = ProjectRequirementContainer(
            world, row.TargetId, (int)SelectedTier(in row, in evaluation), checkLevel, trail, depth,
            unmet);
        return container["children"] is JArray { Count: > 0 } children ? children : null;
    }

    internal static int TierCount(GameWorldState world, Guid linkId)
    {
        var count = 0;
        for (var index = 0; index < world.PrerequisiteLinkTiers.Count; index++)
        {
            if (world.PrerequisiteLinkTiers[index].LinkId == linkId) count++;
        }
        return count;
    }

    /// <summary>
    /// Whether the world publishes the tier this row asks about; <see langword="null"/> for a link
    /// it publishes no tier of at all.
    /// </summary>
    internal static bool? TierPublished(
        GameWorldState world,
        in WorldEntityRequirement row,
        in WorldRequirementLeafEvaluation evaluation)
    {
        var selectedTier = SelectedTier(in row, in evaluation);
        bool? published = null;
        for (var index = 0; index < world.PrerequisiteLinkTiers.Count; index++)
        {
            var tier = world.PrerequisiteLinkTiers[index];
            if (tier.LinkId != row.TargetId) continue;
            published = published is true || tier.TierIndex == selectedTier;
        }
        return published;
    }

    internal static GameMcpObject? SoleLeaf(GameMcpValue node)
    {
        while (node is GameMcpObject { } group && group["operator"] is not null)
        {
            if (group["children"] is not GameMcpArray { Items.Count: 1 } children) return null;
            node = children.Items[0];
        }
        return node is GameMcpObject leaf && leaf["needs"] is not null ? leaf : null;
    }

    internal static string TierNeeds(IReadOnlyList<GameMcpValue> children, string separator)
    {
        var parts = new List<string>(children.Count);
        foreach (var child in children)
        {
            if (child is not GameMcpObject node) continue;
            if (node["children"] is GameMcpArray arms)
            {
                var joined = TierNeeds(arms.Items, Text(node["operator"]) == "OR" ? " or " : " and ");
                parts.Add(arms.Items.Count > 1 && children.Count > 1 ? "(" + joined + ")" : joined);
            }
            else
            {
                parts.Add(Text(node["needs"]) ?? "a condition the suite cannot read");
            }
        }
        return string.Join(separator, parts);
    }

    internal static string? Text(GameMcpValue? value) =>
        value is GameMcpScalar { Value: string text } ? text : null;

    internal static JObject? ResearchThresholds(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind != EntityKind.Research || !WorldLookup.TryFind(world.Research, id, out var research))
            return null;
        // The effective threshold is the number the screen draws, and it is the only one here that
        // was ever news: `baseThreshold` and `scaledThreshold` were the same field read twice, and
        // `selectedValueKind` was the suite naming its own accessor beside the value it read.
        // `nativeStillHasLeeway` went the same way: it was `metWithLeeway`'s own field printed a
        // second time under a name that says where the fact came from rather than what it says.
        var result = new JObject
        {
            ["current"] = research.TotalLevel,
            ["effectiveThreshold"] = research.EffectiveRequirementLevel,
            ["leeway"] = research.Modifiers.LeewayPoints.ToInt(),
            ["metWithLeeway"] = research.StillHasLeeway,
            ["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements,
            ["adjustment"] = research.RequirementLevelAdjustment,
        };
        if (research.RequirementAdjustments.Count > 0)
            result["activeAdjustments"] = new GameMcpDomainValue(
                research.RequirementAdjustments);
        return result;
    }

    internal static long UpgradeCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Upgrades, id, out var row);
        return WorldRequirementEvaluator.UpgradeCheckLevel(in row);
    }

    internal static long StructureCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Structures, id, out var row);
        return WorldRequirementEvaluator.StructureCheckLevel(in row);
    }

    internal static JObject Refusal(string code, Guid ownerId, int tierIndex) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["ownerUuid"] = ownerId.ToString("D"),
        ["tierIndex"] = tierIndex,
    };

    internal static GameMcpValue ProjectNumber(BigDouble value) =>
        new GameMcpDomainValue(value);

    /// <summary>
    /// One requirement as the screen words it: the thing, what is asked of it, and — only while it
    /// is not met — what is held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parenthetical rides on the unmet rows alone. On a satisfied row it restates a number the
    /// reader has no move to make about, and its absence is the same signal <c>met</c> gives.
    /// </para>
    /// <para>
    /// Every check word the vocabulary can produce has a phrase here and the default throws, for the
    /// same reason <see cref="GameMcpNativeVocabulary.RequirementCheck"/>'s arms throw: a comparison
    /// the game ships and this table has no words for is a phrase to write, not a gap to paper over
    /// with the word itself.
    /// </para>
    /// </remarks>
    internal static string RequirementNeeds(
        GameWorldState world,
        in WorldEntityRequirement row,
        in WorldRequirementLeafEvaluation evaluated)
    {
        // `Required` is the met/not-met pair's right-hand side — the leaf evaluator hard-codes it
        // to one for every PrerequisiteLink — so a named-tier gate read "at tier 1" whatever tier
        // it asked for. The tier is the row's own threshold, which is the number the evaluator
        // looks the tier up by.
        // An authored empty composite compares nothing at all: its identity value is the whole of
        // what it says, and Enumerable.All of nothing is true where Enumerable.Any of nothing is
        // false. There is no target to name and no threshold to print.
        if (row.Kind == WorldRequirementConditionKind.Literal)
        {
            return row.ReqType == 1
                ? "nothing — this group is empty"
                : "one of an empty group, which nothing can satisfy";
        }

        var name = RequirementTargetName(world, row.TargetId);
        var (required, current) = evaluated.Met
            ? (GameMcpNumberFormatter.Format(evaluated.Required), string.Empty)
            : GameMcpNumberFormatter.FormatApart(evaluated.Required, evaluated.Current);
        var at = evaluated.Met ? string.Empty : " (at " + current + ")";
        var have = evaluated.Met ? string.Empty : " (have " + current + ")";
        var check = GameMcpNativeVocabulary.RequirementCheck(row.Kind, row.ReqType);
        return check switch
        {
            "any-level" => name + " at any level" + at,
            "at-maximum-level" => name + " at its maximum level " + required + at,
            "at-least-level" => name + " at level " + required + at,
            "at-least-mastery-level" => name + " at mastery level " + required + at,
            "at-least-mastery-ready-level" => name + " at mastery-ready level " + required + at,
            "at-least-maximum-level" => name + " at recipe level " + required + at,
            "at-least-advancement-level" => name + " at advancement level " + required + at,
            "at-least-reached-level" => name + " taken to level " + required + at,
            "at-least-quantity" => required + " " + name + have,
            "at-least-value" => required + " " + name + have,
            "at-least-count" => required + " of " + name + have,
            "at-least-lifetime-quantity" => required + " " + name + " ever gained" + have,
            "at-least-capacity" => required + " " + name + " capacity" + have,
            "in-resource-list" => name + " in your resource list",
            "discovered" => name,
            "visible" => name + " shown",
            "available" => name + " available",
            "any-visible" => "something from " + name,
            "any-available" => "something available from " + name,
            "first-tier-enabled" => "the " + name + " gate",
            "named-tier-enabled" => "the " + name + " gate at tier " +
                SelectedTier(in row, in evaluated).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                "a requirement row reached the wire checking '" + check + "' with no player " +
                "phrase for it; a comparison the game ships is a phrase to write, not a suite word " +
                "to pass through."),
        };
    }

    /// <summary>
    /// The first unmet condition of an entity's usage requirement, in the words its tooltip row
    /// uses, or an empty string where no such condition has words.
    /// </summary>
    internal static string FirstUnmetUsageNeeds(GameWorldState world, Guid ownerId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, ownerId, out var start, out var count))
        {
            return string.Empty;
        }

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.Program != WorldRequirementProgramKind.Usage ||
                row.NodeKind != WorldRequirementNodeKind.Leaf ||
                row.Kind is WorldRequirementConditionKind.Unknown
                    or WorldRequirementConditionKind.Literal
                    or WorldRequirementConditionKind.PrerequisiteLink)
            {
                continue;
            }
            var evaluated = WorldRequirementEvaluator.ExplainLeaf(world, in row, level: 0);
            if (evaluated.Verdict == WorldRequirementVerdict.Unmet)
                return RequirementNeeds(world, in row, in evaluated);
        }

        return string.Empty;
    }

    /// <summary>
    /// The requirement's target, named the way the player sees it, never as a bare UUID in prose.
    /// </summary>
    internal static string RequirementTargetName(GameWorldState world, Guid targetId)
    {
        if (targetId == Guid.Empty) return "something this build authors no reference for";
        var identity = EntityIdentityFormatter.Describe(targetId, world.EntityIdentities);
        return identity.HasName
            ? identity.Name
            : EntityIdentityFormatter.PlayerHandle(targetId, world.EntityIdentities);
    }

    /// <summary>
    /// Whether this row's threshold moves with the level being checked, which is the only condition
    /// under which the level is worth publishing.
    /// </summary>
    /// <remarks>
    /// The threshold is <c>baseValue</c> folded through two authored <c>ValueModifier</c>s. Both
    /// carrying nought is the identity fold, so the row asks the same number at level one and at
    /// level forty and <c>forLevel</c> would be a constant nobody can act on.
    /// </remarks>
    internal static bool ThresholdScales(in WorldEntityRequirement row) =>
        row.PerLevel.Amount != BigDouble.Zero || row.ModPerLevel.Amount != BigDouble.Zero;

    /// <summary>
    /// The one sentence for a condition class this build cannot read, pointing at the screen that
    /// draws the entity holding it wherever the world publishes one.
    /// </summary>
    internal static string UnreadRequirementSentence(
        GameWorldState world,
        in WorldEntityRequirement row) =>
        "This requirement is one the suite cannot read yet; open " +
        (GameMcpScreenState.TryPublishedScreen(world, row.OwnerId, out var screen)
            ? screen
            : "this entity's own screen") +
        " to see it.";

    internal readonly struct RequirementKey : IEquatable<RequirementKey>
    {
        internal RequirementKey(Guid ownerId, int tierIndex,
            WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel)
        {
            OwnerId = ownerId;
            TierIndex = tierIndex;
            Program = program;
        }

        private Guid OwnerId { get; }
        private int TierIndex { get; }
        private WorldRequirementProgramKind Program { get; }
        public bool Equals(RequirementKey other) =>
            OwnerId == other.OwnerId && TierIndex == other.TierIndex && Program == other.Program;
        public override bool Equals(object? obj) => obj is RequirementKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OwnerId, TierIndex, Program);
    }
}
#endif
