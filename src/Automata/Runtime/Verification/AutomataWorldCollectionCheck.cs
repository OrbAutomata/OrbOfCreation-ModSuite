using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Checks world collection against the running game — the half of the collector's correctness that
/// no portable test can reach.
/// </summary>
/// <remarks>
/// <para>
/// The portable gate proves the collector's logic against stand-ins, and the contract manifest proves
/// every member it names exists in the shipped assembly's metadata with the expected shape. Neither
/// proves the part in between: that a compiled accessor binds and reads on a live object without
/// throwing, that a registry actually holds entities, that an identity is neither empty nor claimed
/// twice, that an edge points at something the snapshot contains — or what any of it costs.
/// </para>
/// <para>
/// It also answers the question the whole derived-value strategy rests on and that no amount of static
/// analysis can: <b>does the number the collector publishes equal the number the game itself would
/// read</b>. That is <c>GetValue()</c> — <c>calculationDirty ? Calculate() : calculatedValue</c> — and
/// not <c>Calculate()</c>. The distinction is not academic: a record with no modifiers is never
/// dirtied, so the game's <c>GetValue()</c> returns its <c>[NonSerialized]</c> zero for the whole
/// session while a recomputation returns the base value. A rung that compared against
/// <c>Adjust(baseValue)</c> alone answered "is our arithmetic right" — yes, 5595 of 5595 — while every
/// structure price was wrong, because the question that decides a purchase is "is our arithmetic the
/// number the game will charge from".
/// </para>
/// <para>
/// That survey walks entity registries, and a price is not made only of entity records. Two of its
/// terms belong to nobody it enumerates: the frame-wide <c>Player</c> globals, whose record lives
/// inside a <c>DoubleVariable</c> no registry holds, and the per-quantity modifier, which is a
/// <c>ValueModifierVariable</c> a structure points at rather than owns. Both multiply into every
/// structure price and neither was compared against anything, so "fold PASSED" was a statement about
/// a strict subset of what a price consumes. <see cref="CheckFrameGlobalFolds"/> and
/// <see cref="CheckCostPerQuantity"/> close that, and the fold verdict now means what it reads as.
/// </para>
/// <para>
/// <b>Nothing here takes a write path collection does not already take.</b> Almost every game
/// accessor called below is a field return or a <c>Count</c> comparison, verified against the
/// decompiled originals, and the one derived comparison (<c>GetTrueQuantity()</c>) reaches through
/// <c>quality.AsPercent()</c>, which would recalculate a dirty record, so it is made only when that
/// record is already clean. Two calls are not inert: <c>GetPurchaseCost()</c> recalculates the
/// records its chain touches, and <c>IsAvailable()</c> walks the prerequisite gate and latches.
/// <c>IsAvailable()</c> is a call collection itself makes on every structure and upgrade of every
/// cycle, so asking it again widens nothing; <c>GetPurchaseCost()</c> is the oracle this check
/// exists to consult, and settling a memo the game would have settled on its next render is the
/// price of having an oracle at all. The claim is "no write path the suite was not already taking",
/// not "no writes", and stating the stronger one would be false.
/// </para>
/// </remarks>
internal sealed class AutomataWorldCollectionCheck
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags Statics =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// The types whose cached modifier records the ported math depends on. Surveyed for staleness
    /// rather than every collected type, because these are the ones whose numbers are about to be
    /// recomputed off-thread from a cached read.
    /// </summary>
    /// <remarks>
    /// <c>DoubleVariable</c> is here for the frame globals. Their records belong to no entity type and
    /// were surveyed by nothing, which is how the term that multiplies into every structure price
    /// stayed an unverified input while the rung above it read PASSED.
    /// </remarks>
    private static readonly string[] StalenessSurveyTypes =
    {
        "ResourceSO", "StructureSO", "UpgradeSO", "RitualSO",
        "ConsumableSO", "AlchemyTypeSO", "SpellTypeSO", "DoubleVariable",
    };

    /// <summary>
    /// The player statics <c>WorldFrameGlobalsReader</c> reads, in the order it reads them. Named
    /// here rather than reached through that reader because the check has to prove the fold of each
    /// record separately: a reader that returned one wrong number out of five would otherwise report
    /// as a single unavailable term.
    /// </summary>
    private static readonly string[] FrameGlobalAccessors =
    {
        "GetResourceOverflow", "GetResourceOverflowLoss", "GetResetTimePassed", "GetStructureCost",
        "GetAttributeQualityBonus",
    };

    private readonly Func<string, Type?> _resolveType;
    private readonly List<VerificationFinding> _findings = new();
    private readonly List<string> _detail = new();
    private readonly List<string> _missingOracles = new();

    private int _agreements;
    private int _disagreements;
    private string _firstDisagreement = string.Empty;
    private int _invertedCounters;
    private int _invertedCapacityDivergences;

    internal AutomataWorldCollectionCheck()
        : this(WorldNativeTypes.Resolve)
    {
    }

    internal AutomataWorldCollectionCheck(Func<string, Type?> resolveType) =>
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));

    /// <summary>Runs every check and reports what each of them found.</summary>
    internal WorldCollectionCheckResult Run()
    {
        _findings.Clear();
        _detail.Clear();
        _missingOracles.Clear();
        _agreements = 0;
        _disagreements = 0;
        _firstDisagreement = string.Empty;
        _invertedCounters = 0;
        _invertedCapacityDivergences = 0;

        // Taken before anything else runs, and reported last. Collection folds modifier records and
        // calls IsAvailable(), both of which recalculate whatever they found dirty — measuring the
        // cache after that would be measuring this check's own footprint rather than the game's.
        var staleness = SurveyCacheStaleness();

        var collector = new GameWorldCollector(_resolveType, ReadFixedDeltaTime);

        // Two passes. The first grows every category's buffer to fit; the second is the steady-state
        // cost every cycle will actually pay, and is the number the plan owes — the only one of the
        // two a reader of this response can do anything with. The warm pass is also where the
        // structural categories drop out, since they are read once per lifecycle epoch and this is
        // the same collector reading the same frame — which is exactly what a cycle does.
        collector.Collect();

        var warm = Stopwatch.StartNew();
        var report = collector.Collect();
        warm.Stop();

        var world = collector.Build();

        ReportCategories(report);
        CheckIdentities(world);
        CheckAgainstGameAccessors(world);
        CheckGlobalSingletons(world);
        CheckFrameGlobalFolds();
        CheckReferenceEdges(world);
        CheckCostPerQuantity(world);
        CheckPublishedPurchaseCosts(world);
        ReportAccessorParity();
        Add(staleness.Fold);

        return new WorldCollectionCheckResult(
            _findings.ToArray(),
            report.TotalSampled,
            report.Categories.Length,
            warm.Elapsed.TotalMilliseconds,
            staleness.Drift);
    }

    private void Add(in VerificationFinding finding) => _findings.Add(finding);

    /// <summary>Takes the disagreement rows a check wrote as it walked, and clears the buffer.</summary>
    private string[] TakeDetail()
    {
        var detail = _detail.ToArray();
        _detail.Clear();
        return detail;
    }

    private static double ReadFixedDeltaTime() => UnityEngine.Time.fixedDeltaTime;

    /// <summary>
    /// Which categories bound, which lost entities, and which found nothing. A category that binds
    /// and reads zero entities is the failure a stub cannot show: every member resolves, so nothing
    /// degrades and no failure is reported — the registry is simply not where the collector looked.
    /// </summary>
    private void ReportCategories(WorldCollectionReport report)
    {
        var unavailable = new List<string>();
        var gaps = new List<string>();
        var quiet = new List<string>();
        var bound = 0;

        foreach (var category in report.Categories)
        {
            if (category.Outcome != WorldCategoryOutcome.Collected)
            {
                unavailable.Add($"{category.Category} did not bind — {category.FirstFailure}");
                continue;
            }

            bound++;
            if (category.Skipped > 0)
            {
                gaps.Add($"{category.Category} lost {category.Skipped} — {category.FirstFailure}");
            }

            if (category.Sampled != 0) continue;
            var expected = EmptySampleIsExpected(category.Category);
            if (expected.Length == 0) gaps.Add($"{category.Category} bound and found nothing");
            else quiet.Add($"{category.Category} {expected}");
        }

        // Per-category counts are a census rather than a verdict, and the totals ride on the
        // provenance line. What a reader can act on is the category that came up short, which is
        // exactly what the gap rows name.
        Add(unavailable.Count == 0
            ? VerificationFinding.Agree("Category binding", report.Categories.Length)
            : VerificationFinding.Incomplete(
                "Category binding",
                report.Categories.Length,
                $"{unavailable.Count} categories did not bind against this build",
                unavailable));

        var traversal = gaps.Count == 0
            ? VerificationFinding.Agree("Category traversal", bound)
            : VerificationFinding.Incomplete(
                "Category traversal",
                bound,
                $"{gaps.Count} bound categories came up short",
                gaps);
        Add(quiet.Count == 0 ? traversal : traversal.WithNote("Empty on purpose: " + string.Join("; ", quiet) + "."));
    }

    /// <summary>
    /// Why a category can read nothing without anything being wrong, in the reader's words, or the
    /// empty string when zero really is a shortfall.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most collectors count entities in the world, and zero of those is the one failure a bound
    /// collector can still have: every member resolved, nothing degraded, and the registry simply was
    /// not where it looked. Two of them count something else, and reporting them as shortfalls cost
    /// two rounds of investigation each to re-derive the same answer.
    /// </para>
    /// <para>
    /// This is deliberately a closed list rather than a rule read off the collector: a category that
    /// nobody has ruled on keeps the loud gap, so the way to be wrong here is to explain too little
    /// rather than to excuse too much.
    /// </para>
    /// </remarks>
    internal static string EmptySampleIsExpected(string category) => category switch
    {
        // Sampled counts open requests, not targets: the reader returns zero the moment
        // TargetingManager.IsTargeting() is false, which is nearly every frame there is.
        "targeting" => "samples only while a native targeting request is open, and none was",

        // Sampled counts station instances. Nothing on the pinned build ever creates one:
        // CraftingStructureSO.CreateInstance has one caller, AddInstance.Create, which has no
        // callers of its own and appears in no dumped asset.
        "crafting stations" => "counts stations in play, and this build authors none it can reach",
        _ => string.Empty,
    };

    private void CheckIdentities(GameWorldState world) => Add(WorldIdentityAudit.Audit(world));

    /// <summary>
    /// The parity check proper. Every comparison here reads a field on one side and calls the game's
    /// own accessor on the other, so a disagreement means the collector read the wrong thing rather
    /// than merely that it read something.
    /// </summary>
    /// <remarks>
    /// Comparisons where the collector already calls the accessor are deliberately absent —
    /// <c>StructureSO.GetPurchaseLevel()</c> and <c>ResourceSO.IsVisible()</c> among them. Checking a
    /// value against the method it was read from proves nothing and would pad the agreement count
    /// with results that cannot fail.
    /// </remarks>
    private void CheckAgainstGameAccessors(GameWorldState world)
    {
        // Every ritual member below is a field read; every oracle is the predicate the game answers
        // the same question with. This is where the D17 rework is proved or disproved: the counts
        // stand in for Count > 0 predicates, and the flags come from the runtime object rather than
        // the save record that hid them.
        CompareEach(world.Rituals, "RitualSO", (row, entity, type) =>
        {
            CompareBool("RitualSO.IsDiscovered", entity, type, "IsDiscovered", row.Discovered);
            CompareBool("RitualSO.HasActiveInstances", entity, type, "HasActiveInstances",
                row.ActiveInstances > 0);
            CompareBool("RitualSO.IsDurationRitual", entity, type, "IsDurationRitual",
                row.DurationRewardBlocks > 0);
            CompareInt("RitualSO.GetReachedLevel", entity, type, "GetReachedLevel", row.ReachedLevel);
        });

        // Stock is the member reading the save record hid. GetQuantity() is what the inventory shows.
        //
        // CanFire is the composite that left capture: the deriver composes it from cooldown, both
        // affordability verdicts and stock, and this is the oracle that proves the composition. The
        // native call writes while it answers, which is exactly why it belongs here rather than in
        // the pass. CompareCostScaling proves the other deleted reading — the cost readers scale
        // each entry as they walk it now, and these lists are where that arithmetic can be put
        // beside the game's own ResourceCostList.Multiply over live data.
        CompareEach(world.Consumables, "ConsumableSO", (row, entity, type) =>
        {
            CompareInt("ConsumableSO.GetQuantity", entity, type, "GetQuantity", row.Quantity);
            CompareInt("ConsumableSO.GetQueued", entity, type, "GetQueued", row.QueuedQuantity);
            CompareInt("ConsumableSO.GetGainedSince", entity, type, "GetGainedSince", row.GainedSince);
            CompareBool("ConsumableSO.IsVisible", entity, type, "IsVisible", row.Visible);
            CompareBool("ConsumableSO.CanFire", entity, type, "CanFire", row.CanFire);
            CompareCostScaling(entity, type, "consumeCost");
            CompareCostScaling(entity, type, "usageCost");
        });

        CompareEach(world.Resources, "ResourceSO", (row, entity, type) =>
        {
            var reading = row.Reading;

            // A rate term's active-modifier count stands in for HasActiveElements() throughout the
            // ported rate chain. If the substitution is wrong the chain branches the wrong way, so
            // each term is checked against the method it replaced.
            CompareHasActive(entity, type, "rate", reading.RateInputs.RateModifiers);
            CompareHasActive(entity, type, "rateSplash", reading.RateInputs.RateSplashModifiers);
            CompareHasActive(entity, type, "rateMaxPercent", reading.RateInputs.RateMaxPercentModifiers);
            CompareHasActive(
                entity, type, "rateInterestPercent", reading.RateInputs.RateInterestPercentModifiers);
            CompareHasActive(
                entity, type, "rateMissingPercent", reading.RateInputs.RateMissingPercentModifiers);
            CompareHasActive(
                entity, type, "rateLifetimePercent", reading.RateInputs.RateLifetimePercentModifiers);

            CompareTrueQuantity(entity, type, row.TrueQuantity);
            CompareDisplayedCapacity(entity, type, row);
        });
    }

    /// <summary>
    /// The published display coordinate against <c>GetDisplayQuantity()</c>, and the capacity verdict
    /// that reads it against the game's own ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The display mirror had no oracle at all: <c>WorldResourceCoordinate.DisplayAmount</c> decides
    /// which of two numbers a player-facing surface shows, and picking the wrong one is invisible to
    /// every other check here because both numbers are collected correctly.
    /// </para>
    /// <para>
    /// The capacity verdict is the deliberate disagreement. <c>IsAtMax()</c> compares the
    /// <em>stored</em> quantity with the ceiling and has no callers of its own; the published
    /// <c>isAtCapacity</c> compares the <em>displayed</em> quantity, so an inverted counter reads
    /// full when the thing it counts is maxed rather than when its tolerance is untouched. For every
    /// other resource the two questions coincide and are compared. For an inverted one the
    /// divergence is the design, so it is counted and reported rather than recorded as a failure —
    /// and the verdict is pinned instead against the game's own displayed number and ceiling, which
    /// is the comparison that would catch the branch being dropped.
    /// </para>
    /// <para>
    /// Both native calls reach <c>maxQuantity</c> through the record's conversion, which settles it
    /// when dirty. Compared only while it is already clean, on the same bargain
    /// <see cref="CompareTrueQuantity"/> strikes.
    /// </para>
    /// </remarks>
    private void CompareDisplayedCapacity(object entity, Type type, WorldResource row)
    {
        if (!IsClean(entity, type, "maxQuantity")) return;

        var display = NativeAccessorBinder.Call<BigDouble>(type, "GetDisplayQuantity");
        var atMax = NativeAccessorBinder.Call<bool>(type, "IsAtMax");
        if (display is null || atMax is null)
        {
            NoteMissingOracle(display is null ? "ResourceSO.GetDisplayQuantity" : "ResourceSO.IsAtMax");
            return;
        }

        var ourDisplay = WorldResourceCoordinate.DisplayAmount(in row);
        var theirDisplay = display(entity);
        Record("ResourceSO.GetDisplayQuantity", ourDisplay, theirDisplay, ourDisplay == theirDisplay);

        var theirAtMax = atMax(entity);
        if (!row.Reading.Traits.InvertedResource)
        {
            Record("ResourceSO.IsAtMax", row.IsAtCapacity, theirAtMax, row.IsAtCapacity == theirAtMax);
            return;
        }

        _invertedCounters++;
        if (row.IsAtCapacity != theirAtMax) _invertedCapacityDivergences++;

        var ceiling = ReadRecordValue(entity, type, "maxQuantity");
        if (ceiling is not { } theirCeiling)
        {
            NoteMissingOracle("ResourceSO.maxQuantity");
            return;
        }

        var displayedIsFull = row.IsCapped && theirDisplay.CompareTo(theirCeiling) >= 0;
        Record(
            "ResourceSO.GetDisplayQuantity at ceiling",
            row.IsAtCapacity,
            displayedIsFull,
            row.IsAtCapacity == displayedIsFull);
    }

    /// <summary>
    /// A modifier record's own value, read the way the game reads it. Only sound on a clean record,
    /// which every caller here has already established.
    /// </summary>
    private static BigDouble? ReadRecordValue(object entity, Type type, string fieldName)
    {
        var record = type.GetField(fieldName, Instance)?.GetValue(entity);
        var getValue = record?.GetType().GetMethod("GetValue", Instance, null, Type.EmptyTypes, null);
        if (getValue is null || getValue.ReturnType != typeof(BigDouble)) return null;

        return (BigDouble)getValue.Invoke(record, null)!;
    }

    /// <summary>
    /// That the row a consumer reaches for by identity is the object the game's own singleton hands
    /// out.
    /// </summary>
    /// <remarks>
    /// Auto Buy reads the multi-buy multiplier and the bulk development level out of
    /// <c>world.IntVariables</c> by uuid rather than through <c>AsInt()</c>, which recalculates a
    /// dirty record. Nothing offline can prove the asset carrying that uuid is the one
    /// <c>GlobalVariables</c> and <c>Player</c> actually return: the uuid comes from the extracted
    /// definitions, and the stub asserts an association it defines itself. Comparing identities
    /// against the running game is the only place that closes.
    /// </remarks>
    private void CheckGlobalSingletons(GameWorldState world)
    {
        var compared = 0;
        var differed = 0;

        CheckSingleton(world, "GlobalVariables", "GetMultiBuy", KnownEntities.MultiBuy.Uuid,
            ref compared, ref differed);
        CheckSingleton(world, "Player", "GetBulkDevelopment", KnownEntities.BulkDevelopment.Uuid,
            ref compared, ref differed);

        if (compared == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Global singletons", "neither singleton accessor could be reached."));
            return;
        }

        Add(differed == 0
            ? VerificationFinding.Agree("Global singletons", compared)
            : VerificationFinding.Disagree("Global singletons", compared, differed, TakeDetail()));
    }

    private void CheckSingleton(
        GameWorldState world,
        string typeName,
        string accessor,
        Guid expected,
        ref int compared,
        ref int differed)
    {
        var label = $"{typeName}.{accessor}";
        var type = ReflectionUtil.FindLoadedType(typeName);
        var method = type?.GetMethod(
            accessor, BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (method is null)
        {
            NoteMissingOracle(label);
            return;
        }

        object? variable;
        try
        {
            variable = method.Invoke(null, Array.Empty<object>());
        }
        catch (TargetInvocationException)
        {
            compared++;
            differed++;
            _detail.Add($"{label} threw; identity unproven.");
            return;
        }

        if (variable is null)
        {
            compared++;
            differed++;
            _detail.Add($"{label} returned nothing; identity unproven.");
            return;
        }

        compared++;
        var actual = Guid.TryParse(ReflectionUtil.ReadStableId(variable), out var id) ? id : Guid.Empty;
        var published = WorldLookup.TryFind(world.IntVariables, expected, out _);
        if (actual == expected && published) return;

        differed++;
        _detail.Add($"{label}: pinned {expected}, singleton {actual}, published {published}.");
    }

    /// <summary>
    /// The four frame-wide globals, folded the way collection folds them, against the pure
    /// recomputation of the same record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The staleness survey reaches records by walking entity registries and reading the
    /// <c>ValueModifierRecord</c> fields declared on the types it walks. No registry holds
    /// <c>Player</c>, and the record behind <c>GetStructureCost()</c> sits one field inside the
    /// <c>DoubleVariable</c> that accessor returns, so nothing in that survey ever touched it. The
    /// term it feeds multiplies into every structure price and only into structure prices, which is
    /// the exact shape of a spin that failed all 342 structure cost rows and none of the 402 upgrade
    /// rows.
    /// </para>
    /// <para>
    /// Resolved the way <c>WorldFrameGlobalsReader</c> resolves it — same type name, same accessor
    /// names, same nested <c>value</c> field, same <see cref="NativeModifierRecordAccess"/> — because
    /// a check that reached the record by some other route would prove a different thing than the one
    /// collection does. The side it is compared against is <see cref="GameReads"/>, the game's own
    /// <c>GetValue()</c> reconstructed without calling it, so nothing is written and no observer is
    /// re-stamped.
    /// </para>
    /// </remarks>
    private void CheckFrameGlobalFolds()
    {
        var compared = 0;
        var wrong = 0;
        var unread = 0;
        var first = string.Empty;

        foreach (var accessorName in FrameGlobalAccessors)
        {
            var label = $"Player.{accessorName}";
            var record = FrameGlobalRecord(accessorName, out var failure);
            if (record is null)
            {
                NoteMissingOracle(label);
                unread++;
                _detail.Add($"{label} {failure}; its reading is unproven.");
                continue;
            }

            var access = NativeModifierRecordAccess.For(record.GetType());
            if (access is null || !TryReadCache(record, out var cached, out var isDirty, out var truth))
            {
                NoteMissingOracle($"{label}.value");
                continue;
            }

            compared++;
            var theirs = GameReads(cached, isDirty, truth);
            var ours = access.Fold(record);
            if (ours == theirs) continue;

            wrong++;
            if (first.Length == 0)
            {
                first =
                    $"{label} {VerificationValue.Sides(ours, theirs)} " +
                    $"(recompute {VerificationValue.Format(truth)}, " +
                    $"memo {VerificationValue.Format(cached)}, {(isDirty ? "dirty" : "clean")})";
            }
        }

        if (compared == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Frame global reading", "no player global exposed a modifier record."));
            return;
        }

        if (wrong > 0)
        {
            _detail.Add($"first {first}");
            Add(VerificationFinding.Disagree(
                "Frame global reading", compared, wrong, TakeDetail()));
            return;
        }

        Add(unread == 0
            ? VerificationFinding.Agree("Frame global reading", compared)
            : VerificationFinding.Incomplete(
                "Frame global reading",
                compared,
                $"{unread} globals could not be read",
                TakeDetail()));
    }

    /// <summary>
    /// The <c>ValueModifierRecord</c> behind one player global, reached the way collection reaches it.
    /// </summary>
    private object? FrameGlobalRecord(string accessorName, out string failure)
    {
        failure = string.Empty;

        var player = _resolveType("Player");
        var accessor = player?.GetMethod(accessorName, Statics, null, Type.EmptyTypes, null);
        if (accessor is null)
        {
            failure = "did not resolve on this build";
            return null;
        }

        object? variable;
        try
        {
            variable = accessor.Invoke(null, Array.Empty<object>());
        }
        catch (TargetInvocationException)
        {
            failure = "threw";
            return null;
        }

        if (variable is null)
        {
            failure = "returned nothing";
            return null;
        }

        var record = variable.GetType().GetField("value", Instance)?.GetValue(variable);
        if (record is null) failure = "did not expose its value record";
        return record;
    }

    /// <summary>
    /// What the game's own <c>GetValue()</c> would return for a record, computed without calling it.
    /// </summary>
    /// <remarks>
    /// <c>GetValue()</c> is <c>calculationDirty ? Calculate() : calculatedValue</c>, and
    /// <c>Calculate()</c> stores <c>Adjust(baseValue)</c>. So a dirty record reads as its recomputation
    /// and a clean one reads as its memo — including the memo of a record that has never been
    /// calculated, which is zero and stays zero, because a record with no modifiers is never dirtied
    /// and so is never recomputed. That last case is not staleness. It is the permanent reading of the
    /// field, and it is what the game charges from.
    /// </remarks>
    private static BigDouble GameReads(BigDouble cached, bool isDirty, BigDouble recomputed) =>
        isDirty ? recomputed : cached;

    /// <summary>
    /// Every structure's per-quantity cost modifier: the row the deriver resolves by identity against
    /// the modifier the game hands out from the structure's own reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two failures live here and neither shows up anywhere else. The identity one:
    /// <c>WorldStructure.CostPerQuantityId</c> is read off the reference and looked up in the
    /// collected <c>ValueModifierVariable</c> registry, and nothing proves the row that comes back is
    /// the modifier the structure actually points at — the same gap
    /// <see cref="CheckGlobalSingletons"/> exists to close for the two pinned variables. The value
    /// one: the published row is three fields read directly off the variable's <c>value</c> struct,
    /// and the game reaches the same modifier through <c>ValueModifierRef.GetModifier()</c>. If those
    /// two ever stop agreeing, every structure price is wrong by the same factor and no upgrade price
    /// is, which is precisely the failure this rung was added for.
    /// </para>
    /// <para>
    /// A structure whose modifier does not resolve on our side is counted rather than compared. That
    /// is the deriver refusing to price it — correct behaviour, and invisible from outside except as
    /// an entity with no published cost at all.
    /// </para>
    /// <para>
    /// Nothing here needs <see cref="GameReads"/>. A <c>ValueModifierVariable</c> holds a
    /// <c>ValueModifier</c> struct inline rather than a <c>ValueModifierRecord</c>, so it carries no
    /// memo and no dirty flag, and <c>GetModifier()</c> is already the game's own read of the same
    /// three fields.
    /// </para>
    /// </remarks>
    private void CheckCostPerQuantity(GameWorldState world)
    {
        var type = _resolveType("StructureSO");
        var registry = NativeAccessorBinder.StaticList(type, "All");
        var getGuid = NativeAccessorBinder.Call<Guid>(type, "GetGuid");
        var reference = type?.GetField("costPerQuantity", Instance);
        var getModifier = reference?.FieldType.GetMethod("GetModifier", Instance, null, Type.EmptyTypes, null);
        if (registry is null || getGuid is null || reference is null || getModifier is null)
        {
            NoteMissingOracle("StructureSO.costPerQuantity");
            Add(VerificationFinding.Inconclusive(
                "Cost per quantity",
                "this build does not expose the structure's per-quantity modifier reference."));
            return;
        }

        var compared = 0;
        var exact = 0;
        var unresolved = 0;
        var first = string.Empty;

        foreach (var entity in registry)
        {
            if (entity is null) continue;

            var entityId = getGuid(entity);
            if (!WorldLookup.TryFind(world.Structures, entityId, out var structure) ||
                !WorldLookup.TryFind(
                    world.ModifierVariables, structure.Reading.CostPerQuantityId, out var published))
            {
                unresolved++;
                continue;
            }

            if (!TryReadGameModifier(entity, reference, getModifier,
                    out var theirType, out var theirAmount, out var theirOrder))
            {
                unresolved++;
                continue;
            }

            compared++;
            if (published.ModifierType == theirType &&
                published.Amount == theirAmount &&
                published.Order == theirOrder)
            {
                exact++;
                continue;
            }

            if (first.Length == 0)
            {
                first =
                    $"{Describe(entityId)}: " +
                    $"ours=[type {published.ModifierType} " +
                    $"amount {VerificationValue.Format(published.Amount)} order {published.Order}] " +
                    $"theirs=[type {theirType} " +
                    $"amount {VerificationValue.Format(theirAmount)} order {theirOrder}]";
            }
        }

        if (compared == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Cost per quantity", "nothing was comparable. Load a save first."));
            return;
        }

        if (compared != exact)
        {
            Add(VerificationFinding.Disagree(
                "Cost per quantity", compared, compared - exact, $"first {first}"));
            return;
        }

        Add(unresolved == 0
            ? VerificationFinding.Agree("Cost per quantity", compared)
            : VerificationFinding.Incomplete(
                "Cost per quantity",
                compared,
                $"{unresolved} structures did not resolve a modifier on either side"));
    }

    /// <summary>
    /// The modifier the game hands out for one structure's per-quantity reference.
    /// </summary>
    /// <remarks>
    /// A reference field that is null, or an accessor that throws on this build, is an unreadable
    /// oracle rather than a disagreement — the same distinction every other comparison here makes.
    /// </remarks>
    private static bool TryReadGameModifier(
        object entity,
        FieldInfo reference,
        MethodInfo getModifier,
        out int modifierType,
        out BigDouble amount,
        out int order)
    {
        modifierType = 0;
        amount = BigDouble.Zero;
        order = 0;

        var owner = reference.GetValue(entity);
        if (owner is null) return false;

        try
        {
            return TryDecodeModifier(
                getModifier.Invoke(owner, null), out modifierType, out amount, out order);
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    /// <summary>
    /// One of the game's <c>ValueModifier</c> structs as its three arithmetic fields.
    /// </summary>
    /// <remarks>
    /// <c>adjustReal</c> rather than the public <c>adjust</c> beside it, for the reason
    /// <see cref="NativeModifierRecordAccess"/> gives: <c>adjust</c> is written as
    /// <c>adjustReal.ToDouble()</c> and saturates above ~1e308, which this save's modifiers pass.
    /// </remarks>
    private static bool TryDecodeModifier(
        object? modifier,
        out int modifierType,
        out BigDouble amount,
        out int order)
    {
        modifierType = 0;
        amount = BigDouble.Zero;
        order = 0;
        if (modifier is null) return false;

        var type = modifier.GetType();
        var rawType = type.GetField("type", Instance)?.GetValue(modifier);
        var rawAmount = type.GetField("adjustReal", Instance)?.GetValue(modifier);
        var rawOrder = type.GetField("order", Instance)?.GetValue(modifier);
        if (rawType is null || rawAmount is not BigDouble read || rawOrder is null) return false;

        modifierType = Convert.ToInt32(rawType);
        amount = read;
        order = Convert.ToInt32(rawOrder);
        return true;
    }

    /// <summary>
    /// Ported math against the game's own answer, on real numbers.
    /// </summary>
    /// <remarks>
    /// <c>GetTrueQuantity()</c> is <c>quantity * quality.AsPercent()</c>, and the suite derives the
    /// same product from a cached read through <see cref="OrbGameMath.AsPercent"/>. The game's side
    /// reaches <c>GetValue()</c>, which would recalculate a dirty record and re-stamp its observers,
    /// so the comparison is skipped unless the record is already clean — at which point
    /// <c>GetValue()</c> is a field return and the call is inert.
    /// </remarks>
    private void CompareTrueQuantity(object entity, Type type, BigDouble ours)
    {
        if (!IsClean(entity, type, "quality")) return;

        var call = NativeAccessorBinder.Call<BigDouble>(type, "GetTrueQuantity");
        if (call is null)
        {
            NoteMissingOracle("ResourceSO.GetTrueQuantity");
            return;
        }

        var theirs = call(entity);
        Record("ResourceSO.GetTrueQuantity", ours, theirs, ours == theirs);
    }

    /// <summary>
    /// An edge is a promise that the identity it carries is in the snapshot. One that resolves to
    /// nothing means either the edge or the registry it points into is wrong, and that shows up
    /// nowhere else — both halves collect cleanly on their own.
    /// </summary>
    private void CheckReferenceEdges(GameWorldState world)
    {
        var checkedEdges = 0;
        var dangling = 0;
        var firstDangling = string.Empty;

        void Check(string label, Guid id)
        {
            if (id == Guid.Empty) return;
            checkedEdges++;
            if (WorldLookup.TryFind(world.IntVariables, id, out _)) return;
            dangling++;
            if (firstDangling.Length == 0) firstDangling = $"{label} → {id}";
        }

        for (var index = 0; index < world.AlchemyTypes.Count; index++)
        {
            Check("AlchemyTypeSO.selectedLevel", world.AlchemyTypes[index].SelectedLevelId);
        }

        for (var index = 0; index < world.DiscoveryTrees.Count; index++)
        {
            var tree = world.DiscoveryTrees[index];
            Check("DiscoveryTreeSO.overrideDiscoveryRerolls", tree.OverrideRerollsId);
            Check("DiscoveryTreeSO.overrideDiscoveryChoices", tree.OverrideChoicesId);
        }

        for (var index = 0; index < world.Resources.Count; index++)
        {
            Check("ResourceSO.levelVariable", world.Resources[index].Reading.LevelVariableId);
        }

        Add(dangling == 0
            ? VerificationFinding.Agree("Reference edges", checkedEdges)
            : VerificationFinding.Disagree(
                "Reference edges", checkedEdges, dangling, $"first {firstDangling}"));
    }

    /// <summary>
    /// The published price against the game's, and the eligibility verdict that price feeds against
    /// the game's own <c>HasEnough()</c> — for structures and upgrades alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the link nothing covered. The cost verifier proves the ported arithmetic, but it
    /// builds its own side from inputs it re-reads and freshens itself, so it never touches
    /// <c>world.PurchaseCosts</c> — the table Auto Buy's projector actually reads and the evaluator
    /// actually compares against. A collection defect that priced everything at nothing passed cost
    /// verification 522 of 522 exact while the planner was buying the world.
    /// </para>
    /// <para>
    /// Upgrades were verified nowhere at all. They reach the same published table through
    /// <c>GetLeveledCostList()</c> rather than <c>GetNextCost()</c>, and both are behind
    /// <c>GetPurchaseCost()</c>, so one walk covers both populations.
    /// </para>
    /// <para>
    /// The eligibility comparison reproduces the evaluator's rule rather than the game's, on purpose.
    /// The evaluator sums the rows that share a resource and compares the total, where
    /// <c>ResourceCostList.HasEnough()</c> checks each entry independently against the full holding —
    /// a deliberate divergence that is stricter than native, and this is the one place it can be
    /// caught costing a real candidate. The reserve floor is left out because it is operator policy
    /// layered above the comparison, not a transcription of anything the game does.
    /// </para>
    /// <para>
    /// Calling <c>GetPurchaseCost()</c> and <c>HasEnough()</c> resolves whatever they touch, which is
    /// why this runs after the staleness survey has already been taken. On an upgrade it also fills
    /// <c>cachedCost</c>, which the game refills itself on the next render with the same number; that
    /// is the same deliberate settling the cost verifier already performs, on demand and never on the
    /// service cycle's path.
    /// </para>
    /// </remarks>
    private void CheckPublishedPurchaseCosts(GameWorldState world)
    {
        var costs = PublishedCostContract.TryResolve(_resolveType);
        if (costs is null)
        {
            NoteMissingOracle("GetPurchaseCost");
            Add(VerificationFinding.Inconclusive(
                "Published cost", "this build does not expose GetPurchaseCost."));
            Add(VerificationFinding.Inconclusive(
                "Affordability parity", "no published cost could be priced to compare against."));
            return;
        }

        var compared = 0;
        var exact = 0;
        var worstError = 0d;
        var worstOffender = string.Empty;
        object? worstEntity = null;
        var worstEntityId = Guid.Empty;
        var worstResource = Guid.Empty;
        var missingEntities = 0;
        var countMismatches = 0;

        var eligibilityCompared = 0;
        var eligibilityAgreed = 0;
        var firstEligibilityGap = string.Empty;

        var exclusionCompared = 0;
        var falseExclusions = 0;
        var namedFalseExclusions = new List<string>();

        foreach (var typeName in new[] { "StructureSO", "UpgradeSO" })
        {
            var type = _resolveType(typeName);
            if (type is null) continue;

            var registry = NativeAccessorBinder.StaticList(type, "All");
            var getGuid = NativeAccessorBinder.Call<Guid>(type, "GetGuid");
            if (registry is null || getGuid is null)
            {
                NoteMissingOracle($"{typeName}.All");
                continue;
            }

            foreach (var entity in registry)
            {
                if (entity is null) continue;

                var entityId = getGuid(entity);

                // Nothing published is the deriver withholding a price it could not complete, which
                // is correct behaviour rather than a mismatch — but it is counted, because "no
                // candidate was ever eligible" and "no candidate was ever priced" look identical from
                // outside and are very different problems.
                var priced = WorldPurchaseCostLookup.TryFindRange(
                    world.PurchaseCosts, entityId, out var start, out var count);
                if (!priced) missingEntities++;

                if (!costs.TryRead(entity, out var theirs, out var theirEnough)) continue;

                // Asked of every entity, priced or not: an entity we refuse to price is an entity we
                // refuse to buy, and whether the game would have sold it is exactly the question.
                if (costs.TryReadGates(entity, out var theirAvailable, out var theirRequirements))
                {
                    exclusionCompared++;
                    var bucket = OurExclusion(
                        world, entityId, priced,
                        priced && HasEnoughByOurRule(world, start, count));

                    if (bucket.Length > 0 && theirAvailable && theirRequirements && theirEnough)
                    {
                        falseExclusions++;
                        if (namedFalseExclusions.Count < 3)
                            namedFalseExclusions.Add($"{Describe(entityId)} [{bucket}]");
                    }
                }

                if (!priced) continue;

                // Compared per resource rather than per row. Both sides may spell one resource across
                // several entries, and the number a consumer of either side reads is the total.
                var ours = new Dictionary<Guid, BigDouble>();
                for (var offset = 0; offset < count; offset++)
                {
                    var published = world.PurchaseCosts[start + offset];
                    ours[published.ResourceId] = ours.TryGetValue(published.ResourceId, out var running)
                        ? running + published.Amount
                        : published.Amount;
                }

                if (ours.Count != theirs.Count)
                {
                    countMismatches++;
                    if (worstOffender.Length == 0)
                    {
                        worstOffender =
                            $"{Describe(entityId)} priced {ours.Count} resources, " +
                            $"the game prices {theirs.Count}";
                    }

                    continue;
                }

                foreach (var entry in ours)
                {
                    if (!theirs.TryGetValue(entry.Key, out var theirAmount))
                    {
                        countMismatches++;
                        if (worstOffender.Length == 0)
                        {
                            worstOffender =
                                $"{Describe(entityId)} prices {entry.Key}, the game does not";
                        }

                        continue;
                    }

                    compared++;
                    if (entry.Value == theirAmount)
                    {
                        exact++;
                        continue;
                    }

                    var error = RelativeError(entry.Value, theirAmount);
                    if (error <= worstError) continue;

                    worstError = error;
                    worstOffender =
                        $"{Describe(entityId)} {entry.Key}: " +
                        VerificationValue.Sides(entry.Value, theirAmount);
                    worstEntity = entity;
                    worstEntityId = entityId;
                    worstResource = entry.Key;
                }

                eligibilityCompared++;
                var ourEnough = HasEnoughByOurRule(world, start, count);
                if (ourEnough == theirEnough)
                {
                    eligibilityAgreed++;
                }
                else if (firstEligibilityGap.Length == 0)
                {
                    firstEligibilityGap =
                        $"{Describe(entityId)}: {VerificationValue.Sides(ourEnough, theirEnough)}";
                }
            }
        }

        var mismatches = compared - exact + countMismatches;
        if (mismatches > 0)
        {
            _detail.Add($"worst {worstOffender}");

            // Every term of the worst price, agreeing terms included. This is one disagreement's
            // breakdown rather than a row per compared fact, and an omitted term that agreed is what
            // a past diagnosis read as proof the arithmetic was at fault.
            if (worstEntity is not null)
                ReportStructureTerms(world, worstEntity, worstEntityId, worstResource);

            Add(VerificationFinding.Disagree(
                "Published cost", compared + countMismatches, mismatches, TakeDetail()));
        }
        else
        {
            Add(missingEntities == 0
                ? VerificationFinding.Agree("Published cost", compared)
                : VerificationFinding.Incomplete(
                    "Published cost", compared, $"{missingEntities} entities were unpriced"));
        }

        if (eligibilityCompared == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Affordability parity", "nothing was comparable. Load a save first."));
        }
        else
        {
            Add(eligibilityAgreed == eligibilityCompared
                ? VerificationFinding.Agree("Affordability parity", eligibilityCompared)
                : VerificationFinding.Disagree(
                    "Affordability parity",
                    eligibilityCompared,
                    eligibilityCompared - eligibilityAgreed,
                    $"first {firstEligibilityGap}"));
        }

        if (exclusionCompared == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Exclusion parity", "no entity exposed both of the game's gates."));
            return;
        }

        Add(falseExclusions == 0
            ? VerificationFinding.Agree("Exclusion parity", exclusionCompared)
            : VerificationFinding.Disagree(
                "Exclusion parity",
                exclusionCompared,
                falseExclusions,
                $"the game would sell {string.Join(", ", namedFalseExclusions)}"));
    }

    /// <summary>
    /// Which bucket the projection would drop a candidate into, from published facts alone. Empty
    /// means it would survive to be planned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evaluator's own order, because the order is what decides which bucket a doubly-excluded
    /// candidate is counted in, and the histogram is read as though each count were a cause.
    /// </para>
    /// <para>
    /// Only the gates the <em>game</em> can be asked about are modelled. <c>KindNotSelected</c> is
    /// configuration, not a fact about the world, and a family the user chose not to buy is not a
    /// candidate the suite got wrong — including it would report the user's own setting as a defect.
    /// </para>
    /// <para>
    /// An entity in neither published registry answers <c>Uncaptured</c> rather than surviving. The
    /// projection cannot plan what it never captured, so that is an exclusion like any other, and it
    /// is the one that would otherwise be invisible in every count.
    /// </para>
    /// </remarks>
    private static string OurExclusion(GameWorldState world, Guid entityId, bool priced, bool affordable)
    {
        if (WorldLookup.TryFind(world.Structures, entityId, out var structure))
        {
            if (!structure.Reading.Unlocked) return "Unavailable";
            if (WorldRequirementEvaluator.Evaluate(
                    world, entityId, WorldRequirementEvaluator.StructureCheckLevel(in structure)) !=
                WorldRequirementVerdict.Met)
            {
                return "RequirementsUnmet";
            }
        }
        else if (WorldLookup.TryFind(world.Upgrades, entityId, out var upgrade))
        {
            if (!upgrade.Reading.Available) return "Unavailable";
            if (WorldRequirementEvaluator.Evaluate(
                    world, entityId, WorldRequirementEvaluator.UpgradeCheckLevel(in upgrade)) !=
                WorldRequirementVerdict.Met)
            {
                return "RequirementsUnmet";
            }

            if (upgrade.IsExhausted) return "Terminal";
        }
        else
        {
            return "Uncaptured";
        }

        if (!priced) return "Unpriceable";
        return affordable ? string.Empty : "Unaffordable";
    }

    /// <summary>
    /// The worst-disagreeing structure's price, term by term, ours beside the game's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A verdict that names an entity and two numbers a hundred orders of magnitude apart says a price
    /// is wrong and says nothing about which of its inputs made it wrong. Every term below is printed
    /// as the collector published it, as the game reads it, and — for the record terms — as a fresh
    /// recomputation would compute it. The third column is what makes the interesting failure legible:
    /// a term where ours equals the recomputation and the game reads something else is a term where
    /// the suite is right about the arithmetic and wrong about the question.
    /// </para>
    /// <para>
    /// <b>The inventory is the point, and it must stay complete.</b> The chain is
    /// </para>
    /// <code>
    /// baseCost
    ///   × resource.GetAttributeCostMod().AsPercent()
    ///   × costPerQuantity ^ (costScalingMod.AsPercent() × committed)
    ///   × GetNextCostMod().AsPercent()          // Max(passiveCostMod, 100/…) × activeCostMod × structureCost%
    ///   then RoundToTwoSigsEarly
    /// </code>
    /// <para>
    /// and every factor in it is printed below. That was not always true: the attribute-cost modifier
    /// was the one term nobody printed, and it was the one term that was wrong. A dump showing every
    /// term agreeing above a price off by 1e133 reads as proof that the arithmetic is at fault, and a
    /// diagnosis was written on exactly that reading. An unprinted term is worse than no dump. The
    /// rounding is the only step with no line of its own, because it is a pure function of the product
    /// and alters nothing outside [10, 100).
    /// </para>
    /// <para>
    /// Structures only. An upgrade prices from a per-level modifier list and touches none of these
    /// terms, so a breakdown of them would be a confident answer to a question nobody asked.
    /// </para>
    /// <para>
    /// <c>GetNextCostMod()</c> and <c>GetAttributeCostMod()</c> are the game calls here, and they are
    /// the same calls the walk above already made on this entity through <c>GetPurchaseCost()</c>
    /// moments earlier. Everything else is a field read or a pure recomputation.
    /// </para>
    /// </remarks>
    private void ReportStructureTerms(GameWorldState world, object entity, Guid entityId, Guid resourceId)
    {
        var structureType = _resolveType("StructureSO");
        if (structureType is null || !structureType.IsInstanceOfType(entity)) return;
        if (!WorldLookup.TryFind(world.Structures, entityId, out var structure)) return;

        var reading = structure.Reading;
        _detail.Add(
            $"worst offender term by term — {Describe(entityId)}, " +
            $"resource {EntityIdentityFormatter.Format(resourceId)}:");

        ReportAuthoredBase(entity, structureType, resourceId);
        ReportAttributeCostMod(world, entity, structureType, resourceId);
        ReportRecordTerm("costScalingMod", entity, structureType, "costScalingMod",
            reading.Modifiers.CostScalingMod);
        ReportRecordTerm("passiveCostMod", entity, structureType, "passiveCostMod",
            reading.Modifiers.PassiveCostMod);
        ReportRecordTerm("activeCostMod", entity, structureType, "activeCostMod",
            reading.Modifiers.ActiveCostMod);

        var globalRecord = FrameGlobalRecord("GetStructureCost", out var globalFailure);
        var globalAccess = globalRecord is null ? null : NativeModifierRecordAccess.For(globalRecord.GetType());
        var ourGlobal = globalAccess is null ? BigDouble.Zero : globalAccess.Fold(globalRecord);
        if (globalRecord is not null && globalAccess is not null &&
            TryReadCache(globalRecord, out var globalMemo, out var globalDirty, out var globalTruth))
        {
            var theirGlobal = GameReads(globalMemo, globalDirty, globalTruth);
            _detail.Add(
                $"  {"structure cost %",-20} " +
                VerificationValue.Sides(
                    OrbGameMath.AsPercent(ourGlobal), OrbGameMath.AsPercent(theirGlobal)) +
                $" recompute={VerificationValue.Format(OrbGameMath.AsPercent(globalTruth))}" +
                Verdict(ourGlobal == theirGlobal));
        }
        else
        {
            _detail.Add($"  {"structure cost %",-20} Player.GetStructureCost {globalFailure}");
        }

        var reference = structureType.GetField("costPerQuantity", Instance);
        var getModifier = reference?.FieldType.GetMethod("GetModifier", Instance, null, Type.EmptyTypes, null);
        var theirType = 0;
        var theirAmount = BigDouble.Zero;
        var theirOrder = 0;
        var readTheirs = reference is not null && getModifier is not null &&
            TryReadGameModifier(entity, reference, getModifier, out theirType, out theirAmount, out theirOrder);
        var readOurs = WorldLookup.TryFind(world.ModifierVariables, reading.CostPerQuantityId, out var ours);
        _detail.Add(
            $"  {"costPerQuantity",-20} " +
            (readOurs
                ? $"ours=[type {ours.ModifierType} " +
                  $"amount {VerificationValue.Format(ours.Amount)} order {ours.Order}] "
                : "ours=[unresolved] ") +
            (readTheirs
                ? $"theirs=[type {theirType} " +
                  $"amount {VerificationValue.Format(theirAmount)} order {theirOrder}]"
                : "theirs=[unreadable]"));

        var ourCommitted = reading.Level + reading.QueuedLevels;
        var quantity = structureType.GetField("quantity", Instance)?.GetValue(entity);
        var queued = structureType.GetField("queuedQuantity", Instance)?.GetValue(entity);
        var theirCommitted = quantity is null || queued is null
            ? (BigDouble?)null
            : new BigDouble(Convert.ToInt64(quantity) + Convert.ToInt64(queued));
        _detail.Add(
            $"  {"committed quantity",-20} " +
            (theirCommitted is { } committed
                ? VerificationValue.Sides(ourCommitted, committed) +
                  Verdict(ourCommitted == committed)
                : $"ours={VerificationValue.Format(ourCommitted)} theirs=[unreadable]"));

        BigDouble? ourNextCostMod = null;
        if (readOurs && Enum.IsDefined(typeof(GameValueModifierType), ours.ModifierType))
        {
            var modifier = new GameValueModifier(
                (GameValueModifierType)ours.ModifierType, ours.Amount, ours.Order);
            ourNextCostMod = GameCostMath.ComputeNextCostMod(
                reading.Modifiers.PassiveCostMod,
                reading.Modifiers.ActiveCostMod,
                in modifier,
                ourCommitted,
                OrbGameMath.AsPercent(ourGlobal));
        }

        var getNextCostMod = structureType.GetMethod(
            "GetNextCostMod", Instance, null, Type.EmptyTypes, null);
        BigDouble? theirNextCostMod = null;
        try
        {
            theirNextCostMod = getNextCostMod?.Invoke(entity, null) as BigDouble?;
        }
        catch (TargetInvocationException)
        {
            // Reported as unreadable below. A breakdown that threw would cost every line after it,
            // which is the whole report.
        }

        _detail.Add(
            $"  {"next cost mod",-20} " +
            (ourNextCostMod is { } left && theirNextCostMod is { } right
                ? VerificationValue.Sides(left, right) + Verdict(left == right)
                : (ourNextCostMod is { } ourMod
                      ? $"ours={VerificationValue.Format(ourMod)} "
                      : "ours=[not computable] ") +
                  (theirNextCostMod is { } theirMod
                      ? $"theirs={VerificationValue.Format(theirMod)}"
                      : "theirs=[unreadable]")));
    }

    /// <summary>One record term: what we published, what the game reads, what a recompute would say.</summary>
    private void ReportRecordTerm(string label, object entity, Type type, string fieldName, BigDouble ours)
    {
        var record = type.GetField(fieldName, Instance)?.GetValue(entity);
        if (record is null || !TryReadCache(record, out var memo, out var isDirty, out var recomputed))
        {
            _detail.Add(
                $"  {label,-20} ours={VerificationValue.Format(ours)} " +
                "(the game's record was unreadable)");
            return;
        }

        var theirs = GameReads(memo, isDirty, recomputed);
        _detail.Add(
            $"  {label,-20} {VerificationValue.Sides(ours, theirs)} " +
            $"recompute={VerificationValue.Format(recomputed)} " +
            $"memo={VerificationValue.Format(memo)} {(isDirty ? "dirty" : "clean")}" +
            Verdict(ours == theirs));
    }

    /// <summary>
    /// The authored cost the whole chain starts from, in every field that carries it and through the
    /// accessor the game reads it with.
    /// </summary>
    /// <remarks>
    /// One input rather than two readings of it: the collector reads <c>valueBig</c> and the game
    /// returns the same field — <c>ResourceTuple.GetValue()</c> is <c>return valueBig;</c>, with no
    /// memo and no dirty flag. The accessor is called anyway rather than trusted, because that claim
    /// is the kind that is cheap to state and expensive to be wrong about, and one column settles it.
    /// The serialized double is printed beside them because it is the wrong field a reader could
    /// plausibly have taken, and it saturates above ~1e308 — so a gap between the columns names that
    /// mistake immediately instead of leaving it to be inferred.
    /// </remarks>
    private void ReportAuthoredBase(object entity, Type structureType, Guid resourceId)
    {
        var costList = structureType.GetField("baseCost", Instance)?.GetValue(entity);
        var entries = costList is null
            ? null
            : costList.GetType().GetField("costs", Instance)?.GetValue(costList) as IList;
        if (entries is null)
        {
            _detail.Add($"  {"authored base",-20} the structure's cost list was unreadable");
            return;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null) continue;

            var entryType = entry.GetType();
            var resource = entryType.GetField("resource", Instance)?.GetValue(entry);
            var getGuid = resource?.GetType().GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
            if (getGuid?.Invoke(resource, null) is not Guid entryResource || entryResource != resourceId)
                continue;

            var big = entryType.GetField("valueBig", Instance)?.GetValue(entry);
            var serialized = entryType.GetField("value", Instance)?.GetValue(entry);
            var accessor = FindNoArg(entryType, "GetValue");
            var read = accessor?.Invoke(entry, null) as BigDouble?;
            _detail.Add(
                $"  {"authored base",-20} valueBig={VerificationValue.Format(big)} " +
                $"serialized={VerificationValue.Format(serialized)} " +
                (read is { } theirs
                    ? $"GetValue={VerificationValue.Format(theirs)}"
                    : "GetValue=[unreadable]") +
                (read is { } right && big is BigDouble ours ? Verdict(ours == right) : string.Empty));
            return;
        }

        _detail.Add($"  {"authored base",-20} the game's cost list does not name {resourceId}");
    }

    /// <summary>
    /// The per-resource attribute-cost modifier: what the deriver multiplied the base cost by, beside
    /// what <c>ResourceSO.GetAttributeCostMod()</c> returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The term the dump used to omit, and the one that was wrong. It is not a record read: the game's
    /// accessor is
    /// </para>
    /// <code>
    /// attributeCostMod / BigDouble.Pow(quality.AsPercent(), Player.GetAttributeQualityBonus())
    /// </code>
    /// <para>
    /// — three published inputs, not one — so the two sides are printed with those inputs beside them.
    /// A quotient that disagrees while its numerator agrees names the divisor without a second run.
    /// </para>
    /// <para>
    /// The resource is taken from the structure's own cost entry rather than looked up in a registry,
    /// so the object asked is the object the game's own chain would have asked.
    /// </para>
    /// </remarks>
    private void ReportAttributeCostMod(
        GameWorldState world,
        object entity,
        Type structureType,
        Guid resourceId)
    {
        const string label = "attribute cost mod";

        if (!WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            _detail.Add($"  {label,-20} the snapshot does not carry resource {resourceId}");
            return;
        }

        var bonusRecord = FrameGlobalRecord("GetAttributeQualityBonus", out var bonusFailure);
        var bonusAccess = bonusRecord is null ? null : NativeModifierRecordAccess.For(bonusRecord.GetType());
        if (bonusAccess is null)
        {
            _detail.Add($"  {label,-20} Player.GetAttributeQualityBonus {bonusFailure}");
            return;
        }

        var bonus = bonusAccess.Fold(bonusRecord);
        var numerator = resource.Reading.Modifiers.AttributeCostMod;
        var quality = resource.Reading.Quality;
        var divisor = BigDouble.Pow(OrbGameMath.AsPercent(quality), bonus);
        var ours = divisor == BigDouble.Zero ? BigDouble.Zero : numerator / divisor;

        var native = NativeCostResource(entity, structureType, resourceId);
        var accessor = native is null ? null : FindNoArg(native.GetType(), "GetAttributeCostMod");
        BigDouble? theirs = null;
        try
        {
            theirs = accessor?.Invoke(native, null) as BigDouble?;
        }
        catch (TargetInvocationException)
        {
            // Reported as unreadable. The rest of the breakdown is worth more than this one line.
        }

        _detail.Add(
            $"  {label,-20} " +
            (theirs is { } mod
                ? VerificationValue.Sides(ours, mod) + " "
                : $"ours={VerificationValue.Format(ours)} theirs=[unreadable] ") +
            $"(record {VerificationValue.Format(numerator)} / " +
            $"Pow(quality {VerificationValue.Format(quality)} as percent, " +
            $"bonus {VerificationValue.Format(bonus)}) = {VerificationValue.Format(divisor)})" +
            (theirs is { } right ? Verdict(ours == right) : string.Empty));
    }

    /// <summary>The game's own <c>ResourceSO</c> for one resource of a structure's authored cost.</summary>
    private static object? NativeCostResource(object entity, Type structureType, Guid resourceId)
    {
        var costList = structureType.GetField("baseCost", Instance)?.GetValue(entity);
        if (costList?.GetType().GetField("costs", Instance)?.GetValue(costList) is not IList entries)
            return null;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var resource = entry?.GetType().GetField("resource", Instance)?.GetValue(entry);
            var getGuid = resource is null ? null : FindNoArg(resource.GetType(), "GetGuid");
            if (getGuid?.Invoke(resource, null) is Guid entryResource && entryResource == resourceId)
                return resource;
        }

        return null;
    }

    private static string Verdict(bool agreed) => agreed ? string.Empty : "  ← DISAGREES";

    private static MethodInfo? FindNoArg(Type type, string name) =>
        type.GetMethod(name, Instance, null, Type.EmptyTypes, null);

    /// <summary>
    /// The evaluator's affordability rule, applied to the published rows: combine the rows sharing a
    /// resource, then compare the total against what that resource can spend.
    /// </summary>
    /// <remarks>
    /// A resource the snapshot does not carry answers false. An unreadable holding is not evidence
    /// that a purchase is affordable, and the evaluator refuses on the same ground.
    /// </remarks>
    private static bool HasEnoughByOurRule(GameWorldState world, int start, int count)
    {
        for (var offset = 0; offset < count; offset++)
        {
            var resourceId = world.PurchaseCosts[start + offset].ResourceId;

            // Only the first row for a resource does the work, exactly as the evaluator skips the
            // rest — otherwise a duplicated resource is compared twice and counted once.
            var isFirst = true;
            for (var earlier = 0; earlier < offset; earlier++)
            {
                if (world.PurchaseCosts[start + earlier].ResourceId != resourceId) continue;
                isFirst = false;
                break;
            }

            if (!isFirst) continue;

            var combined = BigDouble.Zero;
            for (var other = 0; other < count; other++)
            {
                if (world.PurchaseCosts[start + other].ResourceId == resourceId)
                    combined += world.PurchaseCosts[start + other].Amount;
            }

            if (combined == BigDouble.Zero) continue;
            if (!TryFindResource(world, resourceId, out var resource)) return false;

            // This side of the differential deliberately mirrors AutoBuyCycleEvaluator's current
            // exact comparison. The native side applies bandwidth snapping; a difference between
            // them must remain visible here until the evaluator policy is ruled separately.
            if (WorldResourceCoordinate.NativeCostAmount(in resource).CompareTo(combined) < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Both resource populations, in the order the projector searches them. An element-owned resource
    /// is in no global registry, so a check that looked only at the registered table would refuse
    /// every candidate priced in one and call it a disagreement.
    /// </summary>
    private static bool TryFindResource(GameWorldState world, Guid resourceId, out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource)) return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvested))
        {
            resource = harvested.Resource;
            return true;
        }

        resource = default;
        return false;
    }

    /// <summary>An entity named the way an operator reads it, falling back to the identity.</summary>
    private static string Describe(Guid entityId) =>
        EntityIdentityFormatter.Format(entityId);

    /// <summary>
    /// The members needed to ask the game what an entity costs and whether the player can pay.
    /// </summary>
    /// <remarks>
    /// <c>GetPurchaseCost()</c> is declared on both <c>StructureSO</c> and <c>UpgradeSO</c> and routes
    /// to <c>GetNextCost()</c> and <c>GetLeveledCostList()</c> respectively, so the two populations
    /// need one contract rather than two. Every member resolves off the returned list, which is why
    /// the contract binds against the first structure it is handed rather than against a type name.
    /// </remarks>
    private sealed class PublishedCostContract
    {
        private readonly Dictionary<Type, MethodInfo> _getPurchaseCost;
        private readonly Dictionary<Type, (MethodInfo Available, MethodInfo Requirements)> _gates;
        private readonly FieldInfo _entries;
        private readonly FieldInfo _tupleResource;
        private readonly MethodInfo _tupleGetValue;
        private readonly MethodInfo _resourceGetGuid;
        private readonly MethodInfo _hasEnough;

        private PublishedCostContract(
            Dictionary<Type, MethodInfo> getPurchaseCost,
            Dictionary<Type, (MethodInfo Available, MethodInfo Requirements)> gates,
            FieldInfo entries,
            FieldInfo tupleResource,
            MethodInfo tupleGetValue,
            MethodInfo resourceGetGuid,
            MethodInfo hasEnough)
        {
            _getPurchaseCost = getPurchaseCost;
            _gates = gates;
            _entries = entries;
            _tupleResource = tupleResource;
            _tupleGetValue = tupleGetValue;
            _resourceGetGuid = resourceGetGuid;
            _hasEnough = hasEnough;
        }

        internal static PublishedCostContract? TryResolve(Func<string, Type?> resolveType)
        {
            var accessors = new Dictionary<Type, MethodInfo>();
            var gates = new Dictionary<Type, (MethodInfo Available, MethodInfo Requirements)>();
            Type? costListType = null;

            foreach (var typeName in new[] { "StructureSO", "UpgradeSO" })
            {
                var type = resolveType(typeName);
                var accessor = type is null ? null : FindNoArg(type, "GetPurchaseCost");
                if (type is null || accessor is null) continue;

                accessors[type] = accessor;
                costListType ??= accessor.ReturnType;

                // Optional: a build that renamed either gate should cost the exclusion readout and
                // nothing else, because the price comparison is the rung that cannot be replaced.
                var available = FindNoArg(type, "IsAvailable");
                var requirements = FindNoArg(type, "HasMetLevelRequirements");
                if (available is not null && requirements is not null &&
                    available.ReturnType == typeof(bool) && requirements.ReturnType == typeof(bool))
                {
                    gates[type] = (available, requirements);
                }
            }

            if (accessors.Count == 0 || costListType is null) return null;

            var entries = costListType.GetField("costs", Instance);
            var hasEnough = FindNoArg(costListType, "HasEnough");
            if (entries is null || hasEnough is null || hasEnough.ReturnType != typeof(bool)) return null;

            var tupleType = entries.FieldType.IsGenericType
                ? entries.FieldType.GetGenericArguments()[0]
                : null;
            var tupleResource = tupleType?.GetField("resource", Instance);
            var tupleGetValue = tupleType is null ? null : FindNoArg(tupleType, "GetValue");
            if (tupleResource is null || tupleGetValue is null) return null;

            var resourceGetGuid = FindNoArg(tupleResource.FieldType, "GetGuid");
            if (resourceGetGuid is null || resourceGetGuid.ReturnType != typeof(Guid)) return null;

            return new PublishedCostContract(
                accessors, gates, entries, tupleResource, tupleGetValue, resourceGetGuid, hasEnough);
        }

        /// <summary>
        /// The two whole-entity gates the game applies before it will sell anything: whether the
        /// entity is available at all, and whether this level's conditions are met.
        /// </summary>
        /// <remarks>
        /// <c>IsAvailable()</c> walks the prerequisite gate and latches, so it writes. It is called
        /// here anyway, and only because collection already calls it on every structure and upgrade
        /// of every cycle — this adds no write path the suite was not already taking, and it is the
        /// only way to learn whether the flag we published still holds. The alternative was leaving
        /// the largest exclusion bucket in the histogram unverified, which is what it was.
        /// </remarks>
        internal bool TryReadGates(object entity, out bool available, out bool requirementsMet)
        {
            available = false;
            requirementsMet = false;

            Type? type = entity.GetType();
            while (type is not null && !_gates.ContainsKey(type)) type = type.BaseType;
            if (type is null) return false;

            var (isAvailable, hasMetRequirements) = _gates[type];
            try
            {
                available = isAvailable.Invoke(entity, null) is true;
                requirementsMet = hasMetRequirements.Invoke(entity, null) is true;
            }
            catch (TargetInvocationException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The game's price for one entity, keyed by resource, and its own affordability verdict.
        /// False when anything could not be read — an unreadable oracle is not a passing comparison.
        /// </summary>
        internal bool TryRead(
            object entity,
            out Dictionary<Guid, BigDouble> amounts,
            out bool hasEnough)
        {
            amounts = new Dictionary<Guid, BigDouble>();
            hasEnough = false;

            var accessor = Accessor(entity.GetType());
            if (accessor is null) return false;

            var list = accessor.Invoke(entity, null);
            if (list is null) return false;
            if (_entries.GetValue(list) is not IList entries) return false;

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry is null) return false;

                var resource = _tupleResource.GetValue(entry);
                if (resource is null) return false;
                if (_resourceGetGuid.Invoke(resource, null) is not Guid resourceId) return false;
                if (_tupleGetValue.Invoke(entry, null) is not BigDouble value) return false;

                // Duplicated resources are summed, matching how the published table's consumer reads
                // them; the count comparison above still sees the raw row count.
                amounts[resourceId] = amounts.TryGetValue(resourceId, out var existing)
                    ? existing + value
                    : value;
            }

            hasEnough = _hasEnough.Invoke(list, null) is true;
            return true;
        }

        /// <summary>
        /// The accessor for an entity's own type or the nearest base that has one, so a build that
        /// subclasses StructureSO does not silently drop every one of its instances.
        /// </summary>
        private MethodInfo? Accessor(Type? type)
        {
            while (type is not null)
            {
                if (_getPurchaseCost.TryGetValue(type, out var accessor)) return accessor;
                type = type.BaseType;
            }

            return null;
        }
    }

    private void ReportAccessorParity()
    {
        if (_agreements + _disagreements == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Accessor parity", "nothing was comparable. Load a save first."));
        }
        else
        {
            Add(_disagreements == 0
                ? VerificationFinding.Agree("Accessor parity", _agreements)
                : VerificationFinding.Disagree(
                    "Accessor parity",
                    _agreements + _disagreements,
                    _disagreements,
                    $"first {_firstDisagreement}"));
        }

        // The one place the suite answers a question differently from a member of the game it can
        // name, and by design. What is worth saying is not that the divergence happened — it is
        // meant to — but that it was never exercised, because then nothing here proves the branch
        // is still taken. Both ways of not exercising it get the same honest verdict.
        if (_invertedCounters == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Inverted counters",
                "no inverted-counter resource was loaded, so the by-design divergence from " +
                "IsAtMax() was never exercised."));
        }
        else if (_invertedCapacityDivergences == 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Inverted counters",
                $"{_invertedCounters} were checked and none answered isAtCapacity differently " +
                "from IsAtMax(), so the by-design divergence was never exercised."));
        }
        else
        {
            Add(VerificationFinding.Agree("Inverted counters", _invertedCounters));
        }

        // An oracle that did not resolve makes this check weaker without making it fail, so the gap
        // is stated rather than left to be inferred from a comparison count nobody has a baseline for.
        if (_missingOracles.Count > 0)
        {
            Add(VerificationFinding.Inconclusive(
                "Absent oracles",
                $"this build does not expose {string.Join(", ", _missingOracles)}."));
        }
    }

    /// <summary>
    /// Two things at once: whether the suite reads every record as the game's own <c>GetValue()</c>
    /// reads it, and how far the game's memo has drifted from a fresh recomputation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reading is the load-bearing half. What has to be verified is not that the transcription of
    /// <c>Calculate()</c> is right — it is — but that the number the collector publishes is the number
    /// the game will act on. An exact count here is the statement "every number the collector published
    /// this pass is the number the game itself reads out of that record".
    /// </para>
    /// <para>
    /// <b>Both sides are obtainable without resolving anything.</b> <c>Calculate()</c> is
    /// <c>calculatedValue = Adjust(baseValue)</c>, and <c>ModifierRecord.Adjust</c> is pure — an
    /// emptiness check, a concat of the two modifier dictionaries, and static arithmetic in
    /// <c>ValueModifier.AdjustWith</c>. So calling <c>Adjust(baseValue)</c> computes exactly what
    /// <c>Calculate()</c> would store, while storing nothing, clearing no dirty flag and re-stamping
    /// no observer; pairing it with the dirty flag reconstructs <c>GetValue()</c> the same way. That is
    /// the whole measurement, for every record, at zero cost to the game's own state.
    /// </para>
    /// <para>
    /// Three outcomes are worth telling apart. A record whose memo already equals a fresh recompute is
    /// harmless however its flag reads. A dirty record's memo will be replaced the next time the game
    /// asks, so the drift is transient and the game reads the recomputation. A record whose memo is
    /// zero while a recompute is not has never been calculated — and if it carries no modifiers it
    /// never will be, because nothing dirties it. Its permanent reading is the zero, and a collector
    /// that published the recomputation instead would be publishing a number the game will not charge.
    /// </para>
    /// <para>
    /// Runs before anything else in this check, because several things downstream resolve records as
    /// a side effect — collection itself folds modifier records and calls <c>IsAvailable()</c> — and a
    /// survey taken after them would report their work rather than the game's.
    /// </para>
    /// </remarks>
    private CacheStalenessSurvey SurveyCacheStaleness()
    {
        var worst = 0d;
        var worstLabel = string.Empty;
        var worstMemo = BigDouble.Zero;
        var worstRecompute = BigDouble.Zero;
        var surveyed = 0;
        var dirty = 0;
        var wrong = 0;
        var neverCalculated = 0;
        var foldCompared = 0;
        var foldWrong = 0;
        var foldFirst = string.Empty;

        foreach (var typeName in StalenessSurveyTypes)
        {
            var type = _resolveType(typeName);
            if (type is null) continue;

            var registry = NativeAccessorBinder.StaticList(type, "All");
            if (registry is null) continue;

            var records = ValueModifierRecordFields(type);
            if (records.Count == 0) continue;

            foreach (var entity in registry)
            {
                if (entity is null) continue;
                foreach (var field in records)
                {
                    var record = field.GetValue(entity);
                    if (record is null) continue;
                    if (!TryReadCache(record, out var cached, out var isDirty, out var truth)) continue;

                    // The suite's own read, through the very machinery world collection binds, against
                    // the reading the game itself would take. Not against Adjust(baseValue): the game
                    // recomputes only a dirty record, so on a clean one the number it uses is the memo,
                    // whatever a recomputation would say. Anything but exact means the published
                    // snapshot carries numbers the game will not act on.
                    var access = NativeModifierRecordAccess.For(record.GetType());
                    if (access is not null)
                    {
                        foldCompared++;
                        var ours = access.Fold(record);
                        var theirs = GameReads(cached, isDirty, truth);
                        if (ours != theirs)
                        {
                            foldWrong++;
                            if (foldFirst.Length == 0)
                            {
                                foldFirst =
                                    $"{typeName}.{field.Name} {VerificationValue.Sides(ours, theirs)} " +
                                    $"(recompute {VerificationValue.Format(truth)}, " +
                                    $"memo {VerificationValue.Format(cached)}, " +
                                    $"{(isDirty ? "dirty" : "clean")})";
                            }
                        }
                    }

                    surveyed++;
                    if (isDirty) dirty++;
                    if (cached == truth) continue;

                    wrong++;

                    if (cached == BigDouble.Zero)
                    {
                        neverCalculated++;
                        continue;
                    }

                    var apart = DriftOrders(cached, truth);
                    if (apart <= worst) continue;

                    worst = apart;
                    worstLabel = $"{typeName}.{field.Name}";
                    worstMemo = cached;
                    worstRecompute = truth;
                }
            }
        }

        if (surveyed == 0)
        {
            return new CacheStalenessSurvey(
                VerificationFinding.Inconclusive(
                    "Modifier reading", "no modifier record was readable."),
                default);
        }

        var fold = foldWrong == 0
            ? VerificationFinding.Agree("Modifier reading", foldCompared)
            : VerificationFinding.Disagree(
                "Modifier reading", foldCompared, foldWrong, $"first {foldFirst}");

        // Drift in the game, not error in the snapshot: a memo the game has not refreshed is still
        // the number the game acts on, so the fold above compares against it rather than against a
        // recompute. That makes the drift a condition of the run rather than a verdict about it —
        // and it moves by thousands between two calls over an unchanged world, which is exactly the
        // kind of number that invites a false diff when it sits in a comparison body. It rides on
        // the provenance line, both sides in the screen's own notation.
        var widest = worstLabel.Length == 0
            ? string.Empty
            : $"{worstLabel} memo={VerificationValue.Format(worstMemo)} " +
              $"recompute={VerificationValue.Format(worstRecompute)} orders={Orders(worst)}";

        return new CacheStalenessSurvey(
            fold,
            new CacheDrift(surveyed, wrong, dirty, neverCalculated, widest));
    }

    /// <summary>
    /// Reads one record's cached value, its dirty flag, and what <c>Calculate()</c> would store —
    /// the last computed through the record's own pure <c>Adjust</c>, so nothing is written.
    /// </summary>
    private static bool TryReadCache(object record, out BigDouble cached, out bool isDirty, out BigDouble truth)
    {
        cached = BigDouble.Zero;
        truth = BigDouble.Zero;
        isDirty = false;

        var type = record.GetType();
        var cachedField = type.GetField("calculatedValue", Instance);
        var dirtyField = type.GetField("calculationDirty", Instance);
        var baseField = type.GetField("baseValue", Instance);
        if (cachedField is null || dirtyField is null || baseField is null) return false;
        if (cachedField.FieldType != typeof(BigDouble)) return false;
        if (dirtyField.FieldType != typeof(bool) || baseField.FieldType != typeof(double)) return false;

        var adjust = type.GetMethod(
            "Adjust", Instance, null, new[] { typeof(BigDouble) }, null);
        if (adjust is null || adjust.ReturnType != typeof(BigDouble)) return false;

        cached = (BigDouble)cachedField.GetValue(record)!;
        isDirty = (bool)dirtyField.GetValue(record)!;
        truth = (BigDouble)adjust.Invoke(record, new object[] { new BigDouble((double)baseField.GetValue(record)!) })!;
        return true;
    }

    /// <summary>How far apart a memo and its recompute are, in orders of magnitude.</summary>
    /// <remarks>
    /// <para>
    /// A share of the truth cannot rank endgame numbers: this game's deep cost reduction drives a
    /// percentage field toward <c>1e-114</c>, and dividing by that made a stale hundred read as
    /// <c>2.25e118%</c> — a figure that says only that the denominator was small, in a notation with
    /// no ceiling. Orders of magnitude are the same comparison with the exponent taken out, so the
    /// widest record stays a number a reader can hold, and the two sides beside it say the rest.
    /// </para>
    /// <para>
    /// A recompute of exactly zero is unbounded orders away from any memo, and that is the honest
    /// reading rather than an edge to suppress: the game is acting on a number its own recalculation
    /// says is nothing. Values that are not finite cannot be ranked at all and never win.
    /// </para>
    /// </remarks>
    internal static double DriftOrders(BigDouble memo, BigDouble recompute)
    {
        if (recompute == BigDouble.Zero) return memo == BigDouble.Zero ? 0d : double.PositiveInfinity;
        if (memo == BigDouble.Zero) return double.PositiveInfinity;

        var apart = Math.Abs(BigDouble.AbsLog10(memo) - BigDouble.AbsLog10(recompute));
        return double.IsNaN(apart) || double.IsInfinity(apart) ? 0d : apart;
    }

    /// <summary>The drift magnitude as the window line prints it.</summary>
    internal static string Orders(double apart) =>
        double.IsPositiveInfinity(apart) ? "unbounded" : apart.ToString("0.#");

    /// <summary>
    /// How far a cached value sits from the truth, as a share of the truth. Falls back to an absolute
    /// comparison when the true value is zero, where a ratio has nothing to divide by.
    /// </summary>
    private static double RelativeError(BigDouble cached, BigDouble truth)
    {
        if (truth == BigDouble.Zero) return cached == BigDouble.Zero ? 0d : 1d;

        var error = ((cached - truth) / truth).ToDouble();
        if (double.IsNaN(error) || double.IsInfinity(error)) return 1d;

        return Math.Abs(error);
    }

    /// <summary>Every field on a type whose value is a <c>ValueModifierRecord</c>.</summary>
    private static List<FieldInfo> ValueModifierRecordFields(Type type)
    {
        var fields = new List<FieldInfo>();
        foreach (var field in type.GetFields(Instance))
        {
            if (field.FieldType.Name == "ValueModifierRecord") fields.Add(field);
        }

        return fields;
    }

    /// <summary>
    /// Pairs every published row with the live object it was read from, then hands both to a
    /// comparison. A row whose entity is no longer in the registry is passed over rather than
    /// reported — the registry can legitimately change between collection and this walk.
    /// </summary>
    private void CompareEach<TRow>(
        PublicationTable<TRow> table,
        string typeName,
        Action<TRow, object, Type> compare)
        where TRow : struct, IWorldEntity
    {
        var type = _resolveType(typeName);
        if (type is null || table.Count == 0) return;

        var registry = NativeAccessorBinder.StaticList(type, "All");
        var getGuid = NativeAccessorBinder.Call<Guid>(type, "GetGuid");
        if (registry is null || getGuid is null)
        {
            NoteMissingOracle($"{typeName}.All");
            return;
        }

        var byId = new Dictionary<Guid, object>(registry.Count);
        foreach (var entity in registry)
        {
            if (entity is null) continue;
            byId[getGuid(entity)] = entity;
        }

        for (var index = 0; index < table.Count; index++)
        {
            var row = table[index];
            if (!byId.TryGetValue(row.EntityId, out var entity)) continue;

            try
            {
                compare(row, entity, type);
            }
            catch (Exception ex)
            {
                Disagree($"{typeName} comparison threw: {ex.GetBaseException().Message}");
            }
        }
    }

    private void CompareBool(string label, object entity, Type type, string method, bool ours)
    {
        var call = NativeAccessorBinder.Call<bool>(type, method);
        if (call is null)
        {
            NoteMissingOracle(label);
            return;
        }

        var theirs = call(entity);
        Record(label, ours, theirs, ours == theirs);
    }

    private void CompareInt(string label, object entity, Type type, string method, int ours)
    {
        var call = NativeAccessorBinder.Call<int>(type, method);
        if (call is null)
        {
            NoteMissingOracle(label);
            return;
        }

        var theirs = call(entity);
        Record(label, ours, theirs, ours == theirs);
    }

    /// <summary>
    /// Scaling a cost list, against the method it replaced. Every cost reader used to call
    /// <c>ResourceCostList.Multiply(BigDouble)</c> — which allocates a whole second list to hold one
    /// scalar product per entry — and now multiplies each entry as it walks it. This puts both
    /// answers side by side over a live authored list, entry for entry.
    /// </summary>
    /// <remarks>
    /// The factor is the shape a drain modifier takes, a percentage that is neither one nor zero, so
    /// an implementation that dropped the multiply or applied it once for the whole list disagrees
    /// rather than coincidentally matching. Both sides are pure: <c>Multiply</c> is
    /// <c>new ResourceCostList(costs.Select(c =&gt; c.NewValue(c.GetValue() * value)).ToList())</c>
    /// and <c>ResourceTuple.GetValue()</c> is <c>return valueBig;</c>.
    /// </remarks>
    private void CompareCostScaling(object entity, Type type, string field)
    {
        var label = $"ResourceCostList.Multiply({type.Name}.{field})";
        var list = type.GetField(field, Instance)?.GetValue(entity);
        var entries = ReadCostEntries(list);
        if (list is null || entries is null)
        {
            NoteMissingOracle(label);
            return;
        }
        if (entries.Count == 0) return;

        var multiply = list.GetType().GetMethod(
            "Multiply", Instance, null, new[] { typeof(BigDouble) }, null);
        if (multiply is null || multiply.ReturnType != list.GetType())
        {
            NoteMissingOracle(label);
            return;
        }

        var factor = new BigDouble(0.9);
        var theirEntries = ReadCostEntries(multiply.Invoke(list, new object[] { factor }));
        if (theirEntries is null || theirEntries.Count != entries.Count)
        {
            Disagree($"{label}: ours={entries.Count} entries theirs={theirEntries?.Count.ToString() ?? "[unreadable]"}");
            return;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            if (!TryReadCostValue(entries[index], out var authored) ||
                !TryReadCostValue(theirEntries[index], out var theirs))
            {
                NoteMissingOracle(label);
                return;
            }
            var ours = authored * factor;
            Record(label, ours, theirs, ours == theirs);
        }
    }

    private static IList? ReadCostEntries(object? list) =>
        list?.GetType().GetField("costs", Instance)?.GetValue(list) as IList;

    private static bool TryReadCostValue(object? entry, out BigDouble value)
    {
        value = default;
        if (entry is null) return false;
        var read = FindNoArg(entry.GetType(), "GetValue")?.Invoke(entry, null);
        if (read is not BigDouble amount) return false;
        value = amount;
        return true;
    }

    /// <summary>
    /// The count substitution for <c>ModifierRecord.HasActiveElements()</c>, against the method it
    /// stands in for. Both sides are pure: the method is <c>activeModifiers.Count &gt; 0</c>.
    /// </summary>
    private void CompareHasActive(object entity, Type type, string field, int ourCount)
    {
        var label = $"{type.Name}.{field}.HasActiveElements";
        var record = type.GetField(field, Instance)?.GetValue(entity);
        if (record is null)
        {
            NoteMissingOracle(label);
            return;
        }

        var hasActive = record.GetType()
            .GetMethod("HasActiveElements", Instance, null, Type.EmptyTypes, null);
        if (hasActive is null || hasActive.ReturnType != typeof(bool))
        {
            NoteMissingOracle(label);
            return;
        }

        var theirs = (bool)hasActive.Invoke(record, null)!;
        Record(label, ourCount > 0, theirs, ourCount > 0 == theirs);
    }

    /// <summary>
    /// Whether a modifier record can be read through its accessor without that read recalculating.
    /// </summary>
    private static bool IsClean(object entity, Type type, string field)
    {
        var record = type.GetField(field, Instance)?.GetValue(entity);
        var flag = record?.GetType().GetField("calculationDirty", Instance);
        if (flag is null || flag.FieldType != typeof(bool)) return false;

        return !(bool)flag.GetValue(record)!;
    }

    private void NoteMissingOracle(string label)
    {
        if (!_missingOracles.Contains(label)) _missingOracles.Add(label);
    }

    private void Record(string label, object ours, object theirs, bool agreed)
    {
        if (agreed) _agreements++;
        else Disagree($"{label}: {VerificationValue.Sides(ours, theirs)}");
    }

    private void Disagree(string detail)
    {
        _disagreements++;
        if (_firstDisagreement.Length == 0) _firstDisagreement = detail;
    }
}

/// <summary>One identity, and the published table it was read from.</summary>
/// <remarks>
/// The table travels with the identity because the two questions worth asking about a snapshot's
/// identities are answered at different scopes. "Can a lookup return the wrong row" is a question
/// about one table, and "how many rows are extra detail about something already listed" is a
/// question about the schema; an identity with no table attached can answer neither.
/// </remarks>
internal readonly struct WorldIdentitySighting
{
    internal WorldIdentitySighting(string table, Guid id, string keyWithinEntity = "")
    {
        Table = table;
        Id = id;
        KeyWithinEntity = keyWithinEntity ?? string.Empty;
    }

    /// <summary>The snapshot property the row was published under.</summary>
    internal string Table { get; }

    internal Guid Id { get; }

    /// <summary>
    /// The rest of this row's key, written out, and empty when the identity is the whole of it.
    /// </summary>
    /// <remarks>
    /// Read from the row's own <see cref="WorldRowKeyPartAttribute"/> members, so a table that keys
    /// several rows to one owner is scored on what its readers actually search by. Written rather
    /// than packed because the only place it is ever read is a sentence naming the row that
    /// repeated, and a reader cannot act on a hash.
    /// </remarks>
    internal string KeyWithinEntity { get; }
}

/// <summary>The members beyond the identity that one row type keys on, in their declared order.</summary>
internal sealed class WorldRowKey
{
    private static readonly PropertyInfo[] None = Array.Empty<PropertyInfo>();

    private readonly PropertyInfo[] _parts;

    private WorldRowKey(PropertyInfo[] parts) => _parts = parts;

    /// <summary>Whether this row is filed under more than its identity.</summary>
    internal bool IsComposite => _parts.Length > 0;

    internal static WorldRowKey Of(Type row)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));

        var parts = new List<KeyValuePair<int, PropertyInfo>>();
        foreach (var property in row.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var part = property.GetCustomAttribute<WorldRowKeyPartAttribute>();
            if (part is not null) parts.Add(new KeyValuePair<int, PropertyInfo>(part.Order, property));
        }

        if (parts.Count == 0) return new WorldRowKey(None);
        parts.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        var ordered = new PropertyInfo[parts.Count];
        for (var index = 0; index < ordered.Length; index++) ordered[index] = parts[index].Value;
        return new WorldRowKey(ordered);
    }

    /// <summary>This row's key beyond its identity, or the empty string when it has none.</summary>
    internal string Read(object row)
    {
        if (_parts.Length == 0) return string.Empty;

        var text = new StringBuilder();
        for (var index = 0; index < _parts.Length; index++)
        {
            if (index > 0) text.Append(' ');
            text.Append(_parts[index].Name).Append('=').Append(_parts[index].GetValue(row));
        }

        return text.ToString();
    }
}

/// <summary>
/// Every identity in a snapshot, whichever tables it happens to have, each under the table it came
/// from and in table order.
/// </summary>
/// <remarks>
/// Reflection rather than a written-out list of tables, because the check this feeds is a
/// completeness check: a table left out of a hand-written walk would make the collision check
/// quietly weaker instead of failing, and quietly weaker is the outcome worth designing out. This is
/// diagnostic code run once on demand, so the cost of reflecting is irrelevant.
/// </remarks>
internal static class WorldIdentityWalk
{
    /// <summary>
    /// The typed reader below, closed over each table's row type. Reflection finds the tables; it
    /// does not read them — naming <c>Count</c> and the indexer as strings would make the suite's own
    /// container look like a native contract to the manifest audit, and would box a row per read.
    /// </summary>
    private static readonly MethodInfo ReadTable = typeof(WorldIdentityWalk)
        .GetMethod(nameof(Identities), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static IEnumerable<WorldIdentitySighting> Enumerate(GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        var properties = typeof(GameWorldState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var property in properties)
        {
            var tableType = property.PropertyType;
            if (!tableType.IsGenericType) continue;
            if (tableType.GetGenericTypeDefinition() != typeof(PublicationTable<>)) continue;

            var row = tableType.GetGenericArguments()[0];
            if (!typeof(IWorldEntity).IsAssignableFrom(row)) continue;

            var table = property.GetValue(world);
            if (table is null) continue;

            var sightings = (IEnumerable<WorldIdentitySighting>)ReadTable
                .MakeGenericMethod(row)
                .Invoke(null, new[] { table, property.Name })!;

            foreach (var sighting in sightings) yield return sighting;
        }
    }

    private static IEnumerable<WorldIdentitySighting> Identities<TRow>(
        PublicationTable<TRow> table,
        string name)
        where TRow : struct, IWorldEntity
    {
        var key = WorldRowKey.Of(typeof(TRow));
        for (var index = 0; index < table.Count; index++)
        {
            var row = table[index];
            yield return new WorldIdentitySighting(name, row.EntityId, key.Read(row));
        }
    }
}

/// <summary>
/// Whether any lookup in a snapshot is ambiguous, and how much of the snapshot is detail about
/// something it already published.
/// </summary>
/// <remarks>
/// <para>
/// A key is claimed once <em>within</em> a table, because that is the invariant a lookup rests on:
/// two rows under one key in one table make the search for it return an arbitrary member of the
/// pair. That, and a row that went out unidentified, are what this scores.
/// </para>
/// <para>
/// The key is the identity plus whatever the row declares with
/// <see cref="WorldRowKeyPartAttribute"/>. Most tables declare nothing and are keyed by identity
/// alone, which is what <see cref="WorldLookup.TryFind"/> searches them by. Three of them —
/// modifier programs by role, their entries by role, set and position, mastery costs by position —
/// hold several rows per owner by design and are read by composite and range searches, never by
/// identity; scored on the identity alone they reported one line per row after the first, 579 of
/// them, and took a whole run's verdict down for describing the schema correctly.
/// </para>
/// <para>
/// One identity reaching more than one table is the design rather than a defect: a dozen per-owner
/// detail tables key their rows by the owning entity on purpose — spell-recipe authoring by its
/// recipe, purchase-view relations by their candidate, harvest resources by their resource. Scored as
/// collisions they read as "1389 things are broken" when they mean "1389 rows are extra detail about
/// something already listed", and they took the whole run's verdict down with them while every read
/// surface built on the same identities was correct. So the sharing is published as a named,
/// non-scoring line, with the tables that account for most of it named — an aggregate nobody can
/// attribute is exactly what made the old number cost an investigation.
/// </para>
/// <para>
/// The narrow claim that remains is worth having and is live. Relation tables reach publication
/// through <c>WorldRelationTableDeriver.Build</c>, which calls <c>PublicationTable.Create</c>
/// directly and so bypasses the sort-and-reject guard in <see cref="WorldTable.Create"/> that every
/// catalog table passes through. A within-table repeat or an unidentified row there would reach a
/// consumer, and nothing else in the suite would notice.
/// </para>
/// </remarks>
internal static class WorldIdentityAudit
{
    internal static VerificationFinding Audit(GameWorldState world)
    {
        var withinTable = new HashSet<(Guid Id, string Key)>();
        var everywhere = new HashSet<Guid>();
        var sharing = new List<TableIdentityCount>();
        var detail = new List<string>();
        var table = string.Empty;
        var sharedHere = 0;
        var compositeHere = false;
        var total = 0;
        var empties = 0;
        var repeated = 0;
        var shared = 0;
        var composite = 0;

        foreach (var sighting in WorldIdentityWalk.Enumerate(world))
        {
            if (!string.Equals(sighting.Table, table, StringComparison.Ordinal))
            {
                if (sharedHere > 0) sharing.Add(new TableIdentityCount(table, sharedHere));
                if (compositeHere) composite++;
                table = sighting.Table;
                sharedHere = 0;
                compositeHere = false;
                withinTable.Clear();
            }

            total++;
            if (sighting.KeyWithinEntity.Length > 0) compositeHere = true;
            if (sighting.Id == Guid.Empty)
            {
                empties++;
                detail.Add($"{table} published a row with no identity.");
                continue;
            }

            if (!withinTable.Add((sighting.Id, sighting.KeyWithinEntity)))
            {
                repeated++;
                detail.Add(sighting.KeyWithinEntity.Length == 0
                    ? $"{table} published {sighting.Id} twice, so a lookup for it is ambiguous."
                    : $"{table} published {sighting.Id} {sighting.KeyWithinEntity} twice, " +
                      "so a lookup for it is ambiguous.");
                continue;
            }

            if (everywhere.Add(sighting.Id)) continue;
            shared++;
            sharedHere++;
        }

        if (sharedHere > 0) sharing.Add(new TableIdentityCount(table, sharedHere));
        if (compositeHere) composite++;

        return (empties == 0 && repeated == 0
                ? VerificationFinding.Agree(
                    "Identities",
                    total,
                    counts: $"{total} compared, {empties} empty, {repeated} repeated within a table, " +
                        $"{composite} {(composite == 1 ? "table" : "tables")} keyed on more than " +
                        "the identity.")
                : VerificationFinding.Disagree("Identities", total, empties + repeated, detail))
            .WithNote(SharedLine(everywhere.Count, shared, sharing));
    }

    /// <summary>
    /// How much of the snapshot is detail about something it already published, and which tables that
    /// is. Not a verdict: it is the publication schema's shape, and it reads the same on a run that
    /// agreed.
    /// </summary>
    private static string SharedLine(int entities, int shared, List<TableIdentityCount> sharing)
    {
        var line = $"Shared identities: {entities} {(entities == 1 ? "entity" : "entities")}, " +
            $"{shared} detail {(shared == 1 ? "row" : "rows")} filed under one of them";
        if (shared == 0) return line + ".";

        sharing.Sort(static (left, right) => right.Rows.CompareTo(left.Rows));
        var largest = new StringBuilder();
        for (var index = 0; index < sharing.Count && index < 3; index++)
        {
            if (index > 0) largest.Append(", ");
            largest.Append(sharing[index].Table).Append(' ').Append(sharing[index].Rows);
        }

        return line + " (largest: " + largest + ").";
    }

    /// <summary>One table, and how many of its rows were filed under an identity already published.</summary>
    private readonly struct TableIdentityCount
    {
        internal TableIdentityCount(string table, int rows)
        {
            Table = table;
            Rows = rows;
        }

        internal string Table { get; }

        internal int Rows { get; }
    }
}
