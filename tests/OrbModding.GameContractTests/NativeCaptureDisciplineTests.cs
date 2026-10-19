using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OrbModding.TestSupport;
using Xunit;
using static OrbModding.GameContractTests.NativeSourceAudit;

namespace OrbModding.GameContractTests;

public sealed class NativeCaptureDisciplineTests
{
    /// <summary>The namespace a source file declares, which is how a capture root finds it.</summary>
    private static readonly Regex NamespacePattern = new(
        "^\\s*namespace\\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>The capture classes a contract may declare.</summary>
    private static readonly string[] CaptureClasses =
        { "grab", "computes", "composite", "enumerating" };

    /// <summary>How often a capture touch happens.</summary>
    private static readonly string[] CaptureCadences =
        { "per-pass", "per-epoch", "request-time" };

    /// <summary>
    /// Capture touches that write while answering, on the pass that runs four times a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every entry is a defect, not a design.</b> Each is a reading the collector takes by making
    /// the game recalculate and cache something, which is a mutation of the state the snapshot claims
    /// to observe. Each is derivable — the manifest says so on the row — and each leaves when the
    /// replacing suite math has a parity pass proving it, never before.
    /// </para>
    /// <para>
    /// Pinned as an exact set rather than a ceiling: an arrival fails here, and so does a departure
    /// that forgets to strike its line. The list grew from twelve to nineteen when the census was
    /// taken by call site rather than by member name, and to twenty when a row that had been
    /// classified from its signature had its body read — the same <c>HasEnough()</c> was reached from
    /// six more readers than the manifest had rows for, because a member-name reconciliation cannot
    /// see a second site. Growth of that kind is the census improving; the rule the doctrine actually
    /// wants is this list being empty.
    /// </para>
    /// <para>
    /// Eleven are one family. <c>Prerequisites.Container.Check()</c> latches <c>available</c>, and every
    /// whole-entity availability or visibility predicate reaches it: <c>StructureSO</c>,
    /// <c>UpgradeSO</c>, <c>ViewSO</c>, <c>RecipeBookSO</c>, <c>CraftingRecipeSO</c>, and
    /// <c>GlyphSO</c> call it directly, <c>ResearchSO</c> through two visibility containers,
    /// <c>RitualSO.HasMetUsageRequirements()</c> and <c>SpellRecipeSO.HasMetUsageRequirements()</c>
    /// through their own usage containers, and
    /// <c>DiscoveryTreeSO.IsVisible()</c> through <c>viewLocation.All(view =&gt; view.IsAvailable())</c>.
    /// They leave together, when the whole-entity container is published and the evaluator answers
    /// for it. The ritual row was the one that had been classified from its signature alone and read
    /// <c>sideEffects: []</c> until its body was audited.
    /// </para>
    /// <para>
    /// Nine more are <c>ResourceCostList.HasEnough()</c>, one per reader that asks it, plus
    /// <c>Spell.HasEnoughResources()</c> which is a one-line call to it. All reach
    /// <c>ValueModifierRecord</c>'s memo through <c>ResourceSO.GetTrueSpend</c>. This family is the
    /// one whose replacement is already proven — <c>WorldResourceCoordinate.HasAmount</c>, compared
    /// against this very call by the spell-level affordability pass — so what these rows are waiting
    /// on is each reader publishing its cost rows for a deriver, not more evidence.
    /// </para>
    /// <para>
    /// The last is the enchantment-target sweep, which is the same latch reached transitively and at
    /// the widest scope: <c>GetRandomList</c> filters the entire structure registry through
    /// <c>StructureSO.IsVisible()</c>, once per enchantment role, per pass. Nothing in the sweep
    /// itself is random or cached — the name is the game's, not a description — so it leaves with the
    /// availability family rather than needing an answer of its own.
    /// </para>
    /// </remarks>
    private static readonly string[] PerPassCaptureWrites =
    {
        "consumable.cost-has-enough-capture",
        "crafting-decision.cost-has-enough-capture",
        "crafting-recipe.visible",
        "discovery-tree-reader.cost-has-enough",
        "discovery-tree-reader.is-visible",
        "generic-discovery.cost-enough-capture",
        "generic-level.cost-has-enough-capture",
        "harvest-lifecycle.cost-has-enough-capture",
        "recipe-book.is-available",
        "research.cost-has-enough-capture",
        "research.is-available",
        "research.is-visible",
        "ritual-lifecycle.cost-has-enough-capture",
        "ritual-lifecycle.ritual-usage-requirements-capture",
        "scribe-relations.target-structure.get-random-list",
        "spell-composition.glyph-is-available-capture",
        "spell-recipe.has-met-usage-requirements",
        "spell.has-enough-resources",
        "structure.is-available",
        "upgrade.is-available",
        "view.is-available",
    };

    /// <summary>
    /// Members a capture-root file selects whose only contract is an action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>place</c> holds one value, and each of these members is genuinely read by world collection
    /// <em>and</em> used by a GameAction — their <c>owners</c> lists say so. The contract is filed
    /// under the transaction because that is the boundary with the stronger obligations, which
    /// leaves the capture-root walk seeing a selector with no capture row behind it.
    /// </para>
    /// <para>
    /// Pinned rather than waved through: this is what a dual-place binding costs today, and the fix
    /// is a schema that lets one contract name both places rather than a longer list here. The list
    /// is reconciled as an exact set, so a new arrival fails until someone decides which it is, and
    /// a name that stops being selected dual-place fails until it is deleted from here. A list that
    /// only forgives is a list that grows quietly.
    /// </para>
    /// <para>
    /// It went from twenty-five names to forty-seven when <see cref="BinderTargetPattern"/> learned
    /// the instance-form selectors, and not one of the twenty-two is new behaviour: every one is a
    /// world binder reading a member the manifest files under a transaction, and every one had been
    /// doing so unseen. The census improving is what growth of that kind means.
    /// </para>
    /// <para>
    /// Seven more arrived the same way when the world categories' private accessor copies moved
    /// onto the binder: Research's queue mode, multi-buy, development cost, develop range and cost
    /// <c>Add</c>, and Ritual's <c>selectedRitual</c> and <c>IsItem</c> were read before, unseen.
    /// </para>
    /// </remarks>
    private static readonly string[] CaptureRootActionSelectors =
    {
        "Add", "AllResourcesVisible", "CanAddInstance", "CanApplyBonusLevels", "CanCastASpell",
        "CanLevel", "GetActionInstances", "GetActivationCost",
        "GetDevelopmentCost", "GetDevelopmentCostAtLevel", "GetFreeBonusLevelsLeft", "GetFreeLevelCost",
        "GetFreeLevels", "GetFreeUsageSlots", "GetLevelCost", "GetMaxLevel",
        "GetMaxSelectedLevel", "GetMaxTypeSlots", "GetMaximumInstances", "GetMultiBuy",
        "GetQueuedLevels", "GetRemainingFreeUsageSlots",
        "GetRemainingMaxUsageSlots", "GetStacks", "GetTypesEquipped",
        "GetUsageCost", "HasMaxLevel", "IsChallengeRestricted", "IsItem",
        "IsResearchQueueMode", "IsWithinDevelopRangeAt",
        "MaximumCostTimes", "MaximumNumberInstances", "activeAlchemy",
        "activeChallenges", "challengeRerollsLeft", "equippedEquipment", "hasCompleteWorldCycle",
        "hasFetchedChallenges", "preferredChallenges", "selectedRitual",
    };

    /// <summary>
    /// A row the suite never touches sits at <c>mirrored</c>, and only such a row does.
    /// </summary>
    /// <remarks>
    /// The census of what capture costs the game is the artifact this whole discipline exists to
    /// make trustworthy, and twenty-six rows nothing reflects on, patches, or calls were filed at
    /// <c>capture</c> — so each declared a per-pass reading, with generated prose asserting a touch
    /// that never happens. The place a row sits in is now decided by its usages rather than left to
    /// whoever wrote it: mirrored-only means mirrored, and any touching usage means a real place.
    /// </remarks>
    [Fact]
    public void OnlyAnUntouchedContractSitsInTheMirroredPlace()
    {
        var manifest = NativeContractManifest.Load();
        var failures = new List<string>();

        foreach (var contract in manifest.Contracts)
        {
            var untouched = contract.Usages.Count == 1 && contract.Usages[0] == "mirrored";
            if (untouched && contract.Place != "mirrored")
            {
                failures.Add(
                    $"{contract.Id}: nothing touches this member, so it belongs at place 'mirrored' "
                        + $"rather than '{contract.Place}'");
            }
            else if (!untouched && contract.Place == "mirrored")
            {
                failures.Add(
                    $"{contract.Id}: place 'mirrored' claims nothing touches this member, but it "
                        + $"declares {string.Join(", ", contract.Usages)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every capture contract says what it makes the game do, how often, and on what evidence.
    /// </summary>
    /// <remarks>
    /// The manifest proved shape and nothing else, so a per-pass sweep over the whole registry and a
    /// single field load were indistinguishable in it — which is how both came to live in capture
    /// unremarked. These six answers are what tells them apart, and they are required rather than
    /// optional so that a new capture contract cannot arrive without someone having thought about
    /// its cost.
    /// </remarks>
    [Fact]
    public void EveryCaptureContractDeclaresWhatItCostsTheGame()
    {
        var manifest = NativeContractManifest.Load();
        var failures = new List<string>();

        foreach (var contract in manifest.Contracts)
        {
            if (contract.Place != "capture")
            {
                if (contract.Capture is not null)
                {
                    failures.Add($"{contract.Id}: only a capture contract may declare capture discipline");
                }
                continue;
            }

            var capture = contract.Capture;
            if (capture is null)
            {
                failures.Add($"{contract.Id}: declares no capture discipline");
                continue;
            }

            if (!CaptureClasses.Contains(capture.Class))
                failures.Add($"{contract.Id}: capture class '{capture.Class}' is not one of the four");
            if (!CaptureCadences.Contains(capture.Cadence))
                failures.Add($"{contract.Id}: capture cadence '{capture.Cadence}' is not one of the three");
            if (string.IsNullOrWhiteSpace(capture.Justification))
                failures.Add($"{contract.Id}: capture justification is empty");
            if (string.IsNullOrWhiteSpace(capture.Evidence))
                failures.Add($"{contract.Id}: capture evidence is empty");
            foreach (var effect in capture.SideEffects)
            {
                if (string.IsNullOrWhiteSpace(effect))
                    failures.Add($"{contract.Id}: a side effect is named by an empty string");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Nothing writes on the pass that runs four times a second, except what is written down.
    /// </summary>
    /// <remarks>
    /// This is the one rule the capture manifest exists to make enforceable. See
    /// <see cref="PerPassCaptureWrites"/> for why the list is not yet empty and what empties it.
    /// </remarks>
    [Fact]
    public void NoPerPassCaptureWritesExceptTheOnesStillOwed()
    {
        var manifest = NativeContractManifest.Load();

        var writing = manifest.Contracts
            .Where(contract => contract.Place == "capture" &&
                contract.Capture?.Cadence == "per-pass" &&
                contract.Capture.SideEffects.Count > 0)
            .Select(contract => contract.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PerPassCaptureWrites.OrderBy(id => id, StringComparer.Ordinal), writing);

        // A write the suite cannot yet replace is debt, and debt that is not derivable is a
        // permanent exception nobody signed off on.
        Assert.All(
            manifest.Contracts.Where(contract => writing.Contains(contract.Id)),
            contract => Assert.True(
                contract.Capture!.Derivable,
                $"{contract.Id}: writes on a capture pass and claims it cannot be derived"));
    }

    /// <summary>
    /// The readers the manifest calls epoch-scoped are the ones the collector runs that way.
    /// </summary>
    /// <remarks>
    /// A cadence nobody reconciles is a wish. The collector marks its structural readers by object
    /// identity at construction so that reordering the array cannot reclassify one; this reads that
    /// same marking out of its source and holds the manifest to it. The marking is on the fields, so
    /// the fields are what the manifest names — one of the nine is a closed generic shared with
    /// several per-pass categories, and its type name would say nothing about which reader was meant.
    /// </remarks>
    [Fact]
    public void TheManifestsEpochScopedReadersAreTheCollectorsOwn()
    {
        var manifest = NativeContractManifest.Load();
        var collector = Path.Combine(
            RepositorySources.Root,
            "src", "Common", "Runtime", "World", "GameWorldCollector.cs");
        Assert.True(File.Exists(collector), $"The collector was not found at {collector}");

        var marked = Regex
            .Matches(
                File.ReadAllText(collector),
                @"ReferenceEquals\(_readers\[index\], (?<field>_[A-Za-z0-9_]+)\)")
            .Select(match => match.Groups["field"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(marked);
        Assert.Equal(
            manifest.CaptureStructuralReaders.OrderBy(name => name, StringComparer.Ordinal),
            marked);
    }

    /// <summary>
    /// Every native member a capture root selects is declared as a capture contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The union is on the member name rather than on the exact contract, because one literal in one
    /// binder call can be satisfied by any contract naming that member — the walk reads source text
    /// and cannot resolve which overload on which type a call meant. That is coarse on purpose for a
    /// first cut: it catches a capture root reaching for something the manifest files as a
    /// transaction, which is the failure that matters.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCaptureRootSelectorNamesACaptureContract()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositorySources.Root;
        Assert.NotEmpty(manifest.CaptureRoots);

        var capturing = manifest.Contracts
            .Where(contract => contract.Place == "capture")
            .SelectMany(contract => contract.Kind == "type"
                ? new[] { contract.Type }
                : new[] { contract.Type, contract.Member! })
            .ToHashSet(StringComparer.Ordinal);
        var declared = manifest.Contracts
            .SelectMany(contract => contract.Kind == "type"
                ? new[] { contract.Type }
                : new[] { contract.Type, contract.Member! })
            .ToHashSet(StringComparer.Ordinal);
        var allowed = CaptureRootActionSelectors.ToHashSet(StringComparer.Ordinal);
        var selectedDualPlace = new HashSet<string>(StringComparer.Ordinal);

        var failures = new List<string>();
        var walked = 0;
        foreach (var file in RepositorySources.SourceFiles("*.cs"))
        {
            var source = File.ReadAllText(file);
            var declaration = NamespacePattern.Match(source);
            if (!declaration.Success) continue;

            var space = declaration.Groups["name"].Value;
            if (!manifest.CaptureRoots.Any(root =>
                    space == root || space.StartsWith(root + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            walked++;
            foreach (var literal in FindLiteralTargets(source).Distinct(StringComparer.Ordinal))
            {
                if (capturing.Contains(literal)) continue;
                if (!declared.Contains(literal)) continue;
                if (allowed.Contains(literal))
                {
                    selectedDualPlace.Add(literal);
                    continue;
                }

                failures.Add(
                    $"{NormalizePath(Path.GetRelativePath(repositoryRoot, file))}: " +
                    $"'{literal}' is selected by a capture root but no capture contract names it");
            }
        }

        // A namespace that moved would walk nothing and report nothing, which reads exactly like a
        // clean sweep. The count is the difference.
        Assert.True(walked > 50, $"Only {walked} files were walked for capture roots.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // Documented as shrink-only, so it is reconciled as an exact set: a name whose dual-place
        // selection is gone has to leave the list, or the list quietly outlives what it forgives.
        Assert.Equal(
            CaptureRootActionSelectors.OrderBy(name => name, StringComparer.Ordinal),
            selectedDualPlace.OrderBy(name => name, StringComparer.Ordinal));
    }
}
