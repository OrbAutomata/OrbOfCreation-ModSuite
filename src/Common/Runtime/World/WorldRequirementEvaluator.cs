using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>What the published requirement rows say about one entity's next purchase.</summary>
internal enum WorldRequirementVerdict
{
    /// <summary>Every condition holds, or the entity authored none.</summary>
    Met = 0,

    /// <summary>A condition was evaluated and does not hold.</summary>
    Unmet = 1,

    /// <summary>
    /// A condition could not be evaluated: an unmodelled class, an unmodelled comparison, a target
    /// absent from the snapshot, or a threshold whose scaling has no closed form. Never treat this as
    /// met.
    /// </summary>
    Unevaluable = 2,
}

/// <summary>
/// The exact value selection and thresholds behind one requirement-leaf verdict.
/// </summary>
internal readonly struct WorldRequirementLeafEvaluation
{
    internal WorldRequirementLeafEvaluation(
        WorldRequirementVerdict verdict,
        string reasonCode,
        string selectedValueKind,
        BigDouble current,
        BigDouble required,
        BigDouble baseThreshold,
        BigDouble scaledThreshold,
        BigDouble effectiveThreshold)
    {
        Verdict = verdict;
        ReasonCode = reasonCode ?? string.Empty;
        SelectedValueKind = selectedValueKind ?? string.Empty;
        Current = current;
        Required = required;
        BaseThreshold = baseThreshold;
        ScaledThreshold = scaledThreshold;
        EffectiveThreshold = effectiveThreshold;
    }

    internal WorldRequirementVerdict Verdict { get; }
    internal string ReasonCode { get; }
    internal string SelectedValueKind { get; }
    internal BigDouble Current { get; }
    internal BigDouble Required { get; }
    internal BigDouble BaseThreshold { get; }
    internal BigDouble ScaledThreshold { get; }
    internal BigDouble EffectiveThreshold { get; }
    internal bool Met => Verdict == WorldRequirementVerdict.Met;
}

/// <summary>
/// Answers <c>prerequisitesPerLevel.Check(level)</c> from the published snapshot, on the worker.
/// </summary>
/// <remarks>
/// <para>
/// The game reaches this answer by walking a polymorphic condition list and asking each entry to look
/// at another entity. Every one of those entities is already a row in the same snapshot, so the same
/// answer is reachable as arithmetic over published facts — which is what lets a plan be made without
/// the Unity thread, and what stops the planner proposing a purchase the game will refuse.
/// </para>
/// <para>
/// <b>It fails closed, comparison by comparison.</b> A <c>Visible</c> or <c>Available</c> comparison
/// is modelled only where the target's own gate is a stored field the snapshot already carries —
/// <c>ConsumableSO</c> answers both from <c>visible</c>. Where it is the whole-entity gate that
/// reaches the <c>Check()</c> which writes, it is not modelled and never will be from here. Several
/// other comparisons are simply not exercised by any authored content in this baseline. Both read as
/// <see cref="WorldRequirementVerdict.Unevaluable"/>, and a consumer that treats that as anything but
/// "do not plan this" has broken the contract this type exists to keep.
/// </para>
/// <para>
/// Every modelled comparison is one <c>&gt;=</c> against a published number, transcribed from the
/// condition class's own <c>InternalIsValid</c>. The only real arithmetic is the threshold, which
/// scales with the level being bought; see <see cref="GameLeveledValue"/>.
/// </para>
/// </remarks>
internal static class WorldRequirementEvaluator
{
    // The game's own enum members, as the integers the rows carry. Named here rather than mirrored as
    // types, because a mirrored enum would keep compiling while a build renumbered it — and this is
    // the one place where naming a member is what the code is actually about. Each is declared in
    // the contract manifest under `requirement-type.*` as a mirrored member, and
    // RequirementEnumContractTests pins the number, which no manifest row can.
    private const int UpgradeOneLevel = 0;
    private const int UpgradeMaxLevel = 1;
    private const int UpgradeAtLeast = 2;
    private const int StructureQuantity = 0;
    private const int SpellDiscovered = 0;
    private const int SpellLevel = 2;
    private const int SpellMasteryLevel = 3;
    private const int AlchemyDiscovered = 0;
    private const int AlchemyRecipeLevel = 2;
    private const int AlchemyMasteryLevel = 3;
    private const int AlchemyAdvancementLevel = 4;
    private const int RitualDiscovered = 0;
    private const int RitualReachedLevel = 1;
    private const int NumberValue = 0;
    private const int GenericLevel = 1;
    private const int PrerequisiteLinkBase = 0;
    private const int PrerequisiteLinkTier = 1;
    private const int ListAnyVisible = 1;
    private const int ListAnyAvailable = 2;
    private const int MaximumExpansionDepth = 32;

    /// <summary>
    /// The level an upgrade's per-level container is checked at, matching the game's
    /// <c>HasMetQueuedLevelRequirements()</c>: the level a purchase made now would land on.
    /// </summary>
    internal static long UpgradeCheckLevel(in WorldUpgrade upgrade) => upgrade.CommittedLevel + 1L;

    /// <summary>
    /// The level a structure's per-level container is checked at, matching the game's
    /// <c>HasMetLevelRequirements()</c>.
    /// </summary>
    /// <remarks>
    /// The game passes the owned count here, not one more than it — a structure's own check is
    /// off-by-one against an upgrade's, and reproducing it as one more would read as the obvious
    /// symmetry and be wrong. <c>HasMetLevelRequirements()</c> passes <c>quantity</c> and
    /// <c>GetPurchaseLevel()</c> is a two-hop return of that same field, so the published level is
    /// that number and the world captures it once.
    /// </remarks>
    internal static long StructureCheckLevel(in WorldStructure structure) =>
        structure.Reading.Level.ToLong();

    /// <summary>
    /// Whether every condition on <paramref name="ownerId"/>'s next purchase holds at
    /// <paramref name="level"/>.
    /// </summary>
    /// <remarks>
    /// An owner with no rows is met, which is the game's own answer for an empty container rather than
    /// a stand-in for one that could not be read: the reader publishes a row for every condition it
    /// finds, including ones it cannot model.
    /// <para>
    /// An unevaluable condition outranks an unmet one in the verdict even though both refuse the
    /// purchase, because they call for different things: unmet resolves itself as the save progresses,
    /// while unevaluable is this suite's gap and should be named as such wherever it surfaces.
    /// </para>
    /// </remarks>
    internal static WorldRequirementVerdict Evaluate(
        GameWorldState world,
        Guid ownerId,
        long level,
        WorldRequirementProgramKind program = WorldRequirementProgramKind.NextLevel)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, ownerId, out var start, out var count))
        {
            return WorldRequirementVerdict.Met;
        }

        var rows = world.EntityRequirements.AsSpan();
        var verdict = WorldRequirementVerdict.Met;
        var priorGroup = -1;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.Program != program || row.GroupOrdinal == priorGroup) continue;
            priorGroup = row.GroupOrdinal;
            var group = EvaluateGroup(world, rows, start, count, in row, level, program);
            if (group == WorldRequirementVerdict.Unevaluable)
                return WorldRequirementVerdict.Unevaluable;
            if (group == WorldRequirementVerdict.Unmet)
                verdict = WorldRequirementVerdict.Unmet;
        }

        return verdict;
    }

    private static WorldRequirementVerdict EvaluateGroup(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        in WorldEntityRequirement first,
        long level,
        WorldRequirementProgramKind program)
    {
        var verdict = first.GroupKind == WorldRequirementGroupKind.Any
            ? WorldRequirementVerdict.Unmet
            : WorldRequirementVerdict.Met;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.Program != program || row.GroupOrdinal != first.GroupOrdinal) continue;
            if (row.GroupKind != first.GroupKind) return WorldRequirementVerdict.Unevaluable;

            var child = Evaluate(world, in row, level);
            if (first.GroupKind == WorldRequirementGroupKind.Any)
            {
                if (child == WorldRequirementVerdict.Met) return WorldRequirementVerdict.Met;
                if (child == WorldRequirementVerdict.Unevaluable)
                    verdict = WorldRequirementVerdict.Unevaluable;
                continue;
            }

            // Preserve the evaluator's fail-closed AND contract: an unmodelled child stays named
            // even when another child is ordinarily unmet.
            if (child == WorldRequirementVerdict.Unevaluable)
                return WorldRequirementVerdict.Unevaluable;
            if (child == WorldRequirementVerdict.Unmet)
                verdict = WorldRequirementVerdict.Unmet;
        }

        return verdict;
    }

    /// <summary>
    /// Whether one numbered container on <paramref name="ownerId"/> holds at
    /// <paramref name="level"/>.
    /// </summary>
    /// <remarks>
    /// An owner holding several containers — a prerequisite link holds one per tier — is not one
    /// requirement set, and the whole-owner overload would fold its tiers into an answer the game
    /// never asks for. Containers are published as a parent-and-child graph rather than as the flat
    /// groups an entity's own per-level container gets, so this is a different walk and not an
    /// alternative spelling of the same one.
    /// </remarks>
    internal static WorldRequirementVerdict EvaluateContainer(
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long level)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        Span<RequirementContainerKey> trail = stackalloc RequirementContainerKey[MaximumExpansionDepth];
        return EvaluateContainer(world, ownerId, containerIndex, level, trail, trailDepth: 0);
    }

    /// <summary>One condition, at the level being bought.</summary>
    internal static WorldRequirementVerdict Evaluate(
        GameWorldState world,
        in WorldEntityRequirement row,
        long level)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (row.NodeKind != WorldRequirementNodeKind.Leaf)
            return WorldRequirementVerdict.Unevaluable;
        Span<RequirementContainerKey> trail = stackalloc RequirementContainerKey[MaximumExpansionDepth];
        return EvaluateLeaf(world, in row, level, trail, trailDepth: 0);
    }

    /// <summary>
    /// Explains which published value the native leaf evaluator selects. The verdict still comes
    /// from <see cref="Evaluate(GameWorldState,in WorldEntityRequirement,long)"/>; this method does
    /// not maintain a second condition evaluator.
    /// </summary>
    internal static WorldRequirementLeafEvaluation ExplainLeaf(
        GameWorldState world,
        in WorldEntityRequirement row,
        long level)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var verdict = Evaluate(world, in row, level);
        var baseThreshold = new BigDouble(row.BaseValue);
        if (!TryThreshold(in row, level, out var scaledThreshold))
        {
            return new WorldRequirementLeafEvaluation(
                WorldRequirementVerdict.Unevaluable,
                "threshold_scaling_unavailable",
                "unsupported",
                default,
                default,
                baseThreshold,
                default,
                default);
        }

        var whole = BigDouble.Round(scaledThreshold).ToLong();
        var required = new BigDouble(whole);
        var effective = required;
        BigDouble current;
        string selected;
        var supported = true;

        switch (row.Kind)
        {
            case WorldRequirementConditionKind.Upgrade
                when WorldLookup.TryFind(world.Upgrades, row.TargetId, out var upgrade):
                selected = "purchased_level";
                current = new BigDouble(upgrade.Reading.Level);
                if (row.ReqType == UpgradeOneLevel) effective = required = BigDouble.One;
                else if (row.ReqType == UpgradeMaxLevel)
                    effective = required = new BigDouble(upgrade.Reading.MaxLevel);
                else supported = row.ReqType == UpgradeAtLeast;
                break;
            case WorldRequirementConditionKind.Research
                when WorldLookup.TryFind(world.Research, row.TargetId, out var research):
                selected = "total_level";
                current = new BigDouble(ResearchLevel(in research));
                if (row.ReqType == UpgradeOneLevel) effective = required = BigDouble.One;
                else if (row.ReqType == UpgradeMaxLevel)
                    effective = required = new BigDouble(research.MaxLevel);
                else supported = row.ReqType == UpgradeAtLeast;
                break;
            case WorldRequirementConditionKind.Structure
                when WorldLookup.TryFind(world.Structures, row.TargetId, out var structure):
                selected = "purchased_quantity";
                current = structure.Reading.Level;
                supported = row.ReqType == StructureQuantity;
                break;
            case WorldRequirementConditionKind.Spell
                when WorldLookup.TryFind(world.SpellRecipes, row.TargetId, out var spell):
                if (row.ReqType == SpellDiscovered)
                {
                    selected = "discovered";
                    current = spell.Discovered ? BigDouble.One : BigDouble.Zero;
                    effective = required = BigDouble.One;
                }
                else
                {
                    selected = "mastery_level";
                    current = new BigDouble(spell.MasteryLevel);
                    supported = row.ReqType is SpellLevel or SpellMasteryLevel;
                }
                break;
            case WorldRequirementConditionKind.AlchemyRecipe
                when WorldLookup.TryFind(world.AlchemyRecipes, row.TargetId, out var alchemy):
                if (row.ReqType == AlchemyDiscovered)
                {
                    selected = "discovered";
                    current = alchemy.Discovered ? BigDouble.One : BigDouble.Zero;
                    effective = required = BigDouble.One;
                }
                else if (row.ReqType == AlchemyRecipeLevel)
                {
                    selected = "recipe_level";
                    current = new BigDouble(alchemy.MaxLevel);
                }
                else if (row.ReqType == AlchemyMasteryLevel)
                {
                    selected = "mastery_level";
                    current = new BigDouble(alchemy.MasteryLevel);
                }
                else
                {
                    selected = "advancement_level";
                    current = new BigDouble(alchemy.AdvancementLevel);
                    supported = row.ReqType == AlchemyAdvancementLevel;
                }
                break;
            case WorldRequirementConditionKind.Ritual
                when WorldLookup.TryFind(world.Rituals, row.TargetId, out var ritual):
                if (row.ReqType == RitualDiscovered)
                {
                    selected = "discovered";
                    current = ritual.Discovered ? BigDouble.One : BigDouble.Zero;
                    effective = required = BigDouble.One;
                }
                else
                {
                    selected = "reached_level";
                    current = new BigDouble(ritual.ReachedLevel);
                    supported = row.ReqType == RitualReachedLevel;
                }
                break;
            case WorldRequirementConditionKind.Number
                when TryFindNumber(world, row.TargetId, out var number):
                selected = "numeric_value";
                current = number.Value;
                required = effective = scaledThreshold;
                supported = row.ReqType == NumberValue;
                break;
            case WorldRequirementConditionKind.Generic
                when TryFindNumber(world, row.TargetId, out var generic):
                selected = "numeric_value";
                current = new BigDouble(generic.Value.ToInt());
                supported = row.ReqType == GenericLevel;
                break;
            case WorldRequirementConditionKind.PrerequisiteLink:
                selected = "prerequisite_link_gate";
                current = verdict == WorldRequirementVerdict.Met
                    ? BigDouble.One
                    : BigDouble.Zero;
                required = effective = BigDouble.One;
                supported = row.ReqType is PrerequisiteLinkBase or PrerequisiteLinkTier;
                break;
            case WorldRequirementConditionKind.List:
                selected = row.ReqType == ListAnyVisible ? "list_any_visible" : "list_any_available";
                current = verdict == WorldRequirementVerdict.Met ? BigDouble.One : BigDouble.Zero;
                required = effective = BigDouble.One;
                supported = row.ReqType is ListAnyVisible or ListAnyAvailable;
                break;
            default:
                selected = "unsupported";
                current = default;
                supported = false;
                break;
        }

        if (!supported)
        {
            return new WorldRequirementLeafEvaluation(
                WorldRequirementVerdict.Unevaluable,
                "unsupported_requirement_value",
                selected,
                current,
                required,
                baseThreshold,
                scaledThreshold,
                effective);
        }

        return new WorldRequirementLeafEvaluation(
            verdict,
            verdict switch
            {
                WorldRequirementVerdict.Met => "requirement_met",
                WorldRequirementVerdict.Unmet => "requirement_unmet",
                _ => "requirement_unevaluable",
            },
            selected,
            current,
            required,
            baseThreshold,
            scaledThreshold,
            effective);
    }

    private static WorldRequirementVerdict EvaluateContainer(
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long level,
        Span<RequirementContainerKey> trail,
        int trailDepth)
    {
        if (trailDepth >= trail.Length) return WorldRequirementVerdict.Unevaluable;
        var key = new RequirementContainerKey(ownerId, containerIndex);
        for (var index = 0; index < trailDepth; index++)
            if (trail[index].Equals(key)) return WorldRequirementVerdict.Unevaluable;
        trail[trailDepth] = key;

        if (!WorldEntityRequirementLookup.TryFindContainerRange(
                world.EntityRequirements, ownerId, containerIndex, out var start, out var count))
        {
            return WorldRequirementVerdict.Met;
        }

        var rows = world.EntityRequirements.AsSpan();
        var verdict = WorldRequirementVerdict.Met;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.ParentOrdinal >= 0) continue;
            var one = EvaluateNode(
                world, rows, start, count, in row, level, trail, trailDepth + 1);
            if (one == WorldRequirementVerdict.Unevaluable) return one;
            if (one == WorldRequirementVerdict.Unmet) verdict = one;
        }

        return verdict;
    }

    private static WorldRequirementVerdict EvaluateNode(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        in WorldEntityRequirement row,
        long level,
        Span<RequirementContainerKey> trail,
        int trailDepth)
    {
        if (row.NodeKind == WorldRequirementNodeKind.Leaf)
            return EvaluateLeaf(world, in row, level, trail, trailDepth);

        var hasUnevaluable = false;
        var hasUnmet = false;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var child = ref rows[start + offset];
            if (child.ParentOrdinal != row.Ordinal) continue;
            var one = EvaluateNode(
                world, rows, start, count, in child, level, trail, trailDepth);
            if (row.Operator == WorldRequirementOperator.Or && one == WorldRequirementVerdict.Met)
                return WorldRequirementVerdict.Met;
            if (one == WorldRequirementVerdict.Unevaluable) hasUnevaluable = true;
            if (one == WorldRequirementVerdict.Unmet) hasUnmet = true;
        }

        if (row.Operator == WorldRequirementOperator.And)
        {
            if (hasUnevaluable) return WorldRequirementVerdict.Unevaluable;
            return hasUnmet ? WorldRequirementVerdict.Unmet : WorldRequirementVerdict.Met;
        }
        if (row.Operator == WorldRequirementOperator.Or)
        {
            if (hasUnevaluable) return WorldRequirementVerdict.Unevaluable;
            return WorldRequirementVerdict.Unmet;
        }
        return WorldRequirementVerdict.Unevaluable;
    }

    private static WorldRequirementVerdict EvaluateLeaf(
        GameWorldState world,
        in WorldEntityRequirement row,
        long level,
        Span<RequirementContainerKey> trail,
        int trailDepth)
    {
        if (row.Kind == WorldRequirementConditionKind.Unknown) return WorldRequirementVerdict.Unevaluable;
        if (row.Kind == WorldRequirementConditionKind.Literal)
            return row.ReqType == 1
                ? WorldRequirementVerdict.Met
                : WorldRequirementVerdict.Unmet;
        if (!TryThreshold(in row, level, out var threshold)) return WorldRequirementVerdict.Unevaluable;

        // The game rounds the threshold to a whole number for every comparison but the numeric one,
        // through ConditionValueInstance.GetLong(); the numeric one compares the magnitude itself.
        var whole = BigDouble.Round(threshold).ToLong();

        return row.Kind switch
        {
            WorldRequirementConditionKind.Upgrade => Upgrade(world, in row, whole),
            WorldRequirementConditionKind.Research => Research(world, in row, whole),
            WorldRequirementConditionKind.Structure => Structure(world, in row, whole),
            WorldRequirementConditionKind.Spell => Spell(world, in row, whole),
            WorldRequirementConditionKind.AlchemyRecipe => Alchemy(world, in row, whole),
            WorldRequirementConditionKind.Ritual => Ritual(world, in row, whole),
            WorldRequirementConditionKind.Number => Number(world, in row, threshold),
            WorldRequirementConditionKind.Generic => Generic(world, in row, whole),
            WorldRequirementConditionKind.PrerequisiteLink => PrerequisiteLink(
                world, in row, whole, trail, trailDepth),
            WorldRequirementConditionKind.List => List(world, in row),
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>Ported from <c>ListRequirement.InternalIsValid</c>.</summary>
    /// <remarks>
    /// <para>
    /// The two folds are <c>Enumerable.Any(element is IVisibility v &amp;&amp; v.IsVisible())</c> and
    /// its availability twin — an element implementing neither interface contributes false rather
    /// than being skipped, which is why a null member is folded rather than ignored. The class's
    /// third comparison, a count against the threshold, is not exercised by any authored content in
    /// this baseline and reads as unevaluable rather than as a comparison nobody has checked.
    /// </para>
    /// <para>
    /// Only a member whose own gate this suite has read is folded. <c>ConsumableSO</c> answers both
    /// <c>IsVisible()</c> and <c>IsAvailable()</c> from the same stored <c>visible</c> field, which
    /// the consumable rows publish; anything else refuses, because a fold that quietly treated an
    /// unread member as not visible would answer <em>unmet</em> for a list the game calls satisfied.
    /// A member found visible still answers met even when a later one is unreadable, because the
    /// game's own <c>Any</c> stops at the first true.
    /// </para>
    /// </remarks>
    private static WorldRequirementVerdict List(GameWorldState world, in WorldEntityRequirement row)
    {
        if (row.ReqType is not (ListAnyVisible or ListAnyAvailable))
            return WorldRequirementVerdict.Unevaluable;
        if (!WorldRequirementListLookup.TryFindRange(
                world.RequirementListMembers, row.TargetId, out var start, out var count))
        {
            return WorldRequirementVerdict.Unevaluable;
        }

        var rows = world.RequirementListMembers.AsSpan();
        var verdict = WorldRequirementVerdict.Unmet;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var member = ref rows[start + offset];
            if (member.Position < 0 || member.MemberId == Guid.Empty) continue;
            if (!WorldLookup.TryFind(world.Consumables, member.MemberId, out var consumable))
            {
                verdict = WorldRequirementVerdict.Unevaluable;
                continue;
            }

            if (consumable.Visible) return WorldRequirementVerdict.Met;
        }

        return verdict;
    }

    /// <summary>
    /// A tier's own gate, at the level the game asks it at.
    /// </summary>
    /// <remarks>
    /// The tier is evaluated at level zero, not at the level of whatever entity is consulting it.
    /// <c>LinkDefinition.CheckPassivesEnabled()</c> reaches the tier through the no-argument
    /// <c>Container.Check()</c>, which walks its conditions at <c>ConditionInfo.Adjust(adjustValue,
    /// 0L)</c> — a link tier has no level of its own to scale by, and forwarding the asking entity's
    /// would scale the tier's thresholds by a number the game never applies there.
    /// </remarks>
    private static WorldRequirementVerdict PrerequisiteLink(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold,
        Span<RequirementContainerKey> trail,
        int trailDepth)
    {
        var tier = row.ReqType switch
        {
            PrerequisiteLinkBase => 0,
            PrerequisiteLinkTier => threshold,
            _ => -1,
        };
        if (tier < 0 || tier > int.MaxValue) return WorldRequirementVerdict.Unevaluable;
        if (!WorldPrerequisiteLinkTierLookup.TryFind(
                world.PrerequisiteLinkTiers, row.TargetId, (int)tier, out var nativeTier))
        {
            return WorldRequirementVerdict.Unevaluable;
        }
        if (!nativeTier.ActiveEnabled) return WorldRequirementVerdict.Unmet;
        if (nativeTier.PassiveEnabled) return WorldRequirementVerdict.Met;
        if (nativeTier.EvaluatedThisFrame) return WorldRequirementVerdict.Unmet;
        if (!WorldEntityRequirementLookup.TryFindContainerRange(
                world.EntityRequirements, row.TargetId, (int)tier, out _, out _))
        {
            return WorldRequirementVerdict.Unevaluable;
        }
        return EvaluateContainer(world, row.TargetId, (int)tier, level: 0L, trail, trailDepth);
    }

    private readonly struct RequirementContainerKey : IEquatable<RequirementContainerKey>
    {
        internal RequirementContainerKey(Guid ownerId, int containerIndex)
        {
            OwnerId = ownerId;
            ContainerIndex = containerIndex;
        }

        private Guid OwnerId { get; }
        private int ContainerIndex { get; }

        public bool Equals(RequirementContainerKey other) =>
            OwnerId == other.OwnerId && ContainerIndex == other.ContainerIndex;
    }

    /// <summary>
    /// The condition's threshold at this level, reproducing
    /// <c>new ConditionValueInstance(value, conditionInfo)</c>.
    /// </summary>
    /// <remarks>
    /// The instance adds <c>conditionInfo.adjustValue</c>, which is nought for every per-level check:
    /// the level reaches <c>Check</c> through the implicit <c>int → ConditionInfo</c> conversion, which
    /// leaves the adjustment at nought and the condition type at <c>HardRequirement</c>.
    /// </remarks>
    private static bool TryThreshold(in WorldEntityRequirement row, long level, out BigDouble threshold)
    {
        if (TryModifier(row.PerLevel, out var perLevel) &&
            TryModifier(row.ModPerLevel, out var modPerLevel))
        {
            return GameLeveledValue.TryAtLevel(row.BaseValue, perLevel, modPerLevel, level, out threshold);
        }

        threshold = default;
        return false;
    }

    /// <summary>The published scaling as the arithmetic type, if this build's enum has that member.</summary>
    /// <remarks>
    /// The row carries the modifier type as the integer the build stored, so a future build that adds a
    /// member arrives here as a number with no meaning. That refuses the condition rather than
    /// defaulting to <c>Raw</c>, which would compute a plausible threshold from a modifier this suite
    /// does not know how to apply.
    /// </remarks>
    private static bool TryModifier(in WorldRequirementScaling scaling, out GameValueModifier modifier)
    {
        if (!Enum.IsDefined(typeof(GameValueModifierType), scaling.ModifierType))
        {
            modifier = default;
            return false;
        }

        modifier = new GameValueModifier(
            (GameValueModifierType)scaling.ModifierType, scaling.Amount, scaling.Order);
        return true;
    }

    /// <summary>Ported from <c>UpgradeRequirement.InternalIsValid</c>.</summary>
    private static WorldRequirementVerdict Upgrade(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.Upgrades, row.TargetId, out var upgrade))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType switch
        {
            UpgradeOneLevel => Verdict(upgrade.Reading.Level > 0),
            UpgradeMaxLevel => Verdict(upgrade.Reading.Level >= upgrade.Reading.MaxLevel),
            UpgradeAtLeast => Verdict(upgrade.Reading.Level >= threshold),

            // Visible: item.IsVisible(), which walks the whole-entity gate and writes.
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>
    /// Ported from <c>ResearchRequirement.InternalIsValid</c>, whose level is not the level field.
    /// </summary>
    private static WorldRequirementVerdict Research(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.Research, row.TargetId, out var research))
            return WorldRequirementVerdict.Unevaluable;

        var level = ResearchLevel(in research);
        return row.ReqType switch
        {
            UpgradeOneLevel => Verdict(level > 0),
            UpgradeMaxLevel => Verdict(level >= research.MaxLevel),
            UpgradeAtLeast => Verdict(level >= threshold),
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>
    /// The collector's direct result from <c>ResearchSO.GetLevel()</c>:
    /// <c>GetBaseLevel() + GetBonusLevels()</c>.
    /// </summary>
    /// <remarks>
    /// Three terms, not one. The <c>level</c> field alone is what a research entry has bought for
    /// itself; the other two are levels granted from elsewhere, and every requirement in the game
    /// compares against the sum. Reading the field would under-report every entry with a bonus and
    /// make the planner skip purchases the game would have allowed.
    /// </remarks>
    internal static long ResearchLevel(in WorldResearch research) => research.TotalLevel;

    /// <summary>Ported from <c>StructureRequirement.InternalIsValid</c>.</summary>
    private static WorldRequirementVerdict Structure(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.Structures, row.TargetId, out var structure))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType switch
        {
            StructureQuantity => Verdict(structure.Reading.Level.ToLong() >= threshold),

            // Available: item.IsAvailable(), which walks the whole-entity gate and writes.
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>Ported from <c>SpellRequirement.InternalIsValid</c>.</summary>
    /// <remarks>
    /// The two level comparisons read the same field, which is the original's own doing: its
    /// <c>SpellLevel</c> and <c>MasteryLevel</c> cases share a switch arm.
    /// </remarks>
    private static WorldRequirementVerdict Spell(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.SpellRecipes, row.TargetId, out var recipe))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType switch
        {
            SpellDiscovered => Verdict(recipe.Discovered),
            SpellLevel or SpellMasteryLevel => Verdict(recipe.MasteryLevel >= threshold),
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>Ported from <c>AlchemyRecipeRequirement.InternalIsValid</c>.</summary>
    private static WorldRequirementVerdict Alchemy(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.AlchemyRecipes, row.TargetId, out var recipe))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType switch
        {
            AlchemyDiscovered => Verdict(recipe.Discovered),
            AlchemyRecipeLevel => Verdict(recipe.MaxLevel >= threshold),
            AlchemyMasteryLevel => Verdict(recipe.MasteryLevel >= threshold),
            AlchemyAdvancementLevel => Verdict(recipe.AdvancementLevel >= threshold),

            // Visible: item.IsVisible(), which walks the whole-entity gate and writes.
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>Ported from <c>RitualRequirement.InternalIsValid</c>.</summary>
    private static WorldRequirementVerdict Ritual(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (!WorldLookup.TryFind(world.Rituals, row.TargetId, out var ritual))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType switch
        {
            RitualDiscovered => Verdict(ritual.Discovered),
            RitualReachedLevel => Verdict(ritual.ReachedLevel >= threshold),
            _ => WorldRequirementVerdict.Unevaluable,
        };
    }

    /// <summary>
    /// Ported from <c>NumberRequirement.InternalIsValid</c>: <c>item &gt;= instance.GetDouble()</c>,
    /// where the implicit conversion off the variable is its own calculated value.
    /// </summary>
    /// <remarks>
    /// The one comparison in the whole set that is not rounded to a whole number. It is also the one
    /// that must search two registries: the game's <c>NumberVariable</c> is the base of both the
    /// integer and the double registries, which the snapshot keeps apart because the game does.
    /// </remarks>
    private static WorldRequirementVerdict Number(
        GameWorldState world,
        in WorldEntityRequirement row,
        BigDouble threshold)
    {
        if (!TryFindNumber(world, row.TargetId, out var variable))
            return WorldRequirementVerdict.Unevaluable;

        return row.ReqType == NumberValue
            ? Verdict(variable.Value >= threshold)
            : WorldRequirementVerdict.Unevaluable;
    }

    /// <summary>
    /// Ported from <c>GenericRequirement.InternalIsValid</c>, for the one target shape this suite has
    /// been audited against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>item</c> is an <c>UpgradeableObject</c> and <c>GetLevel()</c> is virtual across a dozen
    /// subclasses, so "the level of the thing this points at" is a different expression per target
    /// type. The authored content points it at a number variable, whose override is
    /// <c>value.AsInt()</c>; every other target reads as unevaluable rather than as some other type's
    /// level, because a plausible answer from the wrong override is exactly the failure this whole
    /// path exists to avoid.
    /// </para>
    /// <para>
    /// <c>Discovered</c> is refused on that same ground, and not because it writes — every
    /// <c>IDiscoverable.IsDiscovered()</c> is a field return. They are not returns of the <em>same</em>
    /// field: <c>EquipmentSO</c> answers from <c>isCreated</c> where the other five answer from
    /// <c>discovered</c>, and a target implementing neither answers <c>true</c> outright. A row carries
    /// an identity rather than a type, so nothing here can pick the right one. The typed conditions —
    /// spell, alchemy recipe, ritual — name their target's type and so read theirs directly.
    /// </para>
    /// </remarks>
    private static WorldRequirementVerdict Generic(
        GameWorldState world,
        in WorldEntityRequirement row,
        long threshold)
    {
        if (row.ReqType != GenericLevel) return WorldRequirementVerdict.Unevaluable;
        if (!TryFindNumber(world, row.TargetId, out var variable))
            return WorldRequirementVerdict.Unevaluable;

        return Verdict(variable.Value.ToInt() >= threshold);
    }

    private static bool TryFindNumber(GameWorldState world, Guid targetId, out WorldNumberVariable variable) =>
        WorldLookup.TryFind(world.IntVariables, targetId, out variable) ||
        WorldLookup.TryFind(world.DoubleVariables, targetId, out variable);

    private static WorldRequirementVerdict Verdict(bool held) =>
        held ? WorldRequirementVerdict.Met : WorldRequirementVerdict.Unmet;
}
