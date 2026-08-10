using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.Verification;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The player-facing trigger for verification: click the Runtime action, the suite checks itself against the
/// running game, and reports one verdict per thing checked.
/// </summary>
/// <remarks>
/// <para>
/// Requested explicitly from the Mods Runtime page rather than polled from a keyboard shortcut,
/// because it is a rare, deliberately frame-stalling diagnostic action rather than runtime policy.
/// </para>
/// <para>
/// Passes still report separately — "cost passed, rate failed" is immediately actionable where a
/// single combined verdict would not be — but they all run to completion inside the frame the action was
/// pressed in. Spreading the work across ticks was the earlier design and was wrong for a manual
/// diagnostic twice over: it left every pass reading a different frame's game state, and it hid the
/// run. <b>The stall is the acknowledgement.</b> A player who clicks the action and sees the game hitch
/// knows it happened, without needing to go and read a log to find out.
/// </para>
/// <para>
/// Nothing here is bounded any more, because nothing here needs to be: the entity budget existed to
/// cap per-frame cost, and there is now exactly one frame. Every entity in every registry is checked.
/// </para>
/// <para>
/// Earlier keyboard defaults either raced Mentor or held native gameplay modifiers. The permanent
/// Runtime-page action removes that input surface entirely.
/// </para>
/// </remarks>
internal sealed class AutomataDifferentialVerificationControl : IDifferentialVerificationControl
{
    private readonly Action<string> _report;
    private readonly Action<Action<string>>? _runOverride;
    private readonly Func<GameLifecycleState> _lifecycle;
    private readonly Func<long> _generation;
    private readonly Func<int> _frame;
    private VerificationReport _pending = new();
    private List<string>? _capture;
    private bool _runRequested;
    private long _revision;
    private long _ourTicks;
    private long _theirTicks;

    internal AutomataDifferentialVerificationControl(
        Action<string> report,
        Action<Action<string>>? runOverride = null,
        Func<GameLifecycleState>? lifecycle = null,
        Func<long>? generation = null,
        Func<int>? frame = null)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _runOverride = runOverride;
        _lifecycle = lifecycle ?? (() => GameLifecycleMonitor.Shared.Current.State);
        _generation = generation ?? (() => GameLifecycleMonitor.Shared.Current.Generation);
        _frame = frame ?? (() => UnityEngine.Time.frameCount);
    }

    public bool RunRequested => _runRequested;

    public long Revision => _revision;

    public bool RequestRun()
    {
        if (_runRequested) return false;
        _runRequested = true;
        _revision = checked(_revision + 1);
        return true;
    }

    /// <summary>Runs a requested diagnostic in one frame on the Unity main thread.</summary>
    internal void Tick()
    {
        if (!_runRequested) return;
        _runRequested = false;
        _revision = checked(_revision + 1);
        if (!TryBegin(out _, out var reason))
        {
            Report(reason);
            return;
        }
        Run();
    }

    /// <summary>
    /// Runs the same check the Runtime action runs, and hands back the verdict lines rather than
    /// leaving them in the log. The caller is on the Unity main thread inside the frame it asked
    /// from, so the whole stall is charged to that one call — the acknowledgement a player gets
    /// from watching the game hitch, a reader gets from waiting for the answer.
    /// </summary>
    /// <remarks>
    /// A press queued from the Runtime page is the one thing this refuses: that press runs later in
    /// the same frame, and running twice would report a second verdict measured against caches the
    /// first run had just warmed.
    /// </remarks>
    internal bool TryRunNow(out string[] lines, out string code, out string reason)
    {
        if (!TryBegin(out code, out reason))
        {
            lines = Array.Empty<string>();
            return false;
        }
        var captured = new List<string>();
        _capture = captured;
        try
        {
            Run();
        }
        finally
        {
            _capture = null;
        }
        lines = captured.ToArray();
        return true;
    }

    /// <summary>
    /// Whether the game this check compares itself against exists to be read.
    /// </summary>
    /// <remarks>
    /// Every pass below resolves its type from the loaded assembly and reads that type's static
    /// registry, both of which answer in the Start menu — the assets are loaded when the process is,
    /// long before any save is. So "the registry is empty" was never the question it was asked as,
    /// and with no run behind it the first pass to build a comparison world dereferenced a game
    /// object that does not exist yet. The check reads the live game, so the live game's lifecycle
    /// is its precondition, asked once here for both the Runtime-page press and the tool call.
    /// </remarks>
    private bool TryBegin(out string code, out string reason)
    {
        if (_runRequested)
        {
            code = "already_active";
            reason = "A game math check is already queued from the Runtime page and runs this frame.";
            return false;
        }
        if (GameLifecycleUnavailability.TryDescribe(_lifecycle(), out code, out var lifecycleReason))
        {
            reason = "The game math check compares the suite against a running game, and " +
                lifecycleReason;
            return false;
        }
        code = string.Empty;
        reason = string.Empty;
        return true;
    }

    private void Run()
    {
        if (_runOverride is not null)
        {
            _runOverride(Report);
            return;
        }
        RunEverything();
    }

    /// <summary>Reports one line, and keeps it when a caller asked for the lines themselves.</summary>
    private void Report(string line)
    {
        _capture?.Add(line);
        _report(line);
    }

    /// <summary>
    /// Runs every check, then answers once: the verdict word, what disagreed, and one line saying
    /// what was compared against what and when.
    /// </summary>
    /// <remarks>
    /// Nothing is reported as it happens any more. A response cannot lead with its verdict while its
    /// checks are writing prose into the middle of it, and the reader who has to count verdict words
    /// down ninety lines to learn the answer is the reader this check was failing.
    /// </remarks>
    private void RunEverything()
    {
        _pending = new VerificationReport();
        _ourTicks = 0;
        _theirTicks = 0;

        var whole = Stopwatch.StartNew();

        // Order matters, and only in this direction. The world check is a pure reader, while both
        // ported-math passes deliberately settle dirty flags first so that the two sides compare the
        // same inputs — which leaves every record they touched freshly recalculated. Running the
        // check afterwards would have it survey a cache the verifier had just warmed, and report a
        // staleness figure that says more about the verifier than about the game.
        var collection = RunWorldCollectionCheck();
        foreach (var finding in collection.Findings) _pending.Add(finding);

        RunPass(new ConceptDrainPass());
        RunPass(new SpellLevelPass(compareAffordability: false));
        RunPass(new SpellLevelPass(compareAffordability: true));
        RunPass(new CostPass());
        RunPass(new UpgradeCostPass());
        RunPass(new RatePass());
        RunPass(new PlotQuantityPass());
        RunPass(new RequirementPass(
            "Upgrade requirement", "UpgradeSO", RequirementOwnerShape.UpgradeQueuedLevel));
        RunPass(new RequirementPass(
            "Structure requirement", "StructureSO", RequirementOwnerShape.StructureQuantity));
        RunPass(new RequirementPass(
            "Research requirement", "ResearchSO", RequirementOwnerShape.ResearchRequirementLevel));
        RunPass(new RequirementPass(
            "Prerequisite link tier",
            "PrerequisiteLinkSO",
            RequirementOwnerShape.PrerequisiteLinkTier));
        RunPass(new UsagePrerequisitePass());

        whole.Stop();
        foreach (var line in _pending.Render(Window(collection, whole.Elapsed.TotalMilliseconds)))
        {
            Report(line);
        }
    }

    /// <summary>
    /// One line, the same shape every call, carrying everything that moves between two calls over an
    /// unchanged world: when the numbers were read, how much was read, what the game's own caches
    /// looked like while they were, and what it all cost.
    /// </summary>
    private string Window(in WorldCollectionCheckResult collection, double elapsedMilliseconds)
    {
        var drift = collection.Drift;
        var window =
            $"generation={_generation()} frame={_frame()} " +
            $"entities={collection.Entities} categories={collection.Categories} " +
            $"collect={collection.CollectMilliseconds:0.###}ms " +
            $"ported={Milliseconds(_ourTicks)}ms native={Milliseconds(_theirTicks)}ms " +
            $"elapsed={elapsedMilliseconds:0.###}ms " +
            $"memos={drift.Surveyed} drifted={drift.Drifted} dirty={drift.Dirty} " +
            $"uncalculated={drift.NeverCalculated}";
        return drift.Widest.Length == 0 ? window : window + " widestDrift=" + drift.Widest;
    }

    private static string Milliseconds(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.###");

    /// <summary>Runs one ported-math pass over every entity it can reach, then reports its verdict.</summary>
    private void RunPass(IVerificationPass pass)
    {
        if (!pass.TryBegin(out var entities, out var failure))
        {
            _pending.Add(VerificationFinding.Inconclusive(pass.Subject, failure));
            return;
        }

        // One tick, no entity ceiling: the budgets existed to spread work across frames, and there is
        // no longer anything to spread it across.
        var session = new DifferentialVerificationSession(
            pass.Subject, tickBudget: 1, entityBudget: int.MaxValue);
        session.Start();

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null)
            {
                session.RecordUnverifiable("a registry entry was null");
                continue;
            }

            if (pass.TryVerify(entity, session.Run, session, out var entityFailure))
            {
                session.RecordVerified();
            }
            else
            {
                session.RecordUnverifiable(entityFailure);
            }
        }

        session.EndTick();
        _ourTicks += session.OurElapsedTicks;
        _theirTicks += session.TheirElapsedTicks;
        _pending.Add(session.Complete());
    }

    /// <summary>
    /// Checks world collection itself — binding, traversal, identity, edges, accessor parity, and
    /// cache warmth — against the live game. Reports several findings rather than one, because those
    /// checks fail independently and a combined verdict would hide which of them did.
    /// </summary>
    private WorldCollectionCheckResult RunWorldCollectionCheck()
    {
        try
        {
            return new AutomataWorldCollectionCheck().Run();
        }
        catch (Exception ex)
        {
            // A throw here is itself the finding — the collector reached something on a live object
            // that no stub reproduces — so it is reported rather than allowed to take down the frame
            // the player is standing in. What it must not report is the runtime's own exception text
            // on its own: read alone, "Object reference not set to an instance of an object" is a
            // sentence about the suite's plumbing that a reader mistook for a verdict about the
            // game. The verdict word is the finding's own; the exception type stays in the sentence,
            // where whoever is debugging the suite still has the clue.
            return new WorldCollectionCheckResult(
                new[]
                {
                    VerificationFinding.Inconclusive(
                        "World collection",
                        "it faulted before it could compare anything, so nothing here is a verdict " +
                        $"about the game ({ex.GetBaseException().GetType().Name})."),
                },
                entities: 0,
                categories: 0,
                collectMilliseconds: 0d,
                drift: default);
        }
    }

    /// <summary>One ported chain to check, its entity source, and how to compare a single entity.</summary>
    private interface IVerificationPass
    {
        string Subject { get; }

        bool TryBegin(out IList entities, out string failure);

        bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure);
    }

    private sealed class CostPass : IVerificationPass
    {
        private AutomataCostVerifier? _verifier;

        public string Subject => "Cost";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var structureType = FindType("StructureSO");
            if (structureType is null)
            {
                failure = "the StructureSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataCostVerifier(structureType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected cost contract.";
                return false;
            }

            var all = ReadStaticList(structureType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no structures were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the cost verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    /// <summary>
    /// The upgrade half of the purchase curve, sampled at levels the upgrade is not standing on.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CostPass"/> because the two populations do not share a chain:
    /// structures grow by a single per-quantity modifier, upgrades by a modifier list with exponents.
    /// Folding them into one verdict would leave "cost failed" ambiguous between two ports.
    /// </remarks>
    private sealed class UpgradeCostPass : IVerificationPass
    {
        private AutomataUpgradeCostVerifier? _verifier;

        public string Subject => "Upgrade cost curve";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var upgradeType = FindType("UpgradeSO");
            if (upgradeType is null)
            {
                failure = "the UpgradeSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataUpgradeCostVerifier(upgradeType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected upgrade cost contract.";
                return false;
            }

            var all = ReadStaticList(upgradeType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no upgrades were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the upgrade cost verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    /// <summary>Checks the two plot-node quantity ports against the game's own answers.</summary>
    private sealed class PlotQuantityPass : IVerificationPass
    {
        private AutomataPlotQuantityVerifier? _verifier;

        public string Subject => "Plot node quantity";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var plotNodeType = FindType("PlotNodeSO");
            if (plotNodeType is null)
            {
                failure = "the PlotNodeSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataPlotQuantityVerifier(plotNodeType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected plot quantity contract.";
                return false;
            }

            var all = ReadStaticList(plotNodeType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no plot nodes were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure) =>
            _verifier is not null
                ? _verifier.TryVerify(entity, run, out failure)
                : Unavailable(out failure);

        private static bool Unavailable(out string failure)
        {
            failure = "the plot quantity verifier was not started.";
            return false;
        }
    }

    private sealed class RatePass : IVerificationPass
    {
        private AutomataRateVerifier? _verifier;

        public string Subject => "Rate";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var resourceType = FindType("ResourceSO");
            if (resourceType is null)
            {
                failure = "the ResourceSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRateVerifier(resourceType, FindType("Player"));
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected rate contract.";
                return false;
            }

            var all = ReadStaticList(resourceType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no resources were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the rate verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    private sealed class ConceptDrainPass : IVerificationPass
    {
        private AutomataConceptDrainVerifier? _verifier;
        private GameWorldState? _world;

        public string Subject => "Concept drain";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var recipeType = FindType("AlchemyRecipeSO");
            var instanceType = FindType("AlchemyInstance");
            if (recipeType is null || instanceType is null)
            {
                failure = "the AlchemyRecipeSO or AlchemyInstance type could not be resolved.";
                return false;
            }
            _verifier = new AutomataConceptDrainVerifier(recipeType, instanceType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected Concept drain oracle.";
                return false;
            }
            var all = ReadStaticList(recipeType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no Concept recipes were available. Load a save first.";
                return false;
            }
            var collector = VerificationCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the Concept drain verifier was not started.";
                return false;
            }
            return _verifier.TryVerify(entity, _world, run, session, out failure);
        }
    }

    private sealed class SpellLevelPass : IVerificationPass
    {
        private readonly bool _compareAffordability;
        private AutomataSpellLevelVerifier? _verifier;
        private GameWorldState? _world;

        internal SpellLevelPass(bool compareAffordability) =>
            _compareAffordability = compareAffordability;

        public string Subject => _compareAffordability
            ? "Spell level affordability"
            : "Spell level cost";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var spellType = FindType("SpellRecipeSO");
            var costListType = FindType("ResourceCostList");
            if (spellType is null || costListType is null)
            {
                failure = "the SpellRecipeSO or ResourceCostList type could not be resolved.";
                return false;
            }
            _verifier = new AutomataSpellLevelVerifier(spellType, costListType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected spell level oracle.";
                return false;
            }
            var all = ReadStaticList(spellType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no spell recipes were available. Load a save first.";
                return false;
            }
            var collector = VerificationCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the spell level verifier was not started.";
                return false;
            }
            return _compareAffordability
                ? _verifier.TryVerifyAffordability(entity, _world, run, session, out failure)
                : _verifier.TryVerifyCost(entity, _world, run, session, out failure);
        }
    }

    /// <summary>
    /// Checks the suite's own answer to "may this be bought at its next level" against the game's, for
    /// one kind of owner.
    /// </summary>
    /// <remarks>
    /// One pass per owner kind rather than one combined, because each kind is checked at a level of
    /// its own and a combined verdict would hide which of them disagreed — and because the level
    /// expressions are the likeliest thing to be wrong.
    /// <para>
    /// Its own collector, deliberately. The requirement rows are read once per lifecycle epoch, so a
    /// collector that has already run would skip the read this pass exists to check; a fresh one reads
    /// them for the first time, which is the state a real session's first cycle is in.
    /// </para>
    /// </remarks>
    private sealed class RequirementPass : IVerificationPass
    {
        private readonly string _typeName;
        private readonly RequirementOwnerShape _shape;
        private AutomataRequirementVerifier? _verifier;
        private GameWorldState? _world;

        internal RequirementPass(string subject, string typeName, RequirementOwnerShape shape)
        {
            Subject = subject;
            _typeName = typeName;
            _shape = shape;
        }

        public string Subject { get; }

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var ownerType = FindType(_typeName);
            if (ownerType is null)
            {
                failure = $"the {_typeName} type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRequirementVerifier(ownerType, _shape);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected prerequisite contract.";
                return false;
            }

            var all = ReadStaticList(ownerType, "All");
            if (all is null || all.Count == 0)
            {
                failure = $"no {_typeName} entities were available. Load a save first.";
                return false;
            }

            var collector = VerificationCollector();
            collector.Collect();
            _world = collector.Build();

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the requirement verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, _world, run, session, out failure);
        }
    }

    /// <summary>Checks every captured Concept usage-prerequisite program against its native answer.</summary>
    private sealed class UsagePrerequisitePass : IVerificationPass
    {
        private AutomataUsagePrerequisiteVerifier? _verifier;
        private GameWorldState? _world;

        public string Subject => "Concept usage prerequisite";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var ownerType = FindType("AlchemyRecipeSO");
            if (ownerType is null)
            {
                failure = "the AlchemyRecipeSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataUsagePrerequisiteVerifier(ownerType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected usage-prerequisite oracle.";
                return false;
            }

            var all = ReadStaticList(ownerType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no AlchemyRecipeSO entities were available. Load a save first.";
                return false;
            }

            var collector = VerificationCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the usage-prerequisite verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, _world, run, out failure);
        }
    }

    /// <summary>The throwaway collector a pass reads its comparison world from.</summary>
    /// <remarks>
    /// Deliberately not <see cref="GameWorldCollector.ForSession()"/>. That factory is the named
    /// opt-in into the production purchase-view topology — a process-wide singleton the running
    /// suite's action boundary reads its owning-view admissions from — and a diagnostic collector
    /// that took it would restamp the singleton with its own epoch, leaving every purchase
    /// afterwards refusing on a snapshot this check wrote. A verification pass compares math; it has
    /// no business owning the live topology.
    /// </remarks>
    private static GameWorldCollector VerificationCollector() => new(WorldNativeTypes.Resolve);

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(name, throwOnError: false);
            if (candidate is not null) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Reads a public static list member, the same discovery mechanism Auto Buy already uses for
    /// candidate enumeration.
    /// </summary>
    private static IList? ReadStaticList(Type type, string memberName)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var value = type.GetField(memberName, Static)?.GetValue(null) ??
            type.GetProperty(memberName, Static)?.GetValue(null, null);
        return value as IList;
    }
}
